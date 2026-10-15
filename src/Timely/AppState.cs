using System;
using System.IO;
using System.Text.Json;

namespace Timely;

/// <summary>
/// Everything the scheduler needs to survive a restart. Stretchly keeps this
/// only in memory, so each of its restarts silently threw the countdown away.
/// </summary>
public sealed class AppState
{
    /// <summary>Elapsed interval time since the last break, excluding explicit pauses.</summary>
    public double ActiveMs { get; set; }

    public int MiniBreaksSinceLong { get; set; }

    public DateTime LastTickUtc { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static AppState Load()
    {
        try
        {
            if (File.Exists(Paths.StateFile))
            {
                var state = JsonSerializer.Deserialize<AppState>(File.ReadAllText(Paths.StateFile), JsonOptions);
                if (state is not null)
                {
                    state.Normalize();
                    return state;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("could not read state, starting fresh", ex);
        }

        return new AppState { LastTickUtc = DateTime.UtcNow };
    }

    public void Save()
    {
        try
        {
            AtomicFile.WriteAllText(Paths.StateFile, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception ex)
        {
            Log.Error("could not write state", ex);
        }
    }

    internal void Normalize()
    {
        ActiveMs = double.IsFinite(ActiveMs)
            ? Math.Clamp(ActiveMs, 0, TimeSpan.FromMinutes(600).TotalMilliseconds)
            : 0;
        MiniBreaksSinceLong = Math.Clamp(MiniBreaksSinceLong, 0, int.MaxValue - 1);
    }
}
