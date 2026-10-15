using System;
using System.Runtime.InteropServices;

namespace Timely;

internal static class Native
{
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int dwProcessId);

    private const int AsfwAny = -1;

    /// <summary>
    /// Lets any process take the foreground away from this one. Called by a
    /// second launch before it wakes the running instance, otherwise Windows
    /// refuses the handover and the settings window opens behind everything.
    /// </summary>
    internal static void AllowForegroundHandover()
    {
        try
        {
            AllowSetForegroundWindow(AsfwAny);
        }
        catch (EntryPointNotFoundException)
        {
            // Nothing to do; worst case the window opens unfocused.
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmUseImmersiveDarkModePre20H1 = 19;

    /// <summary>
    /// Paints the OS title bar to match the app theme. Best effort: the
    /// attribute id changed in Windows 10 20H1 and does not exist at all on
    /// older builds, so both are tried and failures are ignored.
    /// </summary>
    internal static void SetTitleBarDarkMode(IntPtr hwnd, bool dark)
    {
        if (hwnd == IntPtr.Zero) return;

        var value = dark ? 1 : 0;
        try
        {
            if (DwmSetWindowAttribute(hwnd, DwmUseImmersiveDarkMode, ref value, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, DwmUseImmersiveDarkModePre20H1, ref value, sizeof(int));
        }
        catch (DllNotFoundException)
        {
            // No DWM. Nothing to theme.
        }
    }

}
