using System;
using System.Collections.Generic;
using Timely;

namespace SchedulerSim;

/// <summary>
/// Replays the continuous timer against a virtual clock.
/// Nothing here touches the real clock, the registry or the disk, so a scenario
/// that would take nine hours in real life runs in milliseconds.
/// </summary>
internal sealed class Sim
{
    private readonly BreakScheduler _scheduler;
    private readonly List<(DateTime At, BreakKind Kind)> _breaks = new List<(DateTime, BreakKind)>();

    private DateTime _now;
    private DateTime? _breakEndsAt;
    private BreakKind _breakKind;

    public Sim(AppSettings settings, AppState state, DateTime start)
    {
        _now = start;
        State = state;

        _scheduler = new BreakScheduler(settings, state, () => _now, liveHost: false);
        _scheduler.BreakStarting += (kind, duration) =>
        {
            _breaks.Add((_now, kind));
            _breakKind = kind;
            _breakEndsAt = _now + duration;
        };

        _scheduler.Start();
    }

    public AppState State { get; }

    public BreakScheduler Scheduler => _scheduler;

    public IReadOnlyList<(DateTime At, BreakKind Kind)> Breaks => _breaks;

    public DateTime Now => _now;

    public TimeSpan ElapsedTime => _scheduler.ElapsedTime;

    /// <summary>Advance the clock second by second, without any input activity.</summary>
    public void Run(TimeSpan span)
    {
        var end = _now + span;
        while (_now < end)
        {
            _now = _now.AddSeconds(1);

            if (_breakEndsAt is not null && _now >= _breakEndsAt.Value)
            {
                _breakEndsAt = null;
                _scheduler.FinishBreak(_breakKind, skipped: false);
            }

            _scheduler.SafeTick();
        }
    }

    /// <summary>Machine suspends: the clock jumps forward with no ticks in between.</summary>
    public void Suspend(TimeSpan span)
    {
        _now += span;
        _scheduler.SafeTick();
    }

    public void Stop() => _scheduler.Stop();
}
