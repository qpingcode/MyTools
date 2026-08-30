using System.Reflection;
using Moq;
using MyTools.Common.Config.Interfaces;
using MyTools.Common.Config.Models;
using MyTools.Desktop.Services;
using MyTools.Desktop.ViewModels;
using NUnit.Framework;

namespace MyTools.Desktop.Test.Views;

[TestFixture]
public class SearchDebouncerTests
{
    [Test]
    public void Restart_UsesLatestConfiguredDelay()
    {
        var setting = CreateDelaySetting(200);
        var scheduled = new List<ScheduledCallback>();
        using var debouncer = CreateDebouncer(setting, scheduled, [], () => { });

        debouncer.Restart();
        setting.CurrentValue = 350.0;
        debouncer.Restart();

        Assert.That(scheduled.Select(item => item.Delay), Is.EqualTo(new[]
        {
            TimeSpan.FromMilliseconds(200),
            TimeSpan.FromMilliseconds(350)
        }));
    }

    [Test]
    public void Restart_DropsSupersededCallbackWaitingForDispatcher()
    {
        var setting = CreateDelaySetting(100);
        var scheduled = new List<ScheduledCallback>();
        var dispatched = new List<Action>();
        var invocationCount = 0;
        using var debouncer = CreateDebouncer(setting, scheduled, dispatched, () => invocationCount++);

        debouncer.Restart();
        scheduled[0].Callback();
        debouncer.Restart();
        dispatched[0]();
        scheduled[1].Callback();
        dispatched[1]();

        Assert.That(invocationCount, Is.EqualTo(1));
    }

    [Test]
    public void Restart_InvalidLargeDelayUsesDefault()
    {
        var setting = CreateDelaySetting(double.MaxValue);
        var scheduled = new List<ScheduledCallback>();
        using var debouncer = CreateDebouncer(setting, scheduled, [], () => { });

        debouncer.Restart();

        Assert.That(scheduled.Single().Delay, Is.EqualTo(TimeSpan.FromMilliseconds(250)));
    }

    [Test]
    public void Callback_ExecutesWhileRestartSynchronizationIsHeld()
    {
        var setting = CreateDelaySetting(100);
        var scheduled = new List<ScheduledCallback>();
        var dispatched = new List<Action>();
        SearchDebouncer? debouncer = null;
        bool? lockWasHeld = null;
        debouncer = CreateDebouncer(setting, scheduled, dispatched, () =>
        {
            var syncRoot = typeof(SearchDebouncer)
                .GetField("syncRoot", BindingFlags.Instance | BindingFlags.NonPublic)?
                .GetValue(debouncer);
            lockWasHeld = syncRoot != null && Monitor.IsEntered(syncRoot);
        });

        using (debouncer)
        {
            debouncer.Restart();
            scheduled[0].Callback();
            dispatched[0]();
        }

        Assert.That(lockWasHeld, Is.True);
    }

    private static SearchDebouncer CreateDebouncer(
        ConfigurationSetting setting,
        List<ScheduledCallback> scheduled,
        List<Action> dispatched,
        Action callback)
    {
        var registry = new Mock<IConfigurationRegistry>();
        registry
            .Setup(candidate => candidate.FindSetting(GeneralSettings.SearchDelay))
            .Returns(setting);

        return new SearchDebouncer(
            registry.Object,
            callback,
            dispatched.Add,
            (delay, scheduledCallback) =>
            {
                scheduled.Add(new ScheduledCallback(delay, scheduledCallback));
                return Mock.Of<IDisposable>();
            });
    }

    private static ConfigurationSetting CreateDelaySetting(double delay)
    {
        var setting = new ConfigurationSetting
        {
            Name = "SearchDelay",
            Serializer = Mock.Of<IRegistrySerializer>()
        };
        setting.InitValueWithoutNotify(delay);
        return setting;
    }

    private sealed record ScheduledCallback(TimeSpan Delay, Action Callback);
}
