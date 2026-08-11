using System.Windows;
using System.Windows.Input;

namespace MyTools.Desktop.Views;

internal enum PluginWindowTitleBarDragAction
{
    None,
    ToggleMaximizeRestore,
    DragMove,
    NativeCaptionDrag
}

internal static class PluginWindowTitleBarDragBehavior
{
    public static PluginWindowTitleBarDragAction ResolveMouseLeftButtonDownAction(
        MouseButton changedButton,
        int clickCount,
        bool isInteractiveControlSource)
    {
        if (changedButton != MouseButton.Left || isInteractiveControlSource)
        {
            return PluginWindowTitleBarDragAction.None;
        }

        return clickCount == 2
            ? PluginWindowTitleBarDragAction.ToggleMaximizeRestore
            : PluginWindowTitleBarDragAction.None;
    }

    public static bool ShouldCaptureForPotentialDrag(
        MouseButton changedButton,
        int clickCount,
        bool isInteractiveControlSource)
    {
        return changedButton == MouseButton.Left
            && clickCount == 1
            && !isInteractiveControlSource;
    }

    public static PluginWindowTitleBarDragAction ResolveMouseMoveAction(
        Point dragStartPoint,
        Point currentPoint,
        WindowState windowState,
        MouseButtonState leftButtonState,
        double minimumHorizontalDragDistance,
        double minimumVerticalDragDistance)
    {
        if (leftButtonState != MouseButtonState.Pressed)
        {
            return PluginWindowTitleBarDragAction.None;
        }

        var horizontalDistance = Math.Abs(currentPoint.X - dragStartPoint.X);
        var verticalDistance = Math.Abs(currentPoint.Y - dragStartPoint.Y);
        if (horizontalDistance < minimumHorizontalDragDistance
            && verticalDistance < minimumVerticalDragDistance)
        {
            return PluginWindowTitleBarDragAction.None;
        }

        return windowState == WindowState.Maximized
            ? PluginWindowTitleBarDragAction.NativeCaptionDrag
            : PluginWindowTitleBarDragAction.DragMove;
    }
}
