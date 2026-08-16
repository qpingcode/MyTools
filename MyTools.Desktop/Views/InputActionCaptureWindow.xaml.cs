using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using MyTools.Common.Theming;
using MyTools.Desktop.Models;
using MyTools.Desktop.Services;
using MyTools.Desktop.Themes;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace MyTools.Desktop.Views;

public sealed class InputActionCaptureResult
{
    public required string Kind { get; init; }
    public string? HotKey { get; init; }
    public string? MouseButton { get; init; }
}

public sealed class InputActionCaptureOptions
{
    public bool ShowKeyboard { get; init; } = true;
    public bool ShowMouse { get; init; }
    public string Kind { get; init; } = "hotkey";
    public string? HotKey { get; init; }
    public string? MouseButton { get; init; }
    public bool ShowReset { get; init; }
    public string? DefaultHotKey { get; init; }
    public string? DefaultMouseButton { get; init; }
    public Func<string?, HotKeyInspection> InspectHotKey { get; init; } = static _ => new();
}

public partial class InputActionCaptureWindow
{
    private const string KindHotkey = "hotkey";
    private const string KindMouse = "mouse";
    private const string MouseBack = "XButton1";
    private const string MouseForward = "XButton2";

    private static readonly SolidColorBrush ErrorBrush = CreateErrorBrush();

    private static SolidColorBrush CreateErrorBrush()
    {
        var brush = new SolidColorBrush(Color.FromRgb(0xE0, 0x70, 0x70));
        brush.Freeze();
        return brush;
    }

    private readonly LanguageService languageService;
    private readonly IThemeService themeService;
    private InputActionCaptureOptions options = new();
    private string kind = KindHotkey;
    private string? draftHotKey;
    private string? draftMouseButton;
    private bool canConfirm;
    private bool allowEmptyConfirm;
    private HwndSource? hwndSource;

    public InputActionCaptureWindow(LanguageService languageService, IThemeService themeService)
    {
        this.languageService = languageService;
        this.themeService = themeService;
        InitializeComponent();
    }

    public bool Confirmed { get; private set; }

    public InputActionCaptureResult? Result { get; private set; }

    public void Configure(InputActionCaptureOptions captureOptions)
    {
        options = captureOptions;
        kind = PickInitialKind(captureOptions);
        draftHotKey = captureOptions.HotKey;
        draftMouseButton = captureOptions.MouseButton;
        TitleText.Text = languageService.GetCaption(
            captureOptions.ShowMouse
                ? "InputActionCapture.TitleAction"
                : "InputActionCapture.TitleHotkey",
            captureOptions.ShowMouse ? "Choose action" : "Set shortcut");
        Title = TitleText.Text;
        TabsPanel.Visibility = captureOptions.ShowKeyboard && captureOptions.ShowMouse
            ? Visibility.Visible
            : Visibility.Collapsed;
        ResetButton.Visibility = captureOptions.ShowReset ? Visibility.Visible : Visibility.Collapsed;
        RenderBody();
    }

    private void Window_SourceInitialized(object sender, EventArgs e)
    {
        WindowTitleBarTheme.Apply(this, themeService.CurrentTheme);
        hwndSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        hwndSource?.AddHook(WndProc);
        CaptureSurface.Focus();
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        if (hwndSource != null)
        {
            hwndSource.RemoveHook(WndProc);
            hwndSource = null;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (kind != KindHotkey)
        {
            return IntPtr.Zero;
        }

        if (!WindowSystemMenuFilter.ShouldSuppress(msg, wParam, capturing: true))
        {
            return IntPtr.Zero;
        }

        handled = true;
        ApplyHotKey(WindowSystemMenuFilter.FormatSystemMenuChord());
        return IntPtr.Zero;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
            return;
        }

        if (kind != KindHotkey)
        {
            return;
        }

        var chord = TryFormatHotKey(e);
        if (chord == null)
        {
            if (IsModifier(e.Key == Key.System ? e.SystemKey : e.Key))
            {
                e.Handled = true;
            }

            return;
        }

        e.Handled = true;
        ApplyHotKey(chord);
    }

    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (kind != KindMouse)
        {
            return;
        }

        if (e.OriginalSource is not DependencyObject source || !IsInside(source, CaptureSurface))
        {
            return;
        }

