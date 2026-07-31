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
    private readonly ConcurrentDictionary<string, TaskCompletionSource<string>> pendingRequests = new();
    private Process? process;
    private Task? stdoutTask;
    private Task? stderrTask;

    public event EventHandler<NodePluginEventReceivedEventArgs>? EventReceived;

    public NodePluginProcessHost(NodePluginManifest manifest, ILogger<NodePluginProcessHost> logger)
    {
        this.manifest = manifest;
        this.logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await EnsureStartedAsync(cancellationToken);
    }

    public Task<NodePluginSearchResponse> SearchAsync(string query, string mode, CancellationToken cancellationToken)
    {
        return SendRequestAsync<NodePluginSearchResponse>(
            "search",
            new
            {
                query,
                mode
            },
            cancellationToken);
    }

    public Task<NodePluginActionResponse> InvokeActionAsync(string itemId, string actionId, string query, CancellationToken cancellationToken = default)
    {
        return SendRequestAsync<NodePluginActionResponse>(
            "invokeAction",
            new
            {
                itemId,
                actionId,
                query
            },
            cancellationToken);
    }

    public Task<NodePluginDetailEventResponse> SendDetailEventAsync(string itemId, string eventName, JsonElement? payload, string query, CancellationToken cancellationToken = default)
    {
        return SendRequestAsync<NodePluginDetailEventResponse>(
            "detailEvent",
            new
            {
                itemId,
                eventName,
                query,
                payload
            },
            cancellationToken);
    }

    public Task<NodePluginDetailCallResponse> SendDetailCallAsync(string itemId, string action, JsonElement? payload, string query, CancellationToken cancellationToken = default)
    {
        return SendRequestAsync<NodePluginDetailCallResponse>(
            "detailCall",
            new
            {
                itemId,
                action,
                query,
                payload
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

        await process.StandardInput.WriteLineAsync(requestJson);
        await process.StandardInput.FlushAsync();

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
                if (!root.TryGetProperty("id", out var idElement))
                {
                    HandleNotification(root, line);
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

        if (process is { HasExited: false })
        {
            process.Kill(entireProcessTree: true);
        }

        process?.Dispose();
        startLock.Dispose();
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly Encoding Utf8NoBomEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
}
