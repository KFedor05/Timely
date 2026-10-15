using System;
using System.Collections.Generic;
using System.Globalization;

namespace Timely;

public enum Lang
{
    En,
    Ru
}

/// <summary>
/// Two-language string table. Deliberately a plain dictionary rather than
/// satellite .resx assemblies: the app has a few dozen strings and has to be
/// able to switch language at runtime, which resx does not do without
/// recreating every window.
/// </summary>
public static class Loc
{
    /// <summary>Raised when the language changes, so live UI can relabel itself.</summary>
    public static event Action? Changed;

    public static Lang Current { get; private set; } = SystemDefault();

    public static Lang SystemDefault() =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("ru", StringComparison.OrdinalIgnoreCase)
            ? Lang.Ru
            : Lang.En;

    public static Lang Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "ru" => Lang.Ru,
        "en" => Lang.En,
        _ => SystemDefault()
    };

    public static string Code(Lang lang) => lang == Lang.Ru ? "ru" : "en";

    public static void Set(Lang lang)
    {
        if (Current == lang) return;
        Current = lang;
        Log.Info($"language switched to {Code(lang)}");
        Changed?.Invoke();
    }

    public static string T(string key)
    {
        var table = Current == Lang.Ru ? Ru : En;
        if (table.TryGetValue(key, out var value)) return value;
        // Falling back to English rather than to the key keeps a missed
        // translation readable instead of showing "tray.exit" to the user.
        return En.TryGetValue(key, out var fallback) ? fallback : key;
    }

    public static string T(string key, params object[] args) =>
        string.Format(CultureInfo.CurrentCulture, T(key), args);

    public static string[] Ideas(BreakKind kind)
    {
        var ru = Current == Lang.Ru;
        return kind == BreakKind.Long
            ? (ru ? LongIdeasRu : LongIdeasEn)
            : (ru ? MiniIdeasRu : MiniIdeasEn);
    }

    private static readonly Dictionary<string, string> En = new()
    {
        ["tray.breakNow"] = "Break now",
        ["tray.reset"] = "Reset the counter",
        ["tray.pause"] = "Pause",
        ["tray.pause.30m"] = "30 minutes",
        ["tray.pause.1h"] = "1 hour",
        ["tray.pause.2h"] = "2 hours",
        ["tray.pause.forever"] = "Until I turn it back on",
        ["tray.resume"] = "Resume",
        ["tray.open"] = "Open Timely",
        ["tray.log"] = "Log",
        ["tray.exit"] = "Exit",

        ["main.controls"] = "Controls",
        ["main.settings"] = "Settings",
        ["main.close"] = "Close",

        ["break.mini"] = "Mini break",
        ["break.long"] = "Long break",
        ["break.skip"] = "Skip",
        ["break.escHint"] = "Esc to skip",
        ["break.strictHint"] = "Strict mode — wait until the break ends",
        ["break.imminent"] = "{0} in {1} s",

        ["status.inBreak"] = "Break in progress",
        ["status.pausedForever"] = "Paused until turned back on",
        ["status.pausedFor"] = "Paused for another {0}",
        ["status.next"] = "{0} in {1}",

        ["settings.interval"] = "Break interval",
        ["settings.interval.20m"] = "20 minutes",
        ["settings.interval.30m"] = "30 minutes",
        ["settings.miniDuration"] = "Mini break duration",
        ["settings.miniDuration.20s"] = "20 seconds",
        ["settings.miniDuration.30s"] = "30 seconds",
        ["settings.miniDuration.60s"] = "1 minute",
        ["settings.longDuration"] = "Long break duration",
        ["settings.longDuration.1m"] = "1 minute",
        ["settings.longDuration.3m"] = "3 minutes",
        ["settings.longDuration.5m"] = "5 minutes",
        ["settings.longFrequency"] = "Long breaks",
        ["settings.longFrequency.off"] = "Off",
        ["settings.longFrequency.2"] = "Every 2nd",
        ["settings.longFrequency.3"] = "Every 3rd",
        ["settings.longFrequency.4"] = "Every 4th",
        ["settings.longFrequency.5"] = "Every 5th",
        ["settings.warning"] = "Warn before a break",
        ["settings.warning.10s"] = "10 seconds",
        ["settings.warning.30s"] = "30 seconds",
        ["settings.warning.60s"] = "1 minute",
        ["settings.strict"] = "Strict mode",
        ["settings.strict.hint"] = "Finish the whole break. Cannot be changed during a break.",
        ["settings.autostart"] = "Start with Windows",
        ["settings.autostart.hint"] = "Launches Timely when you sign in.",
        ["settings.language"] = "Language",
        ["settings.language.en"] = "English",
        ["settings.language.ru"] = "Русский",
        ["settings.theme"] = "Theme",
        ["settings.theme.system"] = "System",
        ["settings.theme.light"] = "Light",
        ["settings.theme.dark"] = "Dark",
        ["settings.rest"] = "Additional options are in settings.json.",
        ["settings.openJson"] = "Open settings.json"
    };

    private static readonly Dictionary<string, string> Ru = new()
    {
        ["tray.breakNow"] = "Перерыв сейчас",
        ["tray.reset"] = "Сбросить счётчик",
        ["tray.pause"] = "Пауза",
        ["tray.pause.30m"] = "30 минут",
        ["tray.pause.1h"] = "1 час",
        ["tray.pause.2h"] = "2 часа",
        ["tray.pause.forever"] = "До включения вручную",
        ["tray.resume"] = "Продолжить",
        ["tray.open"] = "Открыть Timely",
        ["tray.log"] = "Лог",
        ["tray.exit"] = "Выход",

        ["main.controls"] = "Управление",
        ["main.settings"] = "Настройки",
        ["main.close"] = "Закрыть",

        ["break.mini"] = "Мини-перерыв",
        ["break.long"] = "Длинный перерыв",
        ["break.skip"] = "Пропустить",
        ["break.escHint"] = "Esc — пропустить",
        ["break.strictHint"] = "Строгий режим — дождись окончания перерыва",
        ["break.imminent"] = "{0} через {1} сек",

        ["status.inBreak"] = "Перерыв идёт",
        ["status.pausedForever"] = "Пауза — до включения вручную",
        ["status.pausedFor"] = "Пауза ещё {0}",
        ["status.next"] = "{0} через {1}",

        ["settings.interval"] = "Интервал между перерывами",
        ["settings.interval.20m"] = "20 минут",
        ["settings.interval.30m"] = "30 минут",
        ["settings.miniDuration"] = "Длительность мини-перерыва",
        ["settings.miniDuration.20s"] = "20 секунд",
        ["settings.miniDuration.30s"] = "30 секунд",
        ["settings.miniDuration.60s"] = "1 минута",
        ["settings.longDuration"] = "Длительность длинного перерыва",
        ["settings.longDuration.1m"] = "1 минута",
        ["settings.longDuration.3m"] = "3 минуты",
        ["settings.longDuration.5m"] = "5 минут",
        ["settings.longFrequency"] = "Длинный перерыв",
        ["settings.longFrequency.off"] = "Выключен",
        ["settings.longFrequency.2"] = "Каждый 2-й",
        ["settings.longFrequency.3"] = "Каждый 3-й",
        ["settings.longFrequency.4"] = "Каждый 4-й",
        ["settings.longFrequency.5"] = "Каждый 5-й",
        ["settings.warning"] = "Предупреждать за",
        ["settings.warning.10s"] = "10 секунд",
        ["settings.warning.30s"] = "30 секунд",
        ["settings.warning.60s"] = "1 минуту",
        ["settings.strict"] = "Строгий режим",
        ["settings.strict.hint"] = "Перерыв нужно пройти целиком. Во время перерыва изменить нельзя.",
        ["settings.autostart"] = "Запускать вместе с Windows",
        ["settings.autostart.hint"] = "Timely поднимется сам при входе в систему.",
        ["settings.language"] = "Язык",
        ["settings.language.en"] = "English",
        ["settings.language.ru"] = "Русский",
        ["settings.theme"] = "Тема",
        ["settings.theme.system"] = "Системная",
        ["settings.theme.light"] = "Светлая",
        ["settings.theme.dark"] = "Тёмная",
        ["settings.rest"] = "Дополнительные параметры — в settings.json.",
        ["settings.openJson"] = "Открыть settings.json"
    };

    private static readonly string[] MiniIdeasEn =
    {
        "Look at something six metres away.",
        "Blink a few times — your eyes have dried out.",
        "Straighten your shoulders and drop them down.",
        "Look away from the screen and let your eyes unfocus.",
        "Turn your head left and right, slowly.",
        "Take three deep breaths."
    };

    private static readonly string[] LongIdeasEn =
    {
        "Stand up and walk. To the window at least.",
        "Pour a glass of water and drink it.",
        "Stretch your wrists and neck.",
        "Leave the room — a change of scenery counts.",
        "Look out of the window into the distance for a couple of minutes.",
        "Do a few squats, seriously."
    };

    private static readonly string[] MiniIdeasRu =
    {
        "Посмотри на что-нибудь в 6 метрах от себя.",
        "Поморгай несколько раз — глаза пересохли.",
        "Расправь плечи, опусти их вниз.",
        "Отведи взгляд от экрана и просто расфокусируй его.",
        "Покрути головой влево-вправо, медленно.",
        "Сделай три глубоких вдоха."
    };

    private static readonly string[] LongIdeasRu =
    {
        "Встань и пройдись. Хотя бы до окна.",
        "Налей воды и выпей.",
        "Разомни запястья и шею.",
        "Выйди из комнаты — смена обстановки считается.",
        "Посмотри в окно вдаль пару минут.",
        "Сделай пару приседаний, серьёзно."
    };
}
