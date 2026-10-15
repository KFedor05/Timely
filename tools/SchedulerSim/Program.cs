using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Timely;

namespace SchedulerSim;

internal static class Program
{
    private static int _failures;
    private static readonly DateTime Start = new DateTime(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);

    [STAThread]
    private static int Main(string[] args)
    {
        Log.Sink = _ => { };
        ContinuousTimer();
        IntervalSelection();
        MiniDurationSelection();
        LongDurationSelection();
        LongFrequencySelection();
        SuspendCountsElapsedTime();
        RestartCountsElapsedTime();
        LongBreakCadence();
        ExplicitPauses();
        DelayedTickAcrossPause();
        NotificationAndReset();
        StrictBreakCommands();
        LegacyData();
        InvalidStateIsNormalized();
        AtomicPersistence();
        StartupApproval();
        DelayedCommands();
        OverlayFailureRecovers();
        WindowLayout(args);
        Console.WriteLine(_failures == 0 ? "ALL SCENARIOS PASSED" : $"{_failures} CHECK(S) FAILED");
        return _failures == 0 ? 0 : 1;
    }

    private static void ContinuousTimer()
    {
        var sim = NewSim();
        sim.Run(TimeSpan.FromMinutes(19));
        Check("19 minutes without input still accumulate", sim.ElapsedTime == TimeSpan.FromMinutes(19));
        Check("no early break", sim.Breaks.Count == 0);
        sim.Run(TimeSpan.FromMinutes(1));
        Check("break starts at exactly 20 minutes without input",
            sim.Breaks.Count == 1 && sim.Breaks[0].At == Start.AddMinutes(20));
        sim.Run(TimeSpan.FromSeconds(30));
        Check("break duration does not count towards the next interval", sim.ElapsedTime == TimeSpan.Zero);
        sim.Run(TimeSpan.FromMinutes(20));
        Check("next break follows a full interval after the previous break",
            sim.Breaks.Count == 2 && sim.Breaks[1].At == Start.AddMinutes(40).AddSeconds(30));
    }

