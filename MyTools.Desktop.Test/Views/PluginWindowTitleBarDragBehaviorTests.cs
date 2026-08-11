using System.Windows;
using System.Windows.Input;
using MyTools.Desktop.Views;
using NUnit.Framework;

namespace MyTools.Desktop.Test.Views;

[TestFixture]
public class PluginWindowTitleBarDragBehaviorTests
{
    [Test]
    public void ResolveMouseMoveAction_WhenMaximizedAndPastThreshold_UsesNativeCaptionDrag()
    {
        var action = PluginWindowTitleBarDragBehavior.ResolveMouseMoveAction(
            dragStartPoint: new Point(12, 10),
            currentPoint: new Point(20, 15),
            windowState: WindowState.Maximized,
            leftButtonState: MouseButtonState.Pressed,
            minimumHorizontalDragDistance: 4,
            minimumVerticalDragDistance: 4);

        Assert.That(action, Is.EqualTo(PluginWindowTitleBarDragAction.NativeCaptionDrag));
    }

    [Test]
    public void ResolveMouseMoveAction_WhenNormalAndPastThreshold_UsesDragMove()
    {
        var action = PluginWindowTitleBarDragBehavior.ResolveMouseMoveAction(
            dragStartPoint: new Point(12, 10),
            currentPoint: new Point(20, 15),
            windowState: WindowState.Normal,
            leftButtonState: MouseButtonState.Pressed,
            minimumHorizontalDragDistance: 4,
            minimumVerticalDragDistance: 4);

        Assert.That(action, Is.EqualTo(PluginWindowTitleBarDragAction.DragMove));
    }

    [Test]
    public void ResolveMouseLeftButtonDownAction_WhenDoubleClick_TogglesMaximizeRestore()
    {
        var action = PluginWindowTitleBarDragBehavior.ResolveMouseLeftButtonDownAction(
            changedButton: MouseButton.Left,
            clickCount: 2,
            isInteractiveControlSource: false);

        Assert.That(action, Is.EqualTo(PluginWindowTitleBarDragAction.ToggleMaximizeRestore));
    }

    [Test]
    public void ShouldCaptureForPotentialDrag_WhenInteractiveControlSource_ReturnsFalse()
    {
        var shouldCapture = PluginWindowTitleBarDragBehavior.ShouldCaptureForPotentialDrag(
            changedButton: MouseButton.Left,
            clickCount: 1,
            isInteractiveControlSource: true);

        Assert.That(shouldCapture, Is.False);
    }
}
