using System;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace Timely;

public enum ThemeChoice
{
    System,
    Light,
    Dark
}

/// <summary>
/// Colour palette as a swappable resource dictionary. Windows bind every colour
/// with DynamicResource, so switching the theme repaints what is already on
/// screen instead of requiring the app to be restarted.
/// </summary>
public static class Theme
{
    public static event Action? Changed;

    public static ThemeChoice Choice { get; private set; } = ThemeChoice.System;

    public static bool IsDark => Choice switch
    {
        ThemeChoice.Dark => true,
        ThemeChoice.Light => false,
        _ => SystemPrefersDark()
    };

    public static ThemeChoice Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "dark" => ThemeChoice.Dark,
        "light" => ThemeChoice.Light,
        _ => ThemeChoice.System
    };

    public static string Code(ThemeChoice choice) => choice switch
    {
        ThemeChoice.Dark => "dark",
        ThemeChoice.Light => "light",
        _ => "system"
    };

    public static void Set(ThemeChoice choice)
    {
        if (Choice == choice && _applied is not null) return;
        Choice = choice;
        Apply();
    }

    /// <summary>Follow the OS while the choice is "system".</summary>
    public static void FollowSystem()
    {
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category != UserPreferenceCategory.General || Choice != ThemeChoice.System) return;

            // SystemEvents fires on its own thread; resources are UI-thread state.
            var app = Application.Current;
            if (app is null) return;
            app.Dispatcher.BeginInvoke(new Action(Apply));
        };
    }

    private static ResourceDictionary? _applied;
    private static bool _lastAppliedDark;

    private static void Apply()
    {
        var app = Application.Current;
        if (app is null) return;

        var dark = IsDark;
        if (_applied is not null && _lastAppliedDark == dark) return;

        var next = dark ? DarkPalette() : LightPalette();

        if (_applied is not null) app.Resources.MergedDictionaries.Remove(_applied);
        app.Resources.MergedDictionaries.Add(next);

        _applied = next;
        _lastAppliedDark = dark;

        Log.Info($"theme applied: {Code(Choice)} -> {(dark ? "dark" : "light")}");
        Changed?.Invoke();
    }

    private static bool SystemPrefersDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            // AppsUseLightTheme: 1 light, 0 dark. Missing key means an older
            // build with no dark mode at all, so light.
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (Exception ex)
        {
            Log.Error("could not read the system theme, assuming light", ex);
            return false;
        }
    }

    private static ResourceDictionary DarkPalette() => Build(
        overlay: "#F2121618",
        surface: "#161D21",
        border: "#25323A",
        accent: "#4FD1C5",
        onAccent: "#0C1518",
        text: "#ECF3F6",
        textSecondary: "#93A6B0",
        textMuted: "#5C6E78",
        controlBg: "#1B2429",
        controlBgHover: "#25333A",
        controlBorder: "#2E3D45",
        controlText: "#B7C6CE",
        track: "#1C262B",
        thumb: "#ECF3F6");

    private static ResourceDictionary LightPalette() => Build(
        overlay: "#F2F6F9FA",
        surface: "#FFFFFF",
        border: "#D8E2E7",
        accent: "#0E9384",
        onAccent: "#FFFFFF",
        text: "#132029",
        textSecondary: "#4C5F6A",
        textMuted: "#8598A2",
        controlBg: "#F1F5F7",
        controlBgHover: "#E4EBEF",
        controlBorder: "#CCD9DF",
        controlText: "#3B4C56",
        track: "#DEE7EB",
        thumb: "#FFFFFF");

    private static ResourceDictionary Build(
        string overlay, string surface, string border, string accent, string onAccent,
        string text, string textSecondary, string textMuted,
        string controlBg, string controlBgHover, string controlBorder, string controlText,
        string track, string thumb)
    {
        var d = new ResourceDictionary();
        d["T.Overlay"] = Brush(overlay);
        d["T.Surface"] = Brush(surface);
        d["T.Border"] = Brush(border);
        d["T.Accent"] = Brush(accent);
        d["T.OnAccent"] = Brush(onAccent);
        d["T.Text"] = Brush(text);
        d["T.TextSecondary"] = Brush(textSecondary);
        d["T.TextMuted"] = Brush(textMuted);
        d["T.ControlBg"] = Brush(controlBg);
        d["T.ControlBgHover"] = Brush(controlBgHover);
        d["T.ControlBorder"] = Brush(controlBorder);
        d["T.ControlText"] = Brush(controlText);
        d["T.Track"] = Brush(track);
        d["T.Thumb"] = Brush(thumb);
        return d;
    }

    private static SolidColorBrush Brush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
