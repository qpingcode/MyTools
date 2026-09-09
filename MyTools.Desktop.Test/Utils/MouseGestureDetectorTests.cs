using Microsoft.Extensions.Logging.Abstractions;
using MyTools.Desktop.Services;
using MyTools.Desktop.Utils;
using NUnit.Framework;

namespace MyTools.Desktop.Test.Utils;

[TestFixture]
public sealed class MouseGestureDetectorTests
{
    [TestCase("Shell_TrayWnd")]
    [TestCase("Shell_SecondaryTrayWnd")]
    public void TaskbarWindowDetector_RecognizesTaskbarClasses(string className)
    {
        Assert.That(TaskbarWindowDetector.IsTaskbarClassName(className), Is.True);
    }

    [TestCase("MSTaskListWClass")]
    [TestCase("CabinetWClass")]
    [TestCase("")]
    public void TaskbarWindowDetector_RejectsOtherClasses(string className)
    {
        Assert.That(TaskbarWindowDetector.IsTaskbarClassName(className), Is.False);
    }

    [Test]
    public void TaskbarRightClick_PassesThroughEntireButtonSequence()
    {
        var hook = new TestMouseHook();
        using var detector = CreateDetector(new MouseHelper(), hook, _ => true);
        var listenerThread = Start(detector);
        Assert.That(hook.WaitForStart(TimeSpan.FromSeconds(2)), Is.True);

        var down = hook.Raise(Native.MouseMsg.WM_RBUTTONDOWN, new Native.POINT { x = 10, y = 20 });
        var move = hook.Raise(Native.MouseMsg.WM_MOUSEMOVE, new Native.POINT { x = 20, y = 20 });
        var up = hook.Raise(Native.MouseMsg.WM_RBUTTONUP, new Native.POINT { x = 20, y = 20 });

        Assert.Multiple(() =>
        {
            Assert.That(down.Handled, Is.False);
            Assert.That(move.Handled, Is.False);
            Assert.That(up.Handled, Is.False);
        });

        detector.Stop();
        Assert.That(listenerThread.Join(TimeSpan.FromSeconds(2)), Is.True);
    }

    [Test]
    public void Registry_EnableAndDisable_ControlsListenerLifetime()
    {
        var hook = new TestMouseHook();
        var mouseHelper = new MouseHelper();
        var detector = CreateDetector(mouseHelper, hook);
        using var registry = new GestureRegistry(NullLogger<GestureRegistry>.Instance, detector);

        registry.EnableDetection([], mouseHelper);
        Assert.That(hook.WaitForStart(TimeSpan.FromSeconds(2)), Is.True);

        registry.DisableDetection();
        Assert.That(detector.IsRunning, Is.False);
        Assert.That(hook.DisposeCount, Is.EqualTo(1));

        hook.ResetStarted();
        registry.EnableDetection([], mouseHelper);
        Assert.That(hook.WaitForStart(TimeSpan.FromSeconds(2)), Is.True);

        registry.DisableDetection();
        Assert.That(detector.IsRunning, Is.False);
        Assert.That(hook.StartCount, Is.EqualTo(2));
        Assert.That(hook.DisposeCount, Is.EqualTo(2));
    }

    [Test]
    public void Stop_UnblocksListenerAndAllowsRestart()
    {
        var hook = new TestMouseHook();
        using var detector = CreateDetector(new MouseHelper(), hook);

        var firstThread = Start(detector);
        Assert.That(hook.WaitForStart(TimeSpan.FromSeconds(2)), Is.True);

        detector.Stop();

        Assert.That(firstThread.Join(TimeSpan.FromSeconds(2)), Is.True);
        Assert.That(hook.DisposeCount, Is.EqualTo(1));

        hook.ResetStarted();
        var secondThread = Start(detector);
        Assert.That(hook.WaitForStart(TimeSpan.FromSeconds(2)), Is.True);

        detector.Stop();

        Assert.That(secondThread.Join(TimeSpan.FromSeconds(2)), Is.True);
        Assert.That(hook.StartCount, Is.EqualTo(2));
        Assert.That(hook.DisposeCount, Is.EqualTo(2));
    }

    private static MouseGestureDetector CreateDetector(
        MouseHelper mouseHelper,
        IMouseHook hook,
        Func<Native.POINT, bool>? isTaskbarAt = null)
        => new(
            mouseHelper,
            NullLogger<MouseGestureDetector>.Instance,
            NullLogger<global::MyTools.Desktop.Views.MouseTrailWindow>.Instance,
            hook,
            isTaskbarAt);

    private static Thread Start(MouseGestureDetector detector)
    {
        var thread = new Thread(detector.Start) { IsBackground = true };
        thread.Start();
        return thread;
    }

    private sealed class TestMouseHook : IMouseHook
    {
        private readonly ManualResetEventSlim started = new(false);

        public event MouseHook.MouseHookEventHandler? MouseHookEvent;

        public int StartCount { get; private set; }
        public int DisposeCount { get; private set; }

        public void StartListening()
        {
            StartCount++;
            started.Set();
        }

        public bool WaitForStart(TimeSpan timeout) => started.Wait(timeout);

        public void ResetStarted() => started.Reset();

        public MouseHook.MouseHookEventArgs Raise(Native.MouseMsg message, Native.POINT point)
        {
            var args = new MouseHook.MouseHookEventArgs(message, 0, point);
            MouseHookEvent?.Invoke(args);
            return args;
        }

        public void Dispose()
        {
            DisposeCount++;
        }
    }
}
