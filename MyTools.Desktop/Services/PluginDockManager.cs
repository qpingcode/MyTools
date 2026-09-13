using System.Collections.ObjectModel;
using System.Windows;
using MyTools.Common.Localization;
using MyTools.Common.Plugins;
using MyTools.Desktop.ViewModels;
using MyTools.Desktop.Views;

namespace MyTools.Desktop.Services;

public sealed class PluginDockManager
{
    private readonly WindowPlacementService windowPlacement;
    private readonly ILocalizationService localization;
    private readonly ObservableCollection<PluginDockItem> items = [];
    private PluginDockWindow? dockWindow;
    private bool placementTracked;

    public PluginDockManager(WindowPlacementService windowPlacement, ILocalizationService localization)
    {
        this.windowPlacement = windowPlacement;
        this.localization = localization;
        Items = new ReadOnlyObservableCollection<PluginDockItem>(items);
        localization.LocaleChanged += (_, _) => RefreshCaptions();
    }

    public ReadOnlyObservableCollection<PluginDockItem> Items { get; }

    public void Dock(PluginWindow window)
    {
        if (!TryGetPluginId(window, out var pluginId))
        {
            return;
        }

        if (items.Any(item => item.PluginId.Equals(pluginId)))
        {
            SyncOpenState(window);
            RefreshDockWindow();
            return;
        }

        var item = new PluginDockItem(
            pluginId,
            window,
            window.PluginDisplayName,
            window.PluginIcon,
            Toggle);
        ApplyItemCaption(item);
        items.Add(item);
        RefreshDockWindow();
    }

    public void Remove(PluginWindow window)
    {
        if (!TryGetPluginId(window, out var pluginId))
        {
            return;
        }

        Remove(pluginId);
    }

    public void Toggle(PluginId pluginId)
    {
        var item = items.FirstOrDefault(candidate => candidate.PluginId.Equals(pluginId));
        if (item == null)
        {
            return;
        }

        if (item.Window.IsWindowOpen)
        {
            item.Window.HideFromDock();
        }
        else
        {
            item.Window.RestoreFromDock();
        }

        SyncOpenState(item.Window);
    }

    public void SyncOpenState(PluginWindow window)
    {
        if (!TryGetPluginId(window, out var pluginId))
        {
            return;
        }

        var item = items.FirstOrDefault(candidate => candidate.PluginId.Equals(pluginId));
        if (item == null)
        {
            return;
        }

        item.RefreshOpenState();
        ApplyItemCaption(item);
    }

    public void Remove(PluginId pluginId)
    {
        var item = items.FirstOrDefault(candidate => candidate.PluginId.Equals(pluginId));
        if (item == null)
        {
            return;
        }

        items.Remove(item);
        RefreshDockWindow();
    }

    private void RefreshDockWindow()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
        {
            return;
        }

        if (items.Count == 0)
        {
            dockWindow?.Hide();
            return;
        }

        EnsureDockWindow();
        if (dockWindow == null)
        {
            return;
        }

        var shouldRestorePlacement = !dockWindow.IsVisible;
        dockWindow.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        if (shouldRestorePlacement)
        {
            windowPlacement.RestorePosition(
                dockWindow,
                WindowPlacementService.PluginDockKey,
                PluginDockLayoutMetrics.DefaultPlacement);
            dockWindow.Show();
            return;
        }

        ClampDockWindow();
    }

    private void EnsureDockWindow()
    {
        if (dockWindow != null)
        {
            return;
        }

        dockWindow = new PluginDockWindow
        {
            DataContext = this
        };
        dockWindow.SizeChanged += (_, _) => ClampDockWindow();
        if (!placementTracked)
        {
            windowPlacement.Track(dockWindow, WindowPlacementService.PluginDockKey);
            placementTracked = true;
        }
    }

    private void ClampDockWindow()
    {
        if (dockWindow == null || !dockWindow.IsVisible)
        {
            return;
        }

        var work = DisplayWorkAreas.FromWindow(dockWindow)
            ?? DisplayWorkAreas.FromCursor()
            ?? DisplayWorkAreas.Primary();
        if (work == null)
        {
            return;
        }

        var width = dockWindow.ActualWidth > 0 ? dockWindow.ActualWidth : dockWindow.DesiredSize.Width;
        var height = dockWindow.ActualHeight > 0 ? dockWindow.ActualHeight : dockWindow.DesiredSize.Height;
        var fitted = WindowPlacementFit.PlaceOnWorkArea(
            new DipRect(dockWindow.Left, dockWindow.Top, width, height),
            work.Value.Bounds,
            width,
            height);
        if (Math.Abs(dockWindow.Left - fitted.Left) < PluginDockLayoutMetrics.PositionSnapTolerance
            && Math.Abs(dockWindow.Top - fitted.Top) < PluginDockLayoutMetrics.PositionSnapTolerance)
        {
            return;
        }

        dockWindow.Left = fitted.Left;
        dockWindow.Top = fitted.Top;
    }

    private void RefreshCaptions()
    {
        foreach (var item in items)
        {
            item.DisplayName = item.Window.PluginDisplayName;
            ApplyItemCaption(item);
        }
    }

    private void ApplyItemCaption(PluginDockItem item)
    {
        item.ActionCaption = item.IsOpen
            ? localization.GetCaption("PluginDock.Hide", "Hide {{name}}", new { name = item.DisplayName })
            : localization.GetCaption("PluginDock.Restore", "Show {{name}}", new { name = item.DisplayName });
    }

    private static bool TryGetPluginId(PluginWindow window, out PluginId pluginId)
    {
        pluginId = null!;
        if (string.IsNullOrWhiteSpace(window.PluginId))
        {
            return false;
        }

        pluginId = new PluginId(window.PluginId);
        return true;
    }
}
