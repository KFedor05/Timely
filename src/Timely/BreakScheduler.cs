using System;
using System.Windows.Threading;

namespace Timely;

public enum BreakKind
{
    Mini,
    Long
}

/// <summary>
/// Continuous wall-clock timer. Only an explicit pause or an active break
/// stops the interval; input activity and session state are never inspected.
/// </summary>
public sealed class BreakScheduler : IDisposable
{
    private static readonly TimeSpan SaveEvery = TimeSpan.FromSeconds(10);

    private readonly AppSettings _settings;
    private readonly AppState _state;
    private readonly Func<DateTime> _now;
    private readonly bool _liveHost;
    private readonly DispatcherTimer? _timer;

    private bool _breakActive;
    private bool _strictBreak;
    private bool _preNotified;
    private DateTime _pausedUntilUtc = DateTime.MinValue;
    private bool _pausedForever;
    private DateTime _lastSaveUtc = DateTime.MinValue;

    public event Action<BreakKind, TimeSpan>? BreakStarting;
    public event Action<BreakKind, TimeSpan>? BreakImminent;
    public event Action? StateChanged;

    public BreakScheduler(AppSettings settings)
        : this(settings, AppState.Load(), now: null, liveHost: true)
    {
    }

    /// <summary>
    /// Test seam: a simulated clock lets the schedule be replayed headlessly.
    /// </summary>
    internal BreakScheduler(
        AppSettings settings,
        AppState state,
        Func<DateTime>? now,
        bool liveHost)
    {
        _settings = settings;
        _state = state;
        _state.Normalize();
        _now = now ?? (() => DateTime.UtcNow);
        _liveHost = liveHost;

        if (_liveHost)
        {
            _timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _timer.Tick += (_, _) => SafeTick();
        }
    }

    public AppSettings Settings => _settings;

    public bool IsPaused => _pausedForever || _now() < _pausedUntilUtc;

    public bool IsBreakActive => _breakActive;

    public bool IsStrictBreakActive => _breakActive && _strictBreak;

    public TimeSpan ElapsedTime => TimeSpan.FromMilliseconds(Math.Max(0, _state.ActiveMs));

    public TimeSpan TimeUntilBreak =>
        TimeSpan.FromMilliseconds(Math.Max(0, _settings.Interval.TotalMilliseconds - _state.ActiveMs));

    public double Progress => Math.Clamp(_state.ActiveMs / _settings.Interval.TotalMilliseconds, 0, 1);

    public BreakKind NextKind =>
        _settings.LongBreakEvery > 0 && _state.MiniBreaksSinceLong >= _settings.LongBreakEvery - 1
            ? BreakKind.Long
            : BreakKind.Mini;

    public void Start()
    {
        // Include elapsed time across restarts just like a delayed timer tick.
        if (_state.LastTickUtc == default) _state.LastTickUtc = _now();
        SafeTick();

        if (_liveHost) _timer!.Start();

        Log.Info($"scheduler started ({_settings.Describe()}), elapsed time so far {Fmt(ElapsedTime)}");
        StateChanged?.Invoke();
    }

    public void Stop()
    {
        if (_liveHost) _timer!.Stop();
        SafeTick(checkForBreak: false);
        Persist();
        Log.Info("scheduler stopped");
    }

    public void Dispose() => Stop();

    // ---------------------------------------------------------------- commands

    public void SetIntervalMinutes(int minutes)
    {
        minutes = Math.Clamp(minutes, 1, 600);
        if (_settings.IntervalMinutes == minutes) return;

        _settings.IntervalMinutes = minutes;
        _state.ActiveMs = 0;
        _state.LastTickUtc = _now();
        _preNotified = false;
        Persist();
        Log.Info($"break interval changed to {minutes} min, countdown restarted");
        StateChanged?.Invoke();
    }

    public void SetMiniBreakSeconds(int seconds)
    {
        seconds = Math.Clamp(seconds, 5, 3600);
        if (_settings.MiniBreakSeconds == seconds) return;
        _settings.MiniBreakSeconds = seconds;
        Log.Info($"mini break duration changed to {seconds} s for the next break");
        StateChanged?.Invoke();
    }

    public void SetLongBreakEvery(int every)
    {
        every = Math.Max(0, every);
        if (_settings.LongBreakEvery == every) return;
        _settings.LongBreakEvery = every;
        _state.MiniBreaksSinceLong = 0;
        _preNotified = false;
        Persist();
        Log.Info($"long break frequency changed to {every}, cadence restarted");
        StateChanged?.Invoke();
    }

    public void SetLongBreakMinutes(int minutes)
    {
        minutes = Math.Clamp(minutes, 1, 120);
        if (_settings.LongBreakMinutes == minutes) return;
        _settings.LongBreakMinutes = minutes;
        Log.Info($"long break duration changed to {minutes} min for the next long break");
        StateChanged?.Invoke();
    }

    public void SetPreNotifySeconds(int seconds)
    {
        seconds = Math.Clamp(seconds, 0, 300);
        if (_settings.PreNotifySeconds == seconds) return;
        _settings.PreNotifySeconds = seconds;
        _preNotified = false;
        Log.Info($"break warning changed to {seconds} s");
        StateChanged?.Invoke();
    }

    public void PauseFor(TimeSpan duration)
    {
        if (IsStrictBreakActive) return;
        SafeTick(checkForBreak: false);
        _pausedForever = false;
        _pausedUntilUtc = _now() + duration;
        Log.Info($"breaks paused for {Fmt(duration)}");
        StateChanged?.Invoke();
    }

