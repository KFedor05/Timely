using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;

namespace Timely;

/// <summary>
/// The app's one real window, and the reason Timely has a taskbar button at all.
/// Shows what the scheduler actually believes, the same commands as the tray
/// menu, and the settings — so the tray icon stays a shortcut rather than the
/// only way in.
/// </summary>
public partial class MainWindow : Window
{
    private readonly BreakScheduler _scheduler;

    public MainWindow(BreakScheduler scheduler)
    {
        _scheduler = scheduler;

        InitializeComponent();
        MaxHeight = SystemParameters.WorkArea.Height;
        MaxWidth = SystemParameters.WorkArea.Width;

        Settings.Bind(scheduler);
        ApplyTexts();
        Refresh();

        Loc.Changed += OnLanguageChanged;
        Theme.Changed += ApplyTitleBarTheme;
        _scheduler.StateChanged += Refresh;
        Activated += (_, _) => Settings.RefreshAutostart();

        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
    }

    private void OnLanguageChanged()
    {
        ApplyTexts();
        Settings.ApplyTexts();
        Refresh();
    }

    private void ApplyTexts()
    {
        Title = "Timely";

        ControlsLabel.Text = Loc.T("main.controls");
        BreakNowButton.Content = Loc.T("tray.breakNow");
        ResetButton.Content = Loc.T("tray.reset");

        PauseLabel.Text = Loc.T("tray.pause");
        Pause30Button.Content = Loc.T("tray.pause.30m");
        Pause1hButton.Content = Loc.T("tray.pause.1h");
        Pause2hButton.Content = Loc.T("tray.pause.2h");
        PauseForeverButton.Content = Loc.T("tray.pause.forever");
        ResumeButton.Content = Loc.T("tray.resume");

        SettingsLabel.Text = Loc.T("main.settings");
        RestNote.Text = Loc.T("settings.rest");
        OpenJsonButton.Content = Loc.T("settings.openJson");
        OpenLogButton.Content = Loc.T("tray.log");
        CloseButton.Content = Loc.T("main.close");
    }

    private void Refresh()
    {
        Settings.RefreshBreakControls();
        StatusText.Text = _scheduler.StatusLine();

        var progress = Math.Clamp(_scheduler.Progress, 0, 1);
        ProgressColumn.Width = new GridLength(progress, GridUnitType.Star);
        RemainderColumn.Width = new GridLength(1 - progress, GridUnitType.Star);

        // A rounded zero-width bar still paints as a dot, which reads as
        // progress that is not there.
        ProgressFill.Visibility = progress > 0.005 ? Visibility.Visible : Visibility.Hidden;

        var paused = _scheduler.IsPaused;
        PauseRow.Visibility = paused ? Visibility.Collapsed : Visibility.Visible;
        ResumeRow.Visibility = paused ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnBreakNowClick(object sender, RoutedEventArgs e) => _scheduler.SkipToNextBreak();

    private void OnResetClick(object sender, RoutedEventArgs e) => _scheduler.ResetCounters();

    private void OnPause30Click(object sender, RoutedEventArgs e) =>
        _scheduler.PauseFor(TimeSpan.FromMinutes(30));

    private void OnPause1hClick(object sender, RoutedEventArgs e) =>
        _scheduler.PauseFor(TimeSpan.FromHours(1));

    private void OnPause2hClick(object sender, RoutedEventArgs e) =>
        _scheduler.PauseFor(TimeSpan.FromHours(2));

    private void OnPauseForeverClick(object sender, RoutedEventArgs e) =>
        _scheduler.PauseIndefinitely();

    private void OnResumeClick(object sender, RoutedEventArgs e) => _scheduler.ResumeFromPause();

    private void OnOpenJsonClick(object sender, RoutedEventArgs e) => OpenInShell(Paths.SettingsFile);

    private void OnOpenLogClick(object sender, RoutedEventArgs e) => OpenInShell(Paths.LogFile);

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private static void OpenInShell(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error($"could not open {path}", ex);
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyTitleBarTheme();
    }

    /// <summary>
    /// WPF leaves the title bar light whatever the app draws below it, which
    /// looks broken next to a dark window. Ask DWM to match.
    /// </summary>
    private void ApplyTitleBarTheme()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero) Native.SetTitleBarDarkMode(handle, Theme.IsDark);
    }

    protected override void OnClosed(EventArgs e)
    {
        Loc.Changed -= OnLanguageChanged;
        Theme.Changed -= ApplyTitleBarTheme;
        _scheduler.StateChanged -= Refresh;
        base.OnClosed(e);
    }
}
