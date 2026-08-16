using System.Threading.Tasks;
using System.Windows;
using MyTools.Common.Theming;
using MyTools.Desktop.Views;

namespace MyTools.Desktop.Services;

public sealed class InputActionCaptureService
{
    private readonly LanguageService languageService;
    private readonly IThemeService themeService;
    private readonly HotKeyManager hotKeyManager;
    private readonly GestureRegistry gestureRegistry;
    private InputActionCaptureWindow? openWindow;

    public InputActionCaptureService(
        LanguageService languageService,
        IThemeService themeService,
        HotKeyManager hotKeyManager,
        GestureRegistry gestureRegistry)
    {
        this.languageService = languageService;
        this.themeService = themeService;
        this.hotKeyManager = hotKeyManager;
        this.gestureRegistry = gestureRegistry;
    }

    public Task<InputActionCaptureResult?> CaptureAsync(InputActionCaptureOptions options)
    {
        CancelOpenWindow();
        hotKeyManager.SuspendAllHotKeys();
        gestureRegistry.SuspendDetection();

        var window = new InputActionCaptureWindow(languageService, themeService);
        window.Configure(options);
        var completed = new TaskCompletionSource<InputActionCaptureResult?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(openWindow, window))
            {
                openWindow = null;
                hotKeyManager.ResumeAllHotKeys();
                gestureRegistry.ResumeDetection();
            }

            completed.TrySetResult(window.Confirmed ? window.Result : null);
        };
        openWindow = window;
        window.Owner = FindOwner();
        window.Show();
        window.Activate();
        return completed.Task;
    }

    private void CancelOpenWindow()
    {
        var existing = openWindow;
        if (existing == null)
        {
            return;
        }

        openWindow = null;
        existing.Close();
    }

    private static Window? FindOwner()
    {
        var current = Application.Current;
        if (current == null)
        {
            return null;
        }

        foreach (Window window in current.Windows)
        {
            if (window is InputActionCaptureWindow || !window.IsVisible)
            {
                continue;
            }

            if (window.IsActive)
            {
                return window;
            }
        }

        return current.MainWindow is { IsVisible: true } main ? main : null;
    }
}