    private static void IntervalSelection()
    {
        var sim = NewSim();
        sim.Run(TimeSpan.FromMinutes(15));
        sim.Scheduler.SetIntervalMinutes(30);
        Check("selecting 30 minutes resets progress to a full interval",
            sim.ElapsedTime == TimeSpan.Zero && sim.Scheduler.TimeUntilBreak == TimeSpan.FromMinutes(30));
        sim.Run(TimeSpan.FromMinutes(29));
        Check("the 30 minute interval does not fire early after switching", sim.Breaks.Count == 0);
        sim.Run(TimeSpan.FromMinutes(1));
        Check("the 30 minute interval fires 30 minutes after switching", sim.Breaks.Count == 1 && sim.Breaks[0].At == Start.AddMinutes(45));

        var shorter = NewSim();
        shorter.Scheduler.SetIntervalMinutes(30);
        shorter.Run(TimeSpan.FromMinutes(25));
        shorter.Scheduler.SetIntervalMinutes(20);
        Check("switching back to 20 minutes also resets progress",
            shorter.ElapsedTime == TimeSpan.Zero && shorter.Scheduler.TimeUntilBreak == TimeSpan.FromMinutes(20));
        shorter.Run(TimeSpan.FromMinutes(19));
        Check("switching back does not trigger an overdue break", shorter.Breaks.Count == 0);
        shorter.Scheduler.SetIntervalMinutes(20);
        Check("selecting the unchanged interval does not reset progress", shorter.ElapsedTime == TimeSpan.FromMinutes(19));
        shorter.Run(TimeSpan.FromMinutes(1));
        Check("the 20 minute interval fires 20 minutes after switching", shorter.Breaks.Count == 1);

        var paused = NewSim();
        paused.Scheduler.PauseIndefinitely();
        paused.Scheduler.SetIntervalMinutes(30);
        paused.Run(TimeSpan.FromHours(1));
        Check("changing interval respects an explicit pause", paused.Breaks.Count == 0 && paused.Scheduler.IsPaused);

        var settings = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(sim.Scheduler.Settings))!;
        Check("the selected interval survives JSON serialization", settings.IntervalMinutes == 30);
    }

    private static void MiniDurationSelection()
    {
        foreach (var seconds in new[] { 20, 30, 60 })
        {
            var settings = Settings();
            var sim = NewSim(settings);
            var durations = new List<TimeSpan>();
            sim.Scheduler.BreakStarting += (_, duration) => durations.Add(duration);
            sim.Run(TimeSpan.FromMinutes(15));
            sim.Scheduler.SetMiniBreakSeconds(seconds);
            Check($"selecting {seconds}s mini break keeps the interval progress", sim.ElapsedTime == TimeSpan.FromMinutes(15));
            sim.Run(TimeSpan.FromMinutes(5));
            Check($"the mini break starts with the selected {seconds}s duration", durations.SequenceEqual(new[] { TimeSpan.FromSeconds(seconds) }));
            var nextSeconds = seconds == 20 ? 60 : 20;
            sim.Scheduler.SetMiniBreakSeconds(nextSeconds);
            sim.Run(TimeSpan.FromSeconds(seconds - 1));
            Check($"changing settings does not shorten the active {seconds}s break", sim.Scheduler.IsBreakActive);
            sim.Run(TimeSpan.FromSeconds(1));
            Check($"the active {seconds}s break ends at its original deadline", !sim.Scheduler.IsBreakActive);
            sim.Run(TimeSpan.FromMinutes(20));
            Check($"the new {nextSeconds}s duration applies to the next mini break", durations.Last() == TimeSpan.FromSeconds(nextSeconds));
            var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
            Check("mini break duration survives JSON serialization", restored.MiniBreakSeconds == nextSeconds);
        }
        var longSettings = Settings();
        longSettings.LongBreakEvery = 1;
        var longSim = NewSim(longSettings);
        var longDuration = TimeSpan.Zero;
        longSim.Scheduler.BreakStarting += (_, duration) => longDuration = duration;
        longSim.Scheduler.SetMiniBreakSeconds(20);
        longSim.Run(TimeSpan.FromMinutes(20));
        Check("mini break selection does not change the long break duration", longDuration == TimeSpan.FromMinutes(5));
    }

    private static void LongDurationSelection()
    {
        foreach (var minutes in new[] { 1, 3, 5 })
        {
            var settings = Settings();
            settings.LongBreakEvery = 1;
            var sim = NewSim(settings);
            var durations = new List<TimeSpan>();
            sim.Scheduler.BreakStarting += (_, duration) => durations.Add(duration);
            sim.Run(TimeSpan.FromMinutes(15));
            sim.Scheduler.SetLongBreakMinutes(minutes);
            Check($"selecting {minutes}min long break keeps interval progress", sim.ElapsedTime == TimeSpan.FromMinutes(15));
            sim.Run(TimeSpan.FromMinutes(5));
            Check($"long break starts with the selected {minutes}min duration", durations.SequenceEqual(new[] { TimeSpan.FromMinutes(minutes) }));
            var nextMinutes = minutes == 1 ? 5 : 1;
            sim.Scheduler.SetLongBreakMinutes(nextMinutes);
            sim.Run(TimeSpan.FromMinutes(minutes).Subtract(TimeSpan.FromSeconds(1)));
            Check($"changing settings does not shorten the active {minutes}min long break", sim.Scheduler.IsBreakActive);
            sim.Run(TimeSpan.FromSeconds(1));
            Check($"the active {minutes}min long break ends at its original deadline", !sim.Scheduler.IsBreakActive);
            sim.Run(TimeSpan.FromMinutes(20));
            Check($"the next long break uses the new {nextMinutes}min duration", durations.Last() == TimeSpan.FromMinutes(nextMinutes));
            var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
            Check("long break duration survives JSON serialization", restored.LongBreakMinutes == nextMinutes);
        }
        var ordinary = NewSim();
        ordinary.Scheduler.SetLongBreakMinutes(1);
        var miniDuration = TimeSpan.Zero;
        ordinary.Scheduler.BreakStarting += (_, duration) => miniDuration = duration;
        ordinary.Run(TimeSpan.FromMinutes(20));
        Check("long break selection does not change mini break duration", miniDuration == TimeSpan.FromSeconds(30));
    }

    private static void LongFrequencySelection()
    {
        foreach (var every in new[] { 0, 2, 3, 4, 5 })
        {
            var settings = Settings();
            settings.IntervalMinutes = 1;
            settings.MiniBreakSeconds = 5;
            settings.LongBreakMinutes = 1;
            var sim = NewSim(settings);
            sim.Run(TimeSpan.FromSeconds(30));
            sim.Scheduler.SetLongBreakEvery(every);
            Check($"selecting cadence {every} keeps interval progress", sim.ElapsedTime == TimeSpan.FromSeconds(30));
            sim.Run(TimeSpan.FromMinutes(30));
            var kinds = sim.Breaks.Take(10).Select(b => b.Kind).ToArray();
            Check($"cadence {every} produces the selected sequence", kinds.Length == 10 &&
                kinds.Select((kind, index) => kind == (every > 0 && (index + 1) % every == 0 ? BreakKind.Long : BreakKind.Mini)).All(matches => matches));
            var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
            Check($"cadence {every} survives JSON serialization", restored.LongBreakEvery == every);
        }

        var current = NewSim(new AppSettings { LongBreakEvery = 1, PreNotifySeconds = 0 });
        current.Run(TimeSpan.FromMinutes(20));
        current.Scheduler.SetLongBreakEvery(0);
        current.Run(TimeSpan.FromMinutes(5).Subtract(TimeSpan.FromSeconds(1)));
        Check("disabling long breaks does not shorten the current long break", current.Scheduler.IsBreakActive);
        current.Run(TimeSpan.FromSeconds(1));
        Check("the current long break still completes at its original deadline", !current.Scheduler.IsBreakActive);
        current.Run(TimeSpan.FromMinutes(20));
        Check("after disabling, the next break is mini", current.Breaks.Last().Kind == BreakKind.Mini);

        var reenabled = NewSim();
        reenabled.Run(TimeSpan.FromMinutes(20).Add(TimeSpan.FromSeconds(30)));
        reenabled.Scheduler.SetLongBreakEvery(3);
        Check("enabling long breaks begins a fresh cadence", reenabled.State.MiniBreaksSinceLong == 0 && reenabled.Scheduler.NextKind == BreakKind.Mini);
    }

    private static void SuspendCountsElapsedTime()
    {
        var sim = NewSim();
        sim.Run(TimeSpan.FromMinutes(5));
        sim.Suspend(TimeSpan.FromMinutes(10));
        Check("a long tick gap adds time instead of crediting rest", sim.ElapsedTime == TimeSpan.FromMinutes(15));
        sim.Suspend(TimeSpan.FromHours(9));
        Check("an overdue timer starts one break on wake", sim.Breaks.Count == 1 && sim.Scheduler.IsBreakActive);
        sim.Run(TimeSpan.FromSeconds(30));
        Check("missed intervals do not produce a backlog", sim.Breaks.Count == 1 && sim.ElapsedTime == TimeSpan.Zero);
    }

    private static void RestartCountsElapsedTime()
    {
        var first = NewSim();
        first.Run(TimeSpan.FromMinutes(15));
        first.Stop();
        var state = JsonSerializer.Deserialize<AppState>(JsonSerializer.Serialize(first.State))!;
        var second = new Sim(Settings(), state, first.Now.AddMinutes(3));
        Check("restart preserves progress and includes downtime", second.ElapsedTime == TimeSpan.FromMinutes(18));
        second.Run(TimeSpan.FromMinutes(2));
        Check("restart does not postpone the deadline", second.Breaks.Count == 1);
        second.Stop();
        var third = new Sim(Settings(), state, second.Now.AddHours(9));
        Check("a long shutdown does not reset the timer as rest", third.Breaks.Count == 1);
    }

    private static void LongBreakCadence()
    {
        var settings = Settings();
        settings.IntervalMinutes = 1;
        settings.MiniBreakSeconds = 5;
        settings.LongBreakMinutes = 1;
        settings.LongBreakEvery = 3;
        var sim = NewSim(settings);
        sim.Run(TimeSpan.FromMinutes(10));
        var kinds = string.Concat(sim.Breaks.Take(6).Select(b => b.Kind == BreakKind.Long ? "L" : "m"));
        Check("every third break is long", kinds == "mmLmmL");
    }

    private static void ExplicitPauses()
    {
        var sim = NewSim();
        sim.Run(TimeSpan.FromMinutes(15));
        sim.Scheduler.PauseFor(TimeSpan.FromMinutes(30));
        sim.Run(TimeSpan.FromMinutes(30));
        Check("a timed pause freezes progress", sim.ElapsedTime == TimeSpan.FromMinutes(15) && sim.Breaks.Count == 0);
        sim.Run(TimeSpan.FromMinutes(5));
        Check("a timed pause resumes from saved progress", sim.Breaks.Count == 1);

        var forever = NewSim();
        forever.Run(TimeSpan.FromMinutes(10));
        forever.Scheduler.PauseIndefinitely();
        forever.Suspend(TimeSpan.FromHours(9));
        Check("an indefinite pause survives a large tick gap",
            forever.ElapsedTime == TimeSpan.FromMinutes(10) && forever.Breaks.Count == 0);
        forever.Scheduler.ResumeFromPause();
        forever.Run(TimeSpan.FromMinutes(10));
        Check("manual resume keeps the remaining interval", forever.Breaks.Count == 1);
    }

    private static void DelayedTickAcrossPause()
    {
        var sim = NewSim();
        sim.Run(TimeSpan.FromMinutes(5));
        sim.Scheduler.PauseFor(TimeSpan.FromMinutes(30));
        sim.Suspend(TimeSpan.FromMinutes(35));
        Check("a delayed tick counts only time after the pause expires", sim.ElapsedTime == TimeSpan.FromMinutes(10));
    }

    private static void NotificationAndReset()
    {
        foreach (var seconds in new[] { 10, 30, 60 })
        {
            var settings = Settings();
            var sim = NewSim(settings);
            sim.Run(TimeSpan.FromMinutes(5));
            sim.Scheduler.SetPreNotifySeconds(seconds);
            Check($"selecting {seconds}s warning keeps the countdown", sim.ElapsedTime == TimeSpan.FromMinutes(5));
            var notifications = new List<int>();
            sim.Scheduler.BreakImminent += (_, lead) => notifications.Add((int)lead.TotalSeconds);
            sim.Run(TimeSpan.FromMinutes(15).Subtract(TimeSpan.FromSeconds(seconds + 1)));
            Check($"no warning before the selected {seconds}s threshold", notifications.Count == 0);
            sim.Run(TimeSpan.FromSeconds(1));
            Check($"one warning arrives at the selected {seconds}s threshold", notifications.SequenceEqual(new[] { seconds }));
            sim.Run(TimeSpan.FromSeconds(seconds));
            Check($"no extra warning before the break with {seconds}s selected", notifications.Count == 1 && sim.Breaks.Count == 1);
            sim.Run(TimeSpan.FromSeconds(30).Add(TimeSpan.FromMinutes(20)));
            Check($"the selected {seconds}s warning repeats next cycle", notifications.SequenceEqual(new[] { seconds, seconds }));
            var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
            Check($"the {seconds}s selection survives JSON serialization", restored.PreNotifySeconds == seconds);
        }

        var reset = NewSim();
        reset.Run(TimeSpan.FromMinutes(15));
        reset.Scheduler.ResetCounters();
        reset.Run(TimeSpan.FromMinutes(19));
        Check("manual reset starts a full interval", reset.Breaks.Count == 0 && reset.ElapsedTime == TimeSpan.FromMinutes(19));
        reset.Scheduler.SkipToNextBreak();
        Check("break now still starts a break", reset.Breaks.Count == 1);
        reset.Scheduler.FinishBreak(BreakKind.Mini, skipped: true);
        Check("skipping a break starts a fresh interval", reset.ElapsedTime == TimeSpan.Zero && !reset.Scheduler.IsBreakActive);

        var disabled = NewSim();
        var disabledWarnings = 0;
        disabled.Scheduler.BreakImminent += (_, _) => disabledWarnings++;
        disabled.Run(TimeSpan.FromMinutes(20));
        Check("preNotifySeconds zero disables warnings", disabledWarnings == 0);

        var now = Start;
        var lateSettings = Settings();
        lateSettings.PreNotifySeconds = 30;
        using var delayed = new BreakScheduler(lateSettings, new AppState { LastTickUtc = now }, () => now, liveHost: false);
        var lateWarnings = new List<int>();
        delayed.BreakImminent += (_, lead) => lateWarnings.Add((int)lead.TotalSeconds);
        delayed.Start();
        now = now.AddMinutes(19).AddSeconds(55);
        delayed.SafeTick();
        Check("a delayed tick emits an accurate remaining time", lateWarnings.SequenceEqual(new[] { 5 }));
    }

    private static void StrictBreakCommands()
    {
        Check("strict mode is off by default", !new AppSettings().StrictMode);
        var settings = Settings();
        settings.StrictMode = true;
        var sim = NewSim(settings);
        sim.Run(TimeSpan.FromMinutes(20));
        Check("strict mode is captured when the break starts", sim.Scheduler.IsStrictBreakActive);
        settings.StrictMode = false;
        sim.Scheduler.FinishBreak(BreakKind.Mini, skipped: true);
        Check("changing settings cannot skip an active strict break", sim.Scheduler.IsStrictBreakActive);
        sim.Scheduler.PauseFor(TimeSpan.FromHours(1));
        sim.Scheduler.PauseIndefinitely();
        Check("pause commands cannot bypass an active strict break", !sim.Scheduler.IsPaused);
        sim.Scheduler.ResetCounters();
        Check("reset cannot erase an active strict break", sim.ElapsedTime == TimeSpan.FromMinutes(20));
        sim.Run(TimeSpan.FromSeconds(30));
        Check("strict break completes normally after its full duration", !sim.Scheduler.IsBreakActive && sim.ElapsedTime == TimeSpan.Zero);
        sim.Run(TimeSpan.FromMinutes(20));
        Check("strict mode can be disabled for future breaks", sim.Scheduler.IsBreakActive && !sim.Scheduler.IsStrictBreakActive);
        var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(new AppSettings { StrictMode = true }))!;
        Check("strict mode survives JSON serialization", restored.StrictMode);
    }

    private static void LegacyData()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var settings = JsonSerializer.Deserialize<AppSettings>(
            """{"intervalMinutes":20,"presenceIdleMinutes":3,"restCreditMinutes":10}""", options)!;
        var state = JsonSerializer.Deserialize<AppState>(
            """{"activeMs":900000,"restRunMs":3600000,"miniBreaksSinceLong":1}""", options)!;
        state.LastTickUtc = Start;
        var sim = new Sim(settings, state, Start);
        sim.Run(TimeSpan.FromMinutes(5));
        Check("legacy rest data is ignored without losing progress", sim.Breaks.Count == 1);
        Check("obsolete settings and state are no longer written",
            !JsonSerializer.Serialize(settings, options).Contains("presenceIdle") &&
            !JsonSerializer.Serialize(settings, options).Contains("restCredit") &&
            !JsonSerializer.Serialize(state, options).Contains("restRun"));
    }

    private static AppSettings Settings() => new AppSettings { LongBreakEvery = 0, PreNotifySeconds = 0 };

    private static void AtomicPersistence()
    {
        var folder = Path.Combine(Path.GetTempPath(), "Timely-checks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "state.json");
        try
        {
            AtomicFile.WriteAllText(path, "{\"activeMs\":123}");
            Check("atomic save creates a new file", File.ReadAllText(path).Contains("123"));
            AtomicFile.WriteAllText(path, "{\"activeMs\":456}");
            Check("atomic save replaces an existing file", File.ReadAllText(path).Contains("456"));
            // A sharing violation must leave the previous complete state intact.
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var failed = false;
                try { AtomicFile.WriteAllText(path, "broken replacement"); }
                catch (IOException) { failed = true; }
                Check("failed atomic replacement reports an error", failed);
            }
            Check("failed replacement preserves the previous JSON", File.ReadAllText(path).Contains("456"));
            Check("atomic save cleans up temporary files", Directory.GetFiles(folder).Length == 1);
        }
        finally
        {
            // Only files in the uniquely created test directory are removed.
            foreach (var file in Directory.GetFiles(folder)) File.Delete(file);
            Directory.Delete(folder);
        }
    }

    private static void InvalidStateIsNormalized()
    {
        var state = new AppState { ActiveMs = double.MaxValue, MiniBreaksSinceLong = -1 };
        state.Normalize();
        Check("oversized state cannot overflow a TimeSpan", state.ActiveMs == TimeSpan.FromMinutes(600).TotalMilliseconds);
        Check("negative break cadence is normalized", state.MiniBreaksSinceLong == 0);
        state.ActiveMs = double.NaN;
        state.Normalize();
        Check("non-finite state is reset", state.ActiveMs == 0);
    }

    private static void StartupApproval()
    {
        Check("a Run entry without an approval override is enabled", Autostart.IsApproved(null));
        foreach (var code in new byte[] { 2, 6 })
        {
            var bytes = new byte[12];
            bytes[0] = code;
            Check($"Windows enabled startup state {code} is recognized", Autostart.IsApproved(bytes));
        }
        foreach (var code in new byte[] { 3, 7, 255 })
        {
            var bytes = new byte[12];
            bytes[0] = code;
            Check($"Windows disabled or unknown startup state {code} stays disabled", !Autostart.IsApproved(bytes));
        }
        Check("malformed startup approval is not treated as enabled", !Autostart.IsApproved(new byte[] { 2 }));
    }

    private static void DelayedCommands()
    {
        var now = Start;
        var state = new AppState { LastTickUtc = now };
        using var scheduler = new BreakScheduler(Settings(), state, () => now, liveHost: false);
        scheduler.Start();
        now = now.AddSeconds(30);
        scheduler.PauseIndefinitely();
        Check("pausing between ticks preserves elapsed time", scheduler.ElapsedTime == TimeSpan.FromSeconds(30));
        now = now.AddHours(1);
        scheduler.ResumeFromPause();
        now = now.AddSeconds(1);
        scheduler.SafeTick();
        Check("resuming between ticks does not count the paused hour", scheduler.ElapsedTime == TimeSpan.FromSeconds(31));
        now = now.AddMinutes(5);
        scheduler.ResetCounters();
        now = now.AddSeconds(1);
        scheduler.SafeTick();
        Check("reset uses the moment of the command as its time origin", scheduler.ElapsedTime == TimeSpan.FromSeconds(1));
    }

    private static void OverlayFailureRecovers()
    {
        var now = Start;
        var settings = Settings();
        settings.PreNotifySeconds = 10;
        using var scheduler = new BreakScheduler(settings, new AppState { LastTickUtc = now }, () => now, liveHost: false);
        var attempts = 0;
        var warnings = 0;
        scheduler.BreakImminent += (_, _) => warnings++;
        scheduler.BreakStarting += (_, _) => { if (++attempts == 1) throw new InvalidOperationException("simulated overlay failure"); };
        scheduler.Start();
        now = now.AddMinutes(30);
        scheduler.SafeTick();
        Check("a failed overlay does not leave the scheduler stuck in a break", !scheduler.IsBreakActive);
        now = now.AddSeconds(1);
        scheduler.SafeTick();
        Check("the next tick retries a failed overlay", attempts == 2 && scheduler.IsBreakActive);
        Check("an overdue break does not emit a misleading advance warning", warnings == 0);
    }

    private static void WindowLayout(string[] args)
    {
        var app = new App();
        app.InitializeComponent();
        foreach (var lang in new[] { Lang.En, Lang.Ru })
        foreach (var theme in new[] { ThemeChoice.Light, ThemeChoice.Dark })
        {
            Loc.Set(lang);
            Theme.Set(theme);
            var sim = NewSim(new AppSettings { PreNotifySeconds = 10 });
            var window = new MainWindow(sim.Scheduler);
            var scroll = (ScrollViewer)window.Content;
            var size = new Size(440, 340);
            scroll.Measure(size);
            scroll.Arrange(new Rect(size));
            scroll.UpdateLayout();
            Check($"settings can scroll on a small viewport ({lang}, {theme})", scroll.ScrollableHeight > 0);
            scroll.ScrollToEnd();
            scroll.UpdateLayout();
            var close = (Button)window.FindName("CloseButton");
            var point = close.TransformToAncestor(scroll).Transform(new Point(0, 0));
            Check($"bottom controls remain reachable ({lang}, {theme})",
                point.Y >= 0 && point.Y + close.ActualHeight <= size.Height + 1);

            if (args.Length == 2 && args[0] == "--screenshots")
            {
                Directory.CreateDirectory(args[1]);
                scroll.ScrollToTop();
                size = new Size(454, 1220);
                scroll.Measure(size);
                scroll.Arrange(new Rect(size));
                scroll.UpdateLayout();
                var bitmap = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(scroll);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var output = File.Create(Path.Combine(args[1], $"settings-{lang}-{theme}.png"));
                encoder.Save(output);
            }
            window.Close();
            sim.Stop();
        }
        StrictOverlayClosing();
        app.Shutdown();
    }

    private static void StrictOverlayClosing()
    {
        var now = Start;
        var settings = new AppSettings { StrictMode = true };
        var window = new BreakWindow(BreakKind.Mini, TimeSpan.FromSeconds(30), settings, () => now);
        var finished = 0;
        var skipped = false;
        window.Finished += (_, wasSkipped) => { finished++; skipped = wasSkipped; };
        var skip = (Button)window.FindName("SkipButton");
        Check("strict overlay hides Skip", skip.Visibility == Visibility.Collapsed);
        skip.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check("even a direct Skip action cannot close a strict overlay", finished == 0);
        window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, new TestPresentationSource(), 0, Key.Escape) { RoutedEvent = Keyboard.KeyDownEvent });
        Check("Escape cannot close a strict overlay", finished == 0);
        window.Close();
        Check("strict overlay cancels Close, including Alt+F4", finished == 0);
        settings.StrictMode = false;
        window.Close();
        Check("strict overlay keeps its original mode for the entire break", finished == 0);
        now = now.AddSeconds(29);
        window.UpdateCountdown();
        Check("strict overlay remains until its deadline", finished == 0);
        now = now.AddSeconds(1);
        window.UpdateCountdown();
        Check("strict overlay closes once at the deadline without skipping", finished == 1 && !skipped);

        var ordinary = new BreakWindow(BreakKind.Mini, TimeSpan.FromSeconds(30), new AppSettings(), () => now);
        var ordinarySkipped = false;
        ordinary.Finished += (_, wasSkipped) => ordinarySkipped = wasSkipped;
        ordinary.Close();
        Check("normal mode still allows closing the break", ordinarySkipped);

        var strictSettings = new AppSettings { StrictMode = true };
        var sim = NewSim(strictSettings);
        var main = new MainWindow(sim.Scheduler);
        var panel = (SettingsPanel)main.FindName("Settings");
        var toggle = (CheckBox)panel.FindName("StrictSwitch");
        sim.Run(TimeSpan.FromMinutes(20));
        Check("strict mode switch is disabled while a break is active", !toggle.IsEnabled);
        toggle.IsChecked = false;
        Check("changing the disabled switch cannot disable the active mode", strictSettings.StrictMode && toggle.IsChecked == true);
        sim.Run(TimeSpan.FromSeconds(30));
        Check("strict mode switch becomes available after the break", toggle.IsEnabled);
        main.Close();
        sim.Stop();
    }

    private sealed class TestPresentationSource : PresentationSource
    {
        public override Visual RootVisual { get; set; } = null!;
        public override bool IsDisposed => false;
        protected override CompositionTarget GetCompositionTargetCore() => null!;
    }

    private static Sim NewSim(AppSettings? settings = null) =>
        new Sim(settings ?? Settings(), new AppState { LastTickUtc = Start }, Start);

    private static void Check(string description, bool condition)
    {
        Console.WriteLine($"[{(condition ? "PASS" : "FAIL")}] {description}");
        if (!condition) _failures++;
    }
}