    public void PauseIndefinitely()
    {
        if (IsStrictBreakActive) return;
        SafeTick(checkForBreak: false);
        _pausedForever = true;
        _pausedUntilUtc = DateTime.MinValue;
        Log.Info("breaks paused indefinitely");
        StateChanged?.Invoke();
    }

    public void ResumeFromPause()
    {
        if (!IsPaused) return;
        SafeTick(checkForBreak: false);
        _pausedForever = false;
        _pausedUntilUtc = DateTime.MinValue;
        Log.Info("breaks resumed");
        StateChanged?.Invoke();
    }

    public void SkipToNextBreak()
    {
        if (_breakActive) return;
        Log.Info("jumping to the next break on request");
        StartBreak();
    }

    public void ResetCounters()
    {
        if (IsStrictBreakActive) return;
        _state.ActiveMs = 0;
        _state.LastTickUtc = _now();
        _state.MiniBreaksSinceLong = 0;
        _preNotified = false;
        Persist();
        Log.Info("counters reset on request");
        StateChanged?.Invoke();
    }

    /// <summary>Called by the overlay when the break window closes.</summary>
    public void FinishBreak(BreakKind kind, bool skipped)
    {
        if (IsStrictBreakActive && skipped) return;
        _breakActive = false;
        _strictBreak = false;
        _preNotified = false;
        _state.ActiveMs = 0;

        if (kind == BreakKind.Long) _state.MiniBreaksSinceLong = 0;
        else _state.MiniBreaksSinceLong = Math.Min(_state.MiniBreaksSinceLong, int.MaxValue - 1) + 1;

        _state.LastTickUtc = _now();
        Persist();

        Log.Info($"finished {kind} break (skipped: {skipped})");
        StateChanged?.Invoke();
    }

    public string StatusLine()
    {
        if (_breakActive) return Loc.T("status.inBreak");
        if (_pausedForever) return Loc.T("status.pausedForever");
        if (IsPaused) return Loc.T("status.pausedFor", Fmt(_pausedUntilUtc - _now()));

        var kind = Loc.T(NextKind == BreakKind.Long ? "break.long" : "break.mini");
        return Loc.T("status.next", kind, Fmt(TimeUntilBreak));
    }

    // ------------------------------------------------------------------- clock

    internal void SafeTick(bool checkForBreak = true)
    {
        try
        {
            Tick(checkForBreak);
        }
        catch (Exception ex)
        {
            // A throwing tick must not leave a live tray icon sitting on top of
            // a dead scheduler — that is exactly the failure mode being designed
            // out, so swallow, log, and keep the timer running.
            Log.Error("tick failed, scheduler keeps running", ex);
        }
    }

    private void Tick(bool checkForBreak)
    {
        var now = _now();
        var previous = _state.LastTickUtc;
        var dt = (now - previous).TotalMilliseconds;
        _state.LastTickUtc = now;

        if (dt <= 0)
        {
            // Clock moved backwards (NTP correction, timezone change). Skip.
            return;
        }

        if (_breakActive)
        {
            PeriodicSave(now);
            return;
        }

        if (!_pausedForever)
        {
            // A delayed tick may straddle the end of a timed pause.
            var countingFrom = previous > _pausedUntilUtc ? previous : _pausedUntilUtc;
            _state.ActiveMs += Math.Max(0, (now - countingFrom).TotalMilliseconds);
        }

        if (checkForBreak && !IsPaused) CheckForBreak();

        PeriodicSave(now);
        StateChanged?.Invoke();
    }

    private void CheckForBreak()
    {
        var intervalMs = _settings.Interval.TotalMilliseconds;
        var preNotifyMs = _settings.PreNotify.TotalMilliseconds;

        if (_state.ActiveMs >= intervalMs)
        {
            StartBreak();
            return;
        }

        if (preNotifyMs <= 0) return;

        var remainingMs = intervalMs - _state.ActiveMs;
        if (!_preNotified && remainingMs <= preNotifyMs)
        {
            _preNotified = true;
            var kind = NextKind;
            var lead = TimeSpan.FromSeconds(Math.Ceiling(remainingMs / 1000));
            Log.Info($"{kind} break in {Fmt(lead)}");
            BreakImminent?.Invoke(kind, lead);
        }


    }

    private void StartBreak()
    {
        var kind = NextKind;
        var duration = kind == BreakKind.Long ? _settings.LongBreak : _settings.MiniBreak;

        _breakActive = true;
        _strictBreak = _settings.StrictMode;
        _preNotified = false;
        Persist();

        Log.Info($"starting {kind} break for {Fmt(duration)} after {Fmt(ElapsedTime)} elapsed");
        try
        {
            BreakStarting?.Invoke(kind, duration);
        }
        catch
        {
            _breakActive = false;
            _strictBreak = false;
            throw;
        }
        StateChanged?.Invoke();
    }

    private void PeriodicSave(DateTime now)
    {
        if (now - _lastSaveUtc < SaveEvery) return;
        _lastSaveUtc = now;
        Persist();
    }

    private void Persist()
    {
        if (_liveHost) _state.Save();
    }

    internal static string Fmt(TimeSpan span)
    {
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;
        return span.TotalHours >= 1
            ? string.Format("{0}:{1:00}:{2:00}", (int)span.TotalHours, span.Minutes, span.Seconds)
            : string.Format("{0}:{1:00}", (int)span.TotalMinutes, span.Seconds);
    }
}
