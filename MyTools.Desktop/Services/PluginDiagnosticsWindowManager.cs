using Microsoft.Extensions.DependencyInjection;
using MyTools.Desktop.Views;

namespace MyTools.Desktop.Services;

public sealed class PluginDiagnosticsWindowManager
{
    private readonly IServiceProvider _serviceProvider;
    private PluginDiagnosticsWindow? _window;

    public PluginDiagnosticsWindowManager(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public void Show()
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            ShowCore();
            return;
        }

        dispatcher.Invoke(ShowCore);
    }

    private void ShowCore()
    {
        if (_window is not null)
        {
            if (_window.WindowState == System.Windows.WindowState.Minimized)
            {
                _window.WindowState = System.Windows.WindowState.Normal;
            }

            _window.Activate();
            _window.Focus();
            return;
        }

        var window = ActivatorUtilities.CreateInstance<PluginDiagnosticsWindow>(_serviceProvider);
        window.Closed += (_, _) => _window = null;
        _window = window;
        window.Show();
        window.Activate();
    }
}