        e.Handled = true;
        draftMouseButton = e.ChangedButton.ToString();
        RenderBody();
    }

    private void KeyboardTab_Click(object sender, RoutedEventArgs e)
    {
        if (kind == KindHotkey)
        {
            return;
        }

        kind = KindHotkey;
        RenderBody();
        CaptureSurface.Focus();
    }

    private void MouseTab_Click(object sender, RoutedEventArgs e)
    {
        if (kind == KindMouse)
        {
            return;
        }

        kind = KindMouse;
        RenderBody();
        CaptureSurface.Focus();
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        if (kind == KindMouse)
        {
            draftMouseButton = options.DefaultMouseButton ?? MouseBack;
            RenderBody();
            return;
        }

        ApplyHotKey(string.IsNullOrWhiteSpace(options.DefaultHotKey) ? null : options.DefaultHotKey, fromReset: true);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();

    private void OkButton_Click(object sender, RoutedEventArgs e) => ConfirmAndClose();

    private void ApplyHotKey(string? hotKey, bool fromReset = false)
    {
        draftHotKey = string.IsNullOrWhiteSpace(hotKey) ? null : hotKey;
        allowEmptyConfirm = fromReset && draftHotKey == null;
        RenderBody();
    }

    private void RenderBody()
    {
        UpdateTabChrome();
        MessageText.Visibility = Visibility.Collapsed;
        MessageText.Text = "";
        canConfirm = false;

        if (kind == KindMouse)
        {
            var label = FormatMouseButton(draftMouseButton);
            CaptureLabel.Text = label
                ?? languageService.GetCaption(
                    "InputActionCapture.RecordingMouse",
                    "Click a mouse button...");
            CaptureLabel.FontStyle = label == null ? FontStyles.Italic : FontStyles.Normal;
            CaptureLabel.Foreground = label == null
                ? (Brush)FindResource("TextTertiaryBrush")
                : (Brush)FindResource("TextPrimaryBrush");
            canConfirm = label != null;
            OkButton.IsEnabled = canConfirm;
            return;
        }

        var inspection = options.InspectHotKey(draftHotKey);
        if (!string.IsNullOrWhiteSpace(draftHotKey) && inspection.ConflictWith != null)
        {
            CaptureLabel.Text = draftHotKey;
            CaptureLabel.FontStyle = FontStyles.Normal;
            CaptureLabel.Foreground = (Brush)FindResource("TextPrimaryBrush");
            MessageText.Visibility = Visibility.Visible;
            MessageText.Foreground = ErrorBrush;
            MessageText.Text = languageService.GetCaption(
                "InputActionCapture.Conflict",
                "Already used by {{name}}",
                new { name = inspection.ConflictWith });
            OkButton.IsEnabled = false;
            return;
        }

        if (!string.IsNullOrWhiteSpace(draftHotKey) && inspection.Reserved)
        {
            CaptureLabel.Text = draftHotKey;
            CaptureLabel.FontStyle = FontStyles.Normal;
            CaptureLabel.Foreground = (Brush)FindResource("TextPrimaryBrush");
            MessageText.Visibility = Visibility.Visible;
            MessageText.Foreground = ErrorBrush;
            MessageText.Text = languageService.GetCaption(
                "InputActionCapture.Reserved",
                "{{hotKey}} is a common Windows shortcut and cannot be used as a global hotkey.",
                new { hotKey = draftHotKey });
            OkButton.IsEnabled = false;
            return;
        }

        CaptureLabel.Text = draftHotKey
            ?? languageService.GetCaption("InputActionCapture.Recording", "Press shortcut...");
        CaptureLabel.FontStyle = draftHotKey == null ? FontStyles.Italic : FontStyles.Normal;
        CaptureLabel.Foreground = draftHotKey == null
            ? (Brush)FindResource("TextTertiaryBrush")
            : (Brush)FindResource("TextPrimaryBrush");
        canConfirm = draftHotKey != null || allowEmptyConfirm;
        OkButton.IsEnabled = canConfirm;
    }

    private void UpdateTabChrome()
    {
        var activeBg = (Brush)FindResource("WindowBackgroundBrush");
        var idleBg = Brushes.Transparent;
        var activeFg = (Brush)FindResource("TextPrimaryBrush");
        var idleFg = (Brush)FindResource("TextSecondaryBrush");
        KeyboardTab.Background = kind == KindHotkey ? activeBg : idleBg;
        MouseTab.Background = kind == KindMouse ? activeBg : idleBg;
        KeyboardTab.Foreground = kind == KindHotkey ? activeFg : idleFg;
        MouseTab.Foreground = kind == KindMouse ? activeFg : idleFg;
    }

    private void ConfirmAndClose()
    {
        if (!canConfirm)
        {
            return;
        }

        Confirmed = true;
        Result = kind == KindMouse
            ? new InputActionCaptureResult { Kind = KindMouse, MouseButton = draftMouseButton }
            : new InputActionCaptureResult { Kind = KindHotkey, HotKey = draftHotKey };
        Close();
    }

    private string? FormatMouseButton(string? mouseButton)
    {
        if (string.IsNullOrWhiteSpace(mouseButton))
        {
            return null;
        }

        return mouseButton switch
        {
            "Left" => languageService.GetCaption("InputActionCapture.MouseLeft", "Left"),
            "Right" => languageService.GetCaption("InputActionCapture.MouseRight", "Right"),
            "Middle" => languageService.GetCaption("InputActionCapture.MouseMiddle", "Middle"),
            MouseForward => languageService.GetCaption("InputActionCapture.MouseForward", "Forward (XButton2)"),
            MouseBack => languageService.GetCaption("InputActionCapture.MouseBack", "Back (XButton1)"),
            _ => mouseButton
        };
    }

    private static string PickInitialKind(InputActionCaptureOptions captureOptions)
    {
        if (captureOptions.Kind == KindMouse && captureOptions.ShowMouse)
        {
            return KindMouse;
        }

        if (captureOptions.ShowKeyboard)
        {
            return KindHotkey;
        }

        return KindMouse;
    }

    internal static string? TryFormatHotKey(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (IsModifier(key) || key is Key.None or Key.DeadCharProcessed or Key.ImeProcessed)
        {
            return null;
        }

        return new HotKeyConfig(key, Keyboard.Modifiers).ToString();
    }

    private static bool IsModifier(Key key) =>
        key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.System;

    private static bool IsInside(DependencyObject source, DependencyObject target)
    {
        var current = source;
        while (current != null)
        {
            if (ReferenceEquals(current, target))
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }
}
