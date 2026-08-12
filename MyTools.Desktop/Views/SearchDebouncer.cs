using MyTools.Common.Config.Interfaces;

namespace MyTools.Desktop.ViewModels;

internal sealed class SearchDebouncer : IDisposable
{
    private const string SearchDelaySettingPath = "General.SearchDelay";
    private const double DefaultDelayMilliseconds = 100;
    private const double MaximumTimerDelayMilliseconds = uint.MaxValue - 1d;
    private readonly IConfigurationRegistry configurationRegistry;
    private readonly Action callback;
    private readonly Action<Action> dispatcher;
    private readonly Func<TimeSpan, Action, IDisposable> schedule;
    private readonly object syncRoot = new();
    private IDisposable? scheduledCallback;
    private long generation;
    private bool disposed;

    public SearchDebouncer(
        IConfigurationRegistry configurationRegistry,
        Action callback,
        Action<Action> dispatcher)
        : this(configurationRegistry, callback, dispatcher, ScheduleCallback)
    {
    }

    internal SearchDebouncer(
        IConfigurationRegistry configurationRegistry,
        Action callback,
        Action<Action> dispatcher,
        Func<TimeSpan, Action, IDisposable> schedule)
    {
        this.configurationRegistry = configurationRegistry;
        this.callback = callback;
        this.dispatcher = dispatcher;
        this.schedule = schedule;
    }

    public void Restart()
    {
        lock (syncRoot)
        {
            ObjectDisposedException.ThrowIf(disposed, this);

            var currentGeneration = ++generation;
            scheduledCallback?.Dispose();
            scheduledCallback = schedule(
                GetConfiguredDelay(),
                () => DispatchIfCurrent(currentGeneration));
        }
    }

    private TimeSpan GetConfiguredDelay()
    {
        var configuredDelay = configurationRegistry
            .FindSetting(SearchDelaySettingPath)?
            .GetValue<double>() ?? DefaultDelayMilliseconds;

        if (!double.IsFinite(configuredDelay)
            || configuredDelay < 0
            || configuredDelay > MaximumTimerDelayMilliseconds)
        {
            configuredDelay = DefaultDelayMilliseconds;
        }

        return TimeSpan.FromMilliseconds(configuredDelay);
    }

    private void DispatchIfCurrent(long currentGeneration)
    {
        lock (syncRoot)
        {
            if (disposed || currentGeneration != generation)
            {
                return;
            }
        }

        dispatcher(() => InvokeIfCurrent(currentGeneration));
    }

    private void InvokeIfCurrent(long currentGeneration)
    {
        lock (syncRoot)
        {
            if (disposed || currentGeneration != generation)
            {
                return;
            }

            callback();
        }
    }

    private static IDisposable ScheduleCallback(TimeSpan delay, Action scheduledCallback)
    {
        return new Timer(
            _ => scheduledCallback(),
            null,
            delay,
            Timeout.InfiniteTimeSpan);
    }

    public void Dispose()
    {
        lock (syncRoot)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            generation++;
            scheduledCallback?.Dispose();
            scheduledCallback = null;
        }
    }
}
