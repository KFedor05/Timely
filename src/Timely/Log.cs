using System;
using System.IO;
using System.Text;

namespace Timely;

/// <summary>
/// Append-only text log. Diagnosing a break scheduler after the fact is
/// impossible without one — this is the single most useful thing the app writes.
/// </summary>
internal static class Log
{
    private const long MaxBytes = 2 * 1024 * 1024;

    private static readonly object Gate = new object();

    /// <summary>When set, lines go here instead of the log file (used by the simulator).</summary>
    internal static Action<string>? Sink;

    public static void Info(string message) => Write("info", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("error", ex is null ? message : message + ": " + ex);

    private static void Write(string level, string message)
    {
        var sink = Sink;
        if (sink is not null)
        {
            sink($"[{level}] {message}");
            return;
        }

        var line = string.Format("[{0:yyyy-MM-dd HH:mm:ss}] [{1}] {2}{3}",
            DateTime.Now, level, message, Environment.NewLine);

        lock (Gate)
        {
            try
            {
                TrimIfHuge();
                File.AppendAllText(Paths.LogFile, line, Encoding.UTF8);
            }
            catch
            {
                // Logging must never be able to take the app down.
            }
        }
    }

    private static void TrimIfHuge()
    {
        var info = new FileInfo(Paths.LogFile);
        if (!info.Exists || info.Length <= MaxBytes) return;

        var lines = File.ReadAllLines(Paths.LogFile, Encoding.UTF8);
        var keep = new string[lines.Length / 2];
        Array.Copy(lines, lines.Length - keep.Length, keep, 0, keep.Length);
        File.WriteAllLines(Paths.LogFile, keep, Encoding.UTF8);
    }
}
