using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;

namespace MyTools.Desktop.Services;

/// <summary>
/// Restores and persists <see cref="Window"/> position and size per monitor.
/// Opening uses the screen under the mouse cursor.
/// </summary>
public sealed class WindowPlacementService
{
    public const string SearchKey = "search";
    private static readonly TimeSpan SaveDebounce = TimeSpan.FromMilliseconds(400);

    private readonly WindowPlacementStore store;
    private readonly ILogger<WindowPlacementService> logger;

    public WindowPlacementService(ILogger<WindowPlacementService> logger)
    {
        this.logger = logger;
        store = new WindowPlacementStore(logger);
    }

    public static string PluginKey(string pluginId) => $"plugin:{pluginId}";

    public void Restore(Window window, string key)
    {
        try
        {
            var target = DisplayWorkAreas.FromCursor();
            if (target is null)
            {
                return;
            }

            var minWidth = PositiveOrZero(window.MinWidth);
            var minHeight = PositiveOrZero(window.MinHeight);
            var record = store.Find(key, target.Value.DeviceName);
            var fitted = record != null && record.Width > 0 && record.Height > 0
                ? WindowPlacementFit.FromRelative(
                    new DipRect(record.Left, record.Top, record.Width, record.Height),
                    target.Value.Bounds,
                    minWidth,
                    minHeight)
                : WindowPlacementFit.CenterOn(
                    target.Value.Bounds,
                    FallbackSize(window.Width, 1020),
                    FallbackSize(window.Height, 624),
                    minWidth,
                    minHeight);

            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = fitted.Left;
            window.Top = fitted.Top;
            window.Width = fitted.Width;
            window.Height = fitted.Height;
            window.WindowState = ParseState(record?.WindowState);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to restore window placement for {Key}.", key);
        }
    }

    public void Track(Window window, string key)
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background, window.Dispatcher)
        {
            Interval = SaveDebounce
        };

        void SaveNow()
        {
            timer.Stop();
            Save(window, key);
        }

        void ScheduleSave(object? sender, EventArgs e)
        {
            timer.Stop();
            timer.Start();
        }

        timer.Tick += (_, _) => SaveNow();
        window.LocationChanged += ScheduleSave;
        window.SizeChanged += ScheduleSave;
        window.StateChanged += ScheduleSave;
        window.Closing += OnClosing;

        void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            window.Closing -= OnClosing;
            window.LocationChanged -= ScheduleSave;
            window.SizeChanged -= ScheduleSave;
            window.StateChanged -= ScheduleSave;
            SaveNow();
        }
    }

    private void Save(Window window, string key)
    {
        try
        {
            if (!TryCapture(window, out var monitorDeviceName, out var record))
            {
                return;
            }

            store.Save(key, monitorDeviceName, record);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to save window placement for {Key}.", key);
        }
    }

    private static bool TryCapture(Window window, out string monitorDeviceName, out WindowPlacementRecord record)
    {
        record = new WindowPlacementRecord();
        monitorDeviceName = string.Empty;
        Rect bounds;
        var state = window.WindowState;
        if (state == WindowState.Minimized)
        {
            bounds = window.RestoreBounds;
            state = WindowState.Normal;
        }
        else if (state == WindowState.Maximized)
        {
            bounds = window.RestoreBounds;
        }
        else
        {
            bounds = new Rect(window.Left, window.Top, window.Width, window.Height);
        }

        if (bounds.Width <= 0 || bounds.Height <= 0
            || double.IsNaN(bounds.Left) || double.IsNaN(bounds.Top)
            || double.IsNaN(bounds.Width) || double.IsNaN(bounds.Height))
        {
            return false;
        }

        var absolute = new DipRect(bounds.Left, bounds.Top, bounds.Width, bounds.Height);
        var work = DisplayWorkAreas.FromWindow(window) ?? DisplayWorkAreas.FromBounds(absolute);
        if (work is null || string.IsNullOrEmpty(work.Value.DeviceName))
        {
            return false;
        }

        var relative = WindowPlacementFit.ToRelative(absolute, work.Value.Bounds);
        monitorDeviceName = work.Value.DeviceName;
        record.Left = relative.Left;
        record.Top = relative.Top;
        record.Width = relative.Width;
        record.Height = relative.Height;
        record.WindowState = state == WindowState.Maximized
            ? nameof(WindowState.Maximized)
            : nameof(WindowState.Normal);
        return true;
    }

    private static double PositiveOrZero(double value)
    {
        return value > 0 && !double.IsNaN(value) ? value : 0;
    }

    private static double FallbackSize(double value, double fallback)
    {
        return value > 0 && !double.IsNaN(value) ? value : fallback;
    }

    private static WindowState ParseState(string? value)
    {
        return string.Equals(value, nameof(WindowState.Maximized), StringComparison.OrdinalIgnoreCase)
            ? WindowState.Maximized
            : WindowState.Normal;
    }
}
