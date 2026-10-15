using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Timely;

public sealed class AppSettings
{
    /// <summary>Elapsed time between breaks, excluding explicit pauses.</summary>
    public int IntervalMinutes { get; set; } = 20;

    public int MiniBreakSeconds { get; set; } = 30;

    /// <summary>Every Nth break is a long one. 0 disables long breaks.</summary>
    public int LongBreakEvery { get; set; } = 3;

    public int LongBreakMinutes { get; set; } = 5;

    /// <summary>Share of the screen the break overlay covers.</summary>
    public int OverlayScreenPercent { get; set; } = 85;

    public bool AllowSkip { get; set; } = true;

    /// <summary>Require the whole break; applies when the next break starts.</summary>
    public bool StrictMode { get; set; } = false;

    /// <summary>One heads-up notification before the break. 0 disables it.</summary>
    public int PreNotifySeconds { get; set; } = 10;

    /// <summary>UI language: "en" or "ru". null follows the Windows display language.</summary>
    public string? Language { get; set; }

    /// <summary>"system", "light" or "dark".</summary>
    public string Theme { get; set; } = "system";

    // Autostart is not stored here on purpose — see Autostart.cs.

    [JsonIgnore]
    public Lang LanguageChoice
    {
        get => Loc.Parse(Language);
        set => Language = Loc.Code(value);
    }

    [JsonIgnore]
    public ThemeChoice ThemeChoice
    {
        get => Timely.Theme.Parse(Theme);
        set => Theme = Timely.Theme.Code(value);
    }

    [JsonIgnore]
    public TimeSpan Interval => TimeSpan.FromMinutes(Clamp(IntervalMinutes, 1, 600));

    [JsonIgnore]
    public TimeSpan MiniBreak => TimeSpan.FromSeconds(Clamp(MiniBreakSeconds, 5, 3600));

    [JsonIgnore]
    public TimeSpan LongBreak => TimeSpan.FromMinutes(Clamp(LongBreakMinutes, 1, 120));

    [JsonIgnore]
    public TimeSpan PreNotify => TimeSpan.FromSeconds(Clamp(PreNotifySeconds, 0, 300));

    [JsonIgnore]
    public double OverlayFraction => Clamp(OverlayScreenPercent, 30, 100) / 100.0;

    private static double Clamp(double value, double min, double max) =>
        value < min ? min : value > max ? max : value;

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static AppSettings Load()
    {
        AppSettings settings;
        try
        {
            settings = File.Exists(Paths.SettingsFile)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Paths.SettingsFile), JsonOptions) ?? new AppSettings()
                : new AppSettings();
        }
        catch (Exception ex)
        {
            Log.Error("could not read settings, falling back to defaults", ex);
            settings = new AppSettings();
        }

        // Write the file back so every knob is discoverable on first run.
        settings.Save();
        return settings;
    }

    public void Save()
    {
        try
        {
            AtomicFile.WriteAllText(Paths.SettingsFile, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception ex)
        {
            Log.Error("could not write settings", ex);
        }
    }

    public string Describe() => string.Format(
        "interval {0}, mini {1}, long {2} every {3}",
        Interval, MiniBreak, LongBreak, LongBreakEvery);
}
