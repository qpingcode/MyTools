using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Windows;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using MyTools.Common.Localization;

namespace MyTools.Desktop.Services;

public sealed class ProtocolActivationService : IDisposable
{
    internal const string PipeName = "MyTools.Desktop.ProtocolActivation";
    private readonly HubMarketplaceService marketplace;
    private readonly PluginLauncher launcher;
    private readonly ILocalizationService localization;
    private readonly ILogger<ProtocolActivationService> logger;
    private readonly CancellationTokenSource cts = new();
    private readonly Queue<string> pending = new();
    private readonly object gate = new();
    private bool ready;
    private bool disposed;

    public ProtocolActivationService(
        HubMarketplaceService marketplace,
        PluginLauncher launcher,
        ILocalizationService localization,
        ILogger<ProtocolActivationService> logger)
    {
        this.marketplace = marketplace;
        this.launcher = launcher;
        this.localization = localization;
        this.logger = logger;
    }

    public static bool TrySendToRunningInstance(IEnumerable<string> args)
    {
        var uri = args.FirstOrDefault(MyToolsProtocol.IsActivation);
        if (uri == null)
        {
            return false;
        }

        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(2000);
            using var writer = new StreamWriter(client, Encoding.UTF8, leaveOpen: true);
            writer.Write(uri);
            writer.Flush();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void RegisterUriScheme()
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
        {
            return;
        }

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + MyToolsProtocol.Scheme);
            key.SetValue("", "URL:MyTools Protocol");
            key.SetValue("URL Protocol", "");
            using var icon = key.CreateSubKey("DefaultIcon");
            icon.SetValue("", $"\"{executable}\",0");
            using var command = key.CreateSubKey(@"shell\open\command");
            command.SetValue("", $"\"{executable}\" \"%1\"");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unable to register the {Scheme} URL protocol.", MyToolsProtocol.Scheme);
        }
    }

    public void StartListening()
    {
        _ = ListenAsync(cts.Token);
    }

    public void HandleStartup(IEnumerable<string> args)
    {
        lock (gate)
        {
            ready = true;
        }

        foreach (var arg in args)
        {
            EnqueueOrHandle(arg);
        }

        DrainPending();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        cts.Cancel();
        cts.Dispose();
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(cancellationToken);
                using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
                var uri = await reader.ReadToEndAsync(cancellationToken);
                EnqueueOrHandle(uri);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Protocol activation listener failed.");
                try
                {
                    await Task.Delay(500, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private void EnqueueOrHandle(string? raw)
    {
        if (!MyToolsProtocol.IsActivation(raw) || string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

        lock (gate)
        {
            if (!ready)
            {
                pending.Enqueue(raw);
                return;
            }
        }

        _ = HandleAsync(raw);
    }

    private void DrainPending()
    {
        while (true)
        {
            string uri;
            lock (gate)
            {
                if (pending.Count == 0)
                {
                    return;
                }

                uri = pending.Dequeue();
            }

            _ = HandleAsync(uri);
        }
    }

    private async Task HandleAsync(string raw)
    {
        if (!MyToolsProtocol.TryParse(raw, out var request)
            || !request.Action.Equals(MyToolsProtocol.InstallAction, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(request.PluginId))
        {
            return;
        }

        try
        {
            await marketplace.InstallAsync(request.PluginId, request.Version, cts.Token);
            await DispatchAsync(() => launcher.OpenStoreListing(request.PluginId));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to install plugin {PluginId} from a protocol activation.", request.PluginId);
            await DispatchAsync(() => MessageBox.Show(
                localization.GetCaption("Protocol.InstallFailed", "Unable to install the plugin: {{message}}", new { message = ex.Message }),
                localization.GetCaption("Error", "Error"),
                MessageBoxButton.OK,
                MessageBoxImage.Error));
        }
    }

    private static Task DispatchAsync(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return dispatcher.InvokeAsync(action).Task;
    }
}
