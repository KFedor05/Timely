using System;
using Microsoft.Win32;

namespace Timely;

/// <summary>
/// Per-user autostart via the HKCU Run key.
///
/// The registry is the single source of truth here on purpose: the user can
/// turn autostart off in Task Manager or in Windows Settings without telling
/// us, and a copy of the flag in settings.json would then be a lie that the app
/// would happily re-apply on the next launch.
/// </summary>
internal static class Autostart
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovalKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ValueName = "Timely";

    private static string? ExecutablePath => Environment.ProcessPath;

    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
                using var approval = Registry.CurrentUser.OpenSubKey(ApprovalKeyPath, writable: false);
                return key?.GetValue(ValueName) is string value && value.Length > 0
                    && IsApproved(approval?.GetValue(ValueName));
            }
            catch (Exception ex)
            {
                Log.Error("could not read the autostart entry", ex);
                return false;
            }
        }
    }

    public static bool Set(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (key is null)
            {
                Log.Error("could not open the Run key");
                return false;
            }

            if (enabled)
            {
                var exe = ExecutablePath;
                if (string.IsNullOrEmpty(exe))
                {
                    Log.Error("could not resolve the executable path, autostart not enabled");
                    return false;
                }

                key.SetValue(ValueName, Quote(exe), RegistryValueKind.String);
                // Re-enabling is an explicit user command. Remove Windows' disabled
                // record so the Run entry returns to its default enabled state.
                // Never do this during startup or when refreshing a stale path.
                using var approval = Registry.CurrentUser.OpenSubKey(ApprovalKeyPath, writable: true);
                approval?.DeleteValue(ValueName, throwOnMissingValue: false);
                Log.Info($"autostart enabled ({exe})");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                Log.Info("autostart disabled");
            }

            return IsEnabled == enabled;
        }
        catch (Exception ex)
        {
            Log.Error("could not write the autostart entry", ex);
            return false;
        }
    }

    /// <summary>
    /// If autostart is on but points at an old location — the exe was rebuilt
    /// elsewhere or moved — repoint it. Does nothing when autostart is off, so
    /// a user who turned it off stays turned off.
    /// </summary>
    public static void RepointIfStale()
    {
        try
        {
            if (!IsEnabled) return;
            var exe = ExecutablePath;
            if (string.IsNullOrEmpty(exe)) return;

            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key?.GetValue(ValueName) is not string current || current.Length == 0) return;

            var wanted = Quote(exe);
            if (string.Equals(current, wanted, StringComparison.OrdinalIgnoreCase)) return;

            key.SetValue(ValueName, wanted, RegistryValueKind.String);
            Log.Info($"autostart repointed from {current} to {wanted}");
        }
        catch (Exception ex)
        {
            Log.Error("could not refresh the autostart entry", ex);
        }
    }

    // StartupApproved is Windows-owned, undocumented binary state. Known enabled
    // values are 2 and 6; unknown or malformed records are treated as disabled.
    // A missing record means that Windows has not overridden the Run entry.
    internal static bool IsApproved(object? record) => record is null
        || record is byte[] { Length: >= 12 } bytes && BitConverter.ToUInt32(bytes, 0) is 2 or 6;

    private static string Quote(string path) => "\"" + path + "\"";
}
