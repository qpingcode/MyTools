using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace MyTools.Plugins.NodePlugins;

internal sealed class NodePluginProcessHost : IDisposable
{
    private readonly NodePluginManifest manifest;
    private readonly ILogger<NodePluginProcessHost> logger;
    private readonly SemaphoreSlim startLock = new(1, 1);
    private readonly SemaphoreSlim writeLock = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<string>> pendingRequests = new();
    private Process? process;
    private Task? stdoutTask;
    private Task? stderrTask;

    public event EventHandler<NodePluginEventReceivedEventArgs>? EventReceived;

    /// <summary>
    /// 宿主能力回调。Node 插件通过 hostCall 向宿主发起请求时调用。
    /// 为 null 表示该插件没有注册宿主能力（如普通搜索/翻译插件）。
    /// </summary>
    public Func<HostCallRequest, CancellationToken, Task<JsonElement>>? HostCallHandler { get; set; }

    public NodePluginProcessHost(NodePluginManifest manifest, ILogger<NodePluginProcessHost> logger)
    {
        this.manifest = manifest;
        this.logger = logger;
    }

    /// <summary>
    /// 向 Node 进程的 stdin 写入一行 JSON。所有写入必须经过此方法以确保互斥，
    /// 避免宿主请求（SendRequestAsync）和 hostCall 响应（HandleHostCallAsync）
    /// 并发写同一个 StreamWriter 导致 "stream is currently in use" 异常。
    /// </summary>
    private async Task WriteToProcessAsync(string json)
    {
        if (process == null)
        {
            return;
        }

        await writeLock.WaitAsync();
        try
        {
            await process.StandardInput.WriteLineAsync(json);
            await process.StandardInput.FlushAsync();
        }
        finally
        {
            writeLock.Release();
        }
    }

    public Task<JsonElement> InitializeAsync(
        string locale,
        string fallbackLocale,
        IReadOnlyDictionary<string, string> messages,
        CancellationToken cancellationToken = default)
    {
        return SendRequestAsync<JsonElement>(
            "initialize",
            new { locale, fallbackLocale, messages },
            cancellationToken);
    }

    public Task<NodePluginSearchResponse> SearchAsync(
        string query, string mode, string locale, string fallbackLocale, CancellationToken cancellationToken)
    {
        return SendRequestAsync<NodePluginSearchResponse>(
            "search",
            new
            {
                query,
                mode,
                locale,
                fallbackLocale
            },
            cancellationToken);
    }

    public Task<NodePluginActionResponse> InvokeActionAsync(
        string itemId, string actionId, string query, string locale, string fallbackLocale,
        CancellationToken cancellationToken = default)
    {
        return SendRequestAsync<NodePluginActionResponse>(
            "invokeAction",
            new
            {
                itemId,
                actionId,
                query,
                locale,
                fallbackLocale
            },
            cancellationToken);
    }

    public Task<NodePluginDetailEventResponse> SendDetailEventAsync(
        string itemId, string eventName, JsonElement? payload, string query, string locale, string fallbackLocale,
        CancellationToken cancellationToken = default)
    {
        return SendRequestAsync<NodePluginDetailEventResponse>(
            "detailEvent",
            new
            {
                itemId,
                eventName,
                query,
                payload,
                locale,
                fallbackLocale
            },
            cancellationToken);
    }

    public Task<NodePluginDetailCallResponse> SendDetailCallAsync(
        string itemId, string action, JsonElement? payload, string query, string locale, string fallbackLocale,
        CancellationToken cancellationToken = default)
    {
        return SendRequestAsync<NodePluginDetailCallResponse>(
            "detailCall",
            new
            {
                itemId,
                action,
                query,
                payload,
                locale,
                fallbackLocale
            },
            cancellationToken);
    }

