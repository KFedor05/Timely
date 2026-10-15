using System.Windows;
using System.Windows.Controls;

namespace Timely;

/// <summary>
/// Settings that can be changed directly in the window. Everything
/// applies on the spot — there is no OK/Cancel to get wrong.
/// </summary>
public partial class SettingsPanel : UserControl
{
    private AppSettings? _settings;
    private BreakScheduler? _scheduler;

    /// <summary>Set while the controls are being populated, so the change handlers stay quiet.</summary>
    private bool _loading;

    public SettingsPanel()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Called by the hosting window once it has the settings object. Not a
    /// constructor argument because XAML instantiates the control for us.
    /// </summary>
    public void Bind(BreakScheduler scheduler)
    {
        _scheduler = scheduler;
        _settings = scheduler.Settings;

        _loading = true;
        Interval20.IsChecked = _settings.IntervalMinutes == 20;
        Interval30.IsChecked = _settings.IntervalMinutes == 30;
        Mini20.IsChecked = _settings.MiniBreakSeconds == 20;
        Mini30.IsChecked = _settings.MiniBreakSeconds == 30;
        Mini60.IsChecked = _settings.MiniBreakSeconds == 60;
        LongOff.IsChecked = _settings.LongBreakEvery <= 0;
        LongEvery2.IsChecked = _settings.LongBreakEvery == 2;
        LongEvery3.IsChecked = _settings.LongBreakEvery == 3;
        LongEvery4.IsChecked = _settings.LongBreakEvery == 4;
        LongEvery5.IsChecked = _settings.LongBreakEvery == 5;
        LongDurationOptions.IsEnabled = _settings.LongBreakEvery > 0;
        Long1.IsChecked = _settings.LongBreakMinutes == 1;
        Long3.IsChecked = _settings.LongBreakMinutes == 3;
        Long5.IsChecked = _settings.LongBreakMinutes == 5;
        Warning10.IsChecked = _settings.PreNotifySeconds == 10;
        Warning30.IsChecked = _settings.PreNotifySeconds == 30;
        Warning60.IsChecked = _settings.PreNotifySeconds == 60;
        StrictSwitch.IsChecked = _settings.StrictMode;
        StrictSwitch.IsEnabled = !scheduler.IsBreakActive;
        AutostartSwitch.IsChecked = Autostart.IsEnabled;
        LangRu.IsChecked = Loc.Current == Lang.Ru;
        LangEn.IsChecked = Loc.Current == Lang.En;
        ThemeSystem.IsChecked = Theme.Choice == ThemeChoice.System;
        ThemeLight.IsChecked = Theme.Choice == ThemeChoice.Light;
        ThemeDark.IsChecked = Theme.Choice == ThemeChoice.Dark;
        _loading = false;

        ApplyTexts();
    }

    public void ApplyTexts()
    {
        IntervalLabel.Text = Loc.T("settings.interval");
        Interval20.Content = Loc.T("settings.interval.20m");
        Interval30.Content = Loc.T("settings.interval.30m");

        MiniDurationLabel.Text = Loc.T("settings.miniDuration");
        Mini20.Content = Loc.T("settings.miniDuration.20s");
        Mini30.Content = Loc.T("settings.miniDuration.30s");
        Mini60.Content = Loc.T("settings.miniDuration.60s");

        LongFrequencyLabel.Text = Loc.T("settings.longFrequency");
        LongOff.Content = Loc.T("settings.longFrequency.off");
        LongEvery2.Content = Loc.T("settings.longFrequency.2");
        LongEvery3.Content = Loc.T("settings.longFrequency.3");
        LongEvery4.Content = Loc.T("settings.longFrequency.4");
        LongEvery5.Content = Loc.T("settings.longFrequency.5");

        LongDurationLabel.Text = Loc.T("settings.longDuration");
        Long1.Content = Loc.T("settings.longDuration.1m");
        Long3.Content = Loc.T("settings.longDuration.3m");
        Long5.Content = Loc.T("settings.longDuration.5m");

        WarningLabel.Text = Loc.T("settings.warning");
        Warning10.Content = Loc.T("settings.warning.10s");
        Warning30.Content = Loc.T("settings.warning.30s");
        Warning60.Content = Loc.T("settings.warning.60s");

        StrictLabel.Text = Loc.T("settings.strict");
        StrictHint.Text = Loc.T("settings.strict.hint");

        AutostartLabel.Text = Loc.T("settings.autostart");
        AutostartHint.Text = Loc.T("settings.autostart.hint");

        LanguageLabel.Text = Loc.T("settings.language");
        LangEn.Content = Loc.T("settings.language.en");
        LangRu.Content = Loc.T("settings.language.ru");

        ThemeLabel.Text = Loc.T("settings.theme");
        ThemeSystem.Content = Loc.T("settings.theme.system");
        ThemeLight.Content = Loc.T("settings.theme.light");
        ThemeDark.Content = Loc.T("settings.theme.dark");
    }

