using System.Windows;

namespace MyTools.Desktop.Utils;

public static class TopmostMessageBox
{
    public static MessageBoxResult Show(
        string messageBoxText,
        string caption,
        MessageBoxButton button,
        MessageBoxImage icon)
    {
        var application = Application.Current;
        if (!application.Dispatcher.CheckAccess())
        {
            return application.Dispatcher.Invoke(() => Show(messageBoxText, caption, button, icon));
        }

        var owner = application.Windows
            .OfType<Window>()
            .Where(window => window.IsVisible && window.WindowState != WindowState.Minimized)
            .OrderByDescending(window => window.IsActive)
            .ThenByDescending(window => window.Topmost)
            .FirstOrDefault();
        Window? temporaryOwner = null;
        var restoreTopmost = false;

        if (owner == null)
        {
            temporaryOwner = CreateTemporaryOwner();
            owner = temporaryOwner;
            owner.Show();
        }
        else if (!owner.Topmost)
        {
            owner.Topmost = true;
            restoreTopmost = true;
        }

        try
        {
            owner.Activate();
            return MessageBox.Show(owner, messageBoxText, caption, button, icon);
        }
        finally
        {
            if (restoreTopmost)
            {
                owner.Topmost = false;
            }
            temporaryOwner?.Close();
        }
    }

    internal static Window CreateTemporaryOwner() => new()
    {
        Width = 1,
        Height = 1,
        Left = -10_000,
        Top = -10_000,
        WindowStyle = WindowStyle.None,
        ResizeMode = ResizeMode.NoResize,
        ShowInTaskbar = false,
        ShowActivated = true,
        AllowsTransparency = true,
        Background = null,
        Opacity = 0,
        Topmost = true
    };
}


