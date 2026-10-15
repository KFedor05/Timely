using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace Timely;

public partial class BreakWindow : Window
{
    private static readonly Random Rng = new Random();

    private readonly BreakKind _kind;
    private readonly TimeSpan _duration;
    private readonly bool _strictMode;
    private readonly DispatcherTimer _ticker;
    private readonly Func<DateTime> _now;
    private readonly DateTime _startedUtc;

    private bool _finished;
    private bool _skipped;

    /// <summary>Raised exactly once, with true if the user cut the break short.</summary>
    public event Action<BreakKind, bool>? Finished;

    public BreakWindow(BreakKind kind, TimeSpan duration, AppSettings settings)
        : this(kind, duration, settings, null)
    {
    }

    internal BreakWindow(BreakKind kind, TimeSpan duration, AppSettings settings, Func<DateTime>? now)
    {
        InitializeComponent();

        _now = now ?? (() => DateTime.UtcNow);
        _startedUtc = _now();
        _kind = kind;
        _duration = duration;
        _strictMode = settings.StrictMode;

        var ideas = Loc.Ideas(kind);
        KindText.Text = Loc.T(kind == BreakKind.Long ? "break.long" : "break.mini");
        IdeaText.Text = ideas[Rng.Next(ideas.Length)];

        SkipButton.Content = Loc.T("break.skip");
        var allowSkip = settings.AllowSkip && !_strictMode;
        SkipButton.Visibility = allowSkip ? Visibility.Visible : Visibility.Collapsed;
        HintText.Text = _strictMode ? Loc.T("break.strictHint") : allowSkip ? Loc.T("break.escHint") : string.Empty;

        SizeToScreen(settings.OverlayFraction);

        _ticker = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _ticker.Tick += (_, _) => UpdateCountdown();
        UpdateCountdown();
        _ticker.Start();

        KeyDown += OnKeyDown;
        Loaded += (_, _) => { Activate(); Focus(); };
        StateChanged += (_, _) =>
        {
            if (_strictMode && !_finished && WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;
        };
    }

    private void SizeToScreen(double fraction)
    {
        var screenWidth = SystemParameters.PrimaryScreenWidth;
        var screenHeight = SystemParameters.PrimaryScreenHeight;

        Width = screenWidth * fraction;
        Height = screenHeight * fraction;
        Left = (screenWidth - Width) / 2;
        Top = (screenHeight - Height) / 2;
    }

    internal void UpdateCountdown()
    {
        var elapsed = _now() - _startedUtc;
        var remaining = _duration - elapsed;

        if (remaining <= TimeSpan.Zero)
        {
            CountdownText.Text = "0:00";
            ProgressFill.Width = ProgressTrackWidth();
            Finish(skipped: false);
            return;
        }

        CountdownText.Text = string.Format("{0}:{1:00}",
            (int)remaining.TotalMinutes, remaining.Seconds);

        var progress = Math.Clamp(elapsed.TotalMilliseconds / _duration.TotalMilliseconds, 0, 1);
        ProgressFill.Width = ProgressTrackWidth() * progress;
    }

    private double ProgressTrackWidth()
    {
        var track = ProgressFill.Parent as FrameworkElement;
        return track is null || double.IsNaN(track.ActualWidth) ? 0 : track.ActualWidth;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && SkipButton.Visibility == Visibility.Visible)
            Finish(skipped: true);
    }

    private void OnSkipClick(object sender, RoutedEventArgs e) => Finish(skipped: true);

    private void Finish(bool skipped)
    {
        if (_finished) return;
        if (skipped && _strictMode) return;
        _finished = true;
        _skipped = skipped;

        _ticker.Stop();
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_strictMode && !_finished) e.Cancel = true;
    }

    // Used only after an initialization failure, when the overlay cannot run.
    internal void CloseAfterFailure()
    {
        _finished = true;
        _ticker.Stop();
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        // Covers Alt+F4 and any other path out of the window, so the scheduler
        // is never left believing a break is still on screen.
        _ticker.Stop();
        if (!_finished)
        {
            _finished = true;
            _skipped = true;
        }

        base.OnClosed(e);
        Finished?.Invoke(_kind, _skipped);
    }
}
