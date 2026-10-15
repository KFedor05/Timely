using System;
using System.IO;

namespace Timely;

internal static class Paths
{
    public static string DataDir { get; } = CreateDataDir();

    public static string SettingsFile => Path.Combine(DataDir, "settings.json");
    public static string StateFile => Path.Combine(DataDir, "state.json");
    public static string LogFile => Path.Combine(DataDir, "timely.log");

    private static string CreateDataDir()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Timely");
        Directory.CreateDirectory(dir);
        return dir;
    }
}