    internal void RefreshAutostart()
    {
        var wasLoading = _loading;
        _loading = true;
        try { AutostartSwitch.IsChecked = Autostart.IsEnabled; }
        finally { _loading = wasLoading; }
    }

    internal void RefreshBreakControls()
    {
        StrictSwitch.IsEnabled = _scheduler?.IsBreakActive != true;
    }

    private void OnStrictChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || _settings is null || _scheduler is null) return;
        if (_scheduler.IsBreakActive)
        {
            _loading = true;
            StrictSwitch.IsChecked = _settings.StrictMode;
            _loading = false;
            return;
        }
        _settings.StrictMode = StrictSwitch.IsChecked == true;
        _settings.Save();
    }

    private void OnIntervalChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || _settings is null || _scheduler is null) return;
        if (sender is not RadioButton { Tag: string value } || !int.TryParse(value, out var minutes)) return;

        _scheduler.SetIntervalMinutes(minutes);
        _settings.Save();
    }

    private void OnMiniDurationChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || _settings is null || _scheduler is null) return;
        if (sender is not RadioButton { Tag: string value } || !int.TryParse(value, out var seconds)) return;
        _scheduler.SetMiniBreakSeconds(seconds);
        _settings.Save();
    }

    private void OnLongFrequencyChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || _settings is null || _scheduler is null) return;
        if (sender is not RadioButton { Tag: string value } || !int.TryParse(value, out var every)) return;
        _scheduler.SetLongBreakEvery(every);
        LongDurationOptions.IsEnabled = every > 0;
        _settings.Save();
    }

    private void OnLongDurationChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || _settings is null || _scheduler is null) return;
        if (sender is not RadioButton { Tag: string value } || !int.TryParse(value, out var minutes)) return;
        _scheduler.SetLongBreakMinutes(minutes);
        _settings.Save();
    }

    private void OnWarningChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || _settings is null || _scheduler is null) return;
        if (sender is not RadioButton { Tag: string value } || !int.TryParse(value, out var seconds)) return;
        _scheduler.SetPreNotifySeconds(seconds);
        _settings.Save();
    }

    private void OnAutostartChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;

        var wanted = AutostartSwitch.IsChecked == true;
        if (Autostart.Set(wanted)) return;

        // The registry write failed. Showing the switch in the position the user
        // asked for would be a lie about what happens at the next login.
        _loading = true;
        AutostartSwitch.IsChecked = Autostart.IsEnabled;
        _loading = false;
    }

    private void OnLanguageChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || _settings is null) return;

        var lang = LangRu.IsChecked == true ? Lang.Ru : Lang.En;
        _settings.LanguageChoice = lang;
        _settings.Save();
        Loc.Set(lang);
    }

    private void OnThemeChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || _settings is null) return;

        var choice = ThemeDark.IsChecked == true ? ThemeChoice.Dark
            : ThemeLight.IsChecked == true ? ThemeChoice.Light
            : ThemeChoice.System;

        _settings.ThemeChoice = choice;
        _settings.Save();
        Theme.Set(choice);
    }
}
