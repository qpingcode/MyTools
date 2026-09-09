using Microsoft.Extensions.Logging.Abstractions;
using MyTools.Desktop.Services;
using MyTools.Desktop.Utils;
using NUnit.Framework;

namespace MyTools.Desktop.Test.Utils;

[TestFixture]
public sealed class MouseGestureDetectorTests
{
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

    private static MouseGestureDetector CreateDetector(MouseHelper mouseHelper, IMouseHook hook)
        => new(
            mouseHelper,
            NullLogger<MouseGestureDetector>.Instance,
            NullLogger<global::MyTools.Desktop.Views.MouseTrailWindow>.Instance,
            hook);

    private static Thread Start(MouseGestureDetector detector)
    {
        var thread = new Thread(detector.Start) { IsBackground = true };
        thread.Start();
        return thread;
    }

    private sealed class TestMouseHook : IMouseHook
    {
        private readonly ManualResetEventSlim started = new(false);

#pragma warning disable CS0067
        public event MouseHook.MouseHookEventHandler? MouseHookEvent;
#pragma warning restore CS0067

        public int StartCount { get; private set; }
        public int DisposeCount { get; private set; }

        public void StartListening()
        {
            StartCount++;
            started.Set();
        }

        public bool WaitForStart(TimeSpan timeout) => started.Wait(timeout);

        public void ResetStarted() => started.Reset();

        public void Dispose()
        {
            DisposeCount++;
        }
    }
}