    private async Task<TResult> SendRequestAsync<TResult>(string method, object parameters, CancellationToken cancellationToken)
    {
        await EnsureStartedAsync(cancellationToken);
        if (process == null)
        {
            throw new InvalidOperationException($"Node plugin process for {manifest.Name} is not available.");
        }

        var id = Guid.NewGuid().ToString("N");
        var completionSource = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        pendingRequests[id] = completionSource;

        using var registration = cancellationToken.Register(() =>
        {
            if (pendingRequests.TryRemove(id, out var pending))
            {
                pending.TrySetCanceled(cancellationToken);
            }
        });

        var requestJson = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id,
            method,
            @params = parameters
        });

        await WriteToProcessAsync(requestJson);

        var responseJson = await completionSource.Task;
        using var document = JsonDocument.Parse(responseJson);
        var root = document.RootElement;
        if (root.TryGetProperty("error", out var errorElement))
        {
            var errorMessage = errorElement.TryGetProperty("message", out var messageElement)
                ? messageElement.GetString()
                : "Unknown node plugin error.";
            throw new InvalidOperationException(errorMessage);
        }

        if (!root.TryGetProperty("result", out var resultElement))
        {
            throw new InvalidOperationException($"Node plugin {manifest.Name} returned a response without result.");
        }

        var result = resultElement.Deserialize<TResult>(JsonOptions);
        if (result == null)
        {
            throw new InvalidOperationException($"Node plugin {manifest.Name} returned an invalid result payload.");
        }

        return result;
    }

    private async Task EnsureStartedAsync(CancellationToken cancellationToken)
    {
        if (process is { HasExited: false })
        {
            return;
        }

        await startLock.WaitAsync(cancellationToken);
        try
        {
            if (process is { HasExited: false })
            {
                return;
            }

            process?.Dispose();
            process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "node",
                    Arguments = $"\"{manifest.EntryFullPath}\"",
                    WorkingDirectory = manifest.PluginDirectory,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardInputEncoding = Utf8NoBomEncoding,
                    StandardOutputEncoding = Utf8NoBomEncoding,
                    StandardErrorEncoding = Utf8NoBomEncoding,
                    UseShellExecute = false,
                    CreateNoWindow = true
                },
                EnableRaisingEvents = true
            };

            process.Exited += (_, _) => OnProcessExited();

            if (!process.Start())
            {
                throw new InvalidOperationException($"Failed to start node plugin process for {manifest.Name}.");
            }

            stdoutTask = Task.Run(ReadStdOutLoopAsync, CancellationToken.None);
            stderrTask = Task.Run(ReadStdErrLoopAsync, CancellationToken.None);
            logger.LogInformation("Started node plugin process {PluginName} with pid {Pid}.", manifest.Name, process.Id);
        }
        finally
        {
            startLock.Release();
        }
    }

    private async Task ReadStdOutLoopAsync()
    {
        if (process == null)
        {
            return;
        }

        try
        {
            while (!process.HasExited)
            {
                var line = await process.StandardOutput.ReadLineAsync();
                if (line == null)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;

                // Node → 宿主的消息分三类：
                // 1. 有 method 无 id → 通知（publish），走 HandleNotification
                // 2. 有 method 有 id → 请求（hostCall），宿主处理后写回响应
                // 3. 无 method 有 id → 宿主之前发起的请求的响应
                if (root.TryGetProperty("method", out var methodElement))
                {
                    var methodName = methodElement.GetString();
                    var messageId = root.TryGetProperty("id", out var reqIdElement)
                        ? reqIdElement.GetString()
                        : null;

                    if (string.Equals(methodName, "hostCall", StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(messageId))
                    {
                        _ = HandleHostCallAsync(root, messageId!);
                        continue;
                    }

                    HandleNotification(root, line);
                    continue;
                }

                if (!root.TryGetProperty("id", out var idElement))
                {
                    continue;
                }

                var id = idElement.GetString();
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                if (pendingRequests.TryRemove(id, out var completionSource))
                {
                    completionSource.TrySetResult(line);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Node plugin stdout loop failed for {PluginName}.", manifest.Name);
            FailPendingRequests(ex);
        }
    }

    private async Task ReadStdErrLoopAsync()
    {
        if (process == null)
        {
            return;
        }

        while (!process.HasExited)
        {
            var line = await process.StandardError.ReadLineAsync();
            if (line == null)
            {
                break;
            }

            if (!string.IsNullOrWhiteSpace(line))
            {
                logger.LogWarning("Node plugin {PluginName}: {Message}", manifest.Name, line);
            }
        }
    }

    private void OnProcessExited()
    {
        var exitCode = process?.ExitCode;
        var exception = new InvalidOperationException($"Node plugin process {manifest.Name} exited unexpectedly with code {exitCode}.");
        FailPendingRequests(exception);
    }

    private void FailPendingRequests(Exception exception)
    {
        foreach (var pendingRequest in pendingRequests)
        {
            if (pendingRequests.TryRemove(pendingRequest.Key, out var completionSource))
            {
                completionSource.TrySetException(exception);
            }
        }
    }

    private async Task HandleHostCallAsync(JsonElement root, string id)
    {
        if (process == null)
        {
            return;
        }

        string responseJson;
        try
        {
            if (HostCallHandler == null)
            {
                throw new InvalidOperationException("No host call handler registered for this plugin.");
            }

            var callMethod = root.TryGetProperty("params", out var paramsElement)
                             && paramsElement.TryGetProperty("method", out var innerMethod)
                ? innerMethod.GetString() ?? ""
                : "";
            var callParams = paramsElement.ValueKind == JsonValueKind.Object
                             && paramsElement.TryGetProperty("params", out var innerParams)
                             && innerParams.ValueKind == JsonValueKind.Object
                ? innerParams.Clone()
                : JsonSerializer.SerializeToElement(new { });

            var result = await HostCallHandler(new HostCallRequest(callMethod, callParams), CancellationToken.None);

            responseJson = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id,
                result,
            });
        }
        catch (Exception ex)
        {
            responseJson = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id,
                error = new { code = -32000, message = ex.Message },
            });
            logger.LogError(ex, "HostCall failed for plugin {PluginName}.", manifest.Name);
        }

        try
        {
            await WriteToProcessAsync(responseJson);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to write hostCall response for plugin {PluginName}.", manifest.Name);
        }
    }

    private void HandleNotification(JsonElement root, string line)
    {
        if (!root.TryGetProperty("method", out var methodElement)
            || !string.Equals(methodElement.GetString(), "publish", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("Node plugin {PluginName} emitted an unsupported notification: {Message}", manifest.Name, line);
            return;
        }

        if (!root.TryGetProperty("params", out var paramsElement)
            || !paramsElement.TryGetProperty("subjectId", out var subjectIdElement))
        {
            logger.LogWarning("Node plugin {PluginName} emitted a publish notification without subjectId: {Message}", manifest.Name, line);
            return;
        }

        var subjectId = subjectIdElement.GetString();
        if (string.IsNullOrWhiteSpace(subjectId))
        {
            return;
        }

        var payload = paramsElement.TryGetProperty("payload", out var payloadElement)
            ? payloadElement.Clone()
            : JsonSerializer.SerializeToElement(new { });
        EventReceived?.Invoke(this, new NodePluginEventReceivedEventArgs
        {
            SubjectId = subjectId,
            Payload = payload
        });
    }

    public void Dispose()
    {
        foreach (var pendingRequest in pendingRequests)
        {
            if (pendingRequests.TryRemove(pendingRequest.Key, out var completionSource))
            {
                completionSource.TrySetCanceled();
            }
        }

        if (process is { } currentProcess)
        {
            if (!currentProcess.HasExited)
            {
                currentProcess.Kill(entireProcessTree: true);
            }

            currentProcess.WaitForExit();
            currentProcess.Dispose();
            process = null;
        }

        startLock.Dispose();
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly Encoding Utf8NoBomEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
}
