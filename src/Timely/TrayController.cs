using System;
using System.Diagnostics;
using Drawing = System.Drawing;
using Drawing2D = System.Drawing.Drawing2D;
using WinForms = System.Windows.Forms;

namespace Timely;

/// <summary>
/// Tray icon and menu. The tooltip always shows what the scheduler actually
/// believes, so a stalled scheduler is visible instead of quietly showing a
/// stale "next break in 17 minutes".
/// </summary>
public sealed class TrayController : IDisposable
{
    private readonly BreakScheduler _scheduler;
    private readonly WinForms.NotifyIcon _icon;
    private readonly WinForms.ToolStripMenuItem _statusItem;
    private readonly WinForms.ToolStripMenuItem _breakNowItem;
    private readonly WinForms.ToolStripMenuItem _resetItem;
    private readonly WinForms.ToolStripMenuItem _pauseMenu;
    private readonly WinForms.ToolStripMenuItem _pause30Item;
    private readonly WinForms.ToolStripMenuItem _pause1hItem;
    private readonly WinForms.ToolStripMenuItem _pause2hItem;
    private readonly WinForms.ToolStripMenuItem _pauseForeverItem;
    private readonly WinForms.ToolStripMenuItem _resumeItem;
    private readonly WinForms.ToolStripMenuItem _openItem;
    private readonly WinForms.ToolStripMenuItem _logItem;
    private readonly WinForms.ToolStripMenuItem _exitItem;

    private Drawing.Icon? _currentIcon;
    private int _lastDrawnPercent = -1;
    private bool _lastDrawnPaused;

    public event Action? ExitRequested;
    public event Action? OpenRequested;

    public TrayController(BreakScheduler scheduler)
    {
        _scheduler = scheduler;

        _statusItem = new WinForms.ToolStripMenuItem("…") { Enabled = false };

        _pause30Item = new WinForms.ToolStripMenuItem(string.Empty,
            null, (_, _) => _scheduler.PauseFor(TimeSpan.FromMinutes(30)));
        _pause1hItem = new WinForms.ToolStripMenuItem(string.Empty,
            null, (_, _) => _scheduler.PauseFor(TimeSpan.FromHours(1)));
        _pause2hItem = new WinForms.ToolStripMenuItem(string.Empty,
            null, (_, _) => _scheduler.PauseFor(TimeSpan.FromHours(2)));
        _pauseForeverItem = new WinForms.ToolStripMenuItem(string.Empty,
            null, (_, _) => _scheduler.PauseIndefinitely());

        _pauseMenu = new WinForms.ToolStripMenuItem(string.Empty);
        _pauseMenu.DropDownItems.Add(_pause30Item);
        _pauseMenu.DropDownItems.Add(_pause1hItem);
        _pauseMenu.DropDownItems.Add(_pause2hItem);
        _pauseMenu.DropDownItems.Add(_pauseForeverItem);

        _resumeItem = new WinForms.ToolStripMenuItem(string.Empty,
            null, (_, _) => _scheduler.ResumeFromPause()) { Visible = false };

        _breakNowItem = new WinForms.ToolStripMenuItem(string.Empty,
            null, (_, _) => _scheduler.SkipToNextBreak());
        _resetItem = new WinForms.ToolStripMenuItem(string.Empty,
            null, (_, _) => _scheduler.ResetCounters());
        _openItem = new WinForms.ToolStripMenuItem(string.Empty,
            null, (_, _) => OpenRequested?.Invoke()) { Font = BoldMenuFont() };
        _logItem = new WinForms.ToolStripMenuItem(string.Empty,
            null, (_, _) => OpenInShell(Paths.LogFile));
        _exitItem = new WinForms.ToolStripMenuItem(string.Empty,
            null, (_, _) => ExitRequested?.Invoke());

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add(_statusItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(_openItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(_breakNowItem);
        menu.Items.Add(_resetItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(_pauseMenu);
        menu.Items.Add(_resumeItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(_logItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(_exitItem);

        _icon = new WinForms.NotifyIcon
        {
            ContextMenuStrip = menu,
            Visible = true
        };

        // Double-click opens the window, which is what the bold menu entry
        // promises and what every other tray app does.
        _icon.DoubleClick += (_, _) => OpenRequested?.Invoke();

        ApplyTexts();
        Refresh();

        Loc.Changed += ApplyTexts;
        _scheduler.StateChanged += Refresh;
        _scheduler.BreakImminent += OnBreakImminent;
    }

    private void ApplyTexts()
    {
        _breakNowItem.Text = Loc.T("tray.breakNow");
        _resetItem.Text = Loc.T("tray.reset");
        _pauseMenu.Text = Loc.T("tray.pause");
        _pause30Item.Text = Loc.T("tray.pause.30m");
        _pause1hItem.Text = Loc.T("tray.pause.1h");
        _pause2hItem.Text = Loc.T("tray.pause.2h");
        _pauseForeverItem.Text = Loc.T("tray.pause.forever");
        _resumeItem.Text = Loc.T("tray.resume");
        _openItem.Text = Loc.T("tray.open");
        _logItem.Text = Loc.T("tray.log");
        _exitItem.Text = Loc.T("tray.exit");

        Refresh();
    }

    /// <summary>The default menu action is shown in bold, the shell convention.</summary>
    private static Drawing.Font BoldMenuFont()
    {
        var baseFont = Drawing.SystemFonts.MenuFont ?? WinForms.Control.DefaultFont;
        return new Drawing.Font(baseFont, Drawing.FontStyle.Bold);
    }

    private void OnBreakImminent(BreakKind kind, TimeSpan lead)
    {
        var name = Loc.T(kind == BreakKind.Long ? "break.long" : "break.mini");
        _icon.ShowBalloonTip(
            (int)lead.TotalMilliseconds,
            "Timely",
            Loc.T("break.imminent", name, (int)lead.TotalSeconds),
            WinForms.ToolTipIcon.None);
    }

    private void Refresh()
    {
        var status = _scheduler.StatusLine();
        _statusItem.Text = status;

        // NotifyIcon.Text is capped at 63 characters by the shell.
        _icon.Text = status.Length > 63 ? status.Substring(0, 60) + "…" : status;

        var paused = _scheduler.IsPaused;
        _pauseMenu.Visible = !paused;
        _resumeItem.Visible = paused;

        var percent = (int)Math.Round(_scheduler.Progress * 100);
        if (percent != _lastDrawnPercent || paused != _lastDrawnPaused)
        {
            _lastDrawnPercent = percent;
            _lastDrawnPaused = paused;
            SetIcon(BuildIcon(percent / 100.0, paused));
        }
    }

    private void SetIcon(Drawing.Icon icon)
    {
        var previous = _currentIcon;
        _currentIcon = icon;
        _icon.Icon = icon;
        previous?.Dispose();
    }

    private static Drawing.Icon BuildIcon(double progress, bool paused)
    {
        using var bitmap = new Drawing.Bitmap(32, 32);
        using (var g = Drawing.Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Drawing.Color.Transparent);

            var ring = new Drawing.Rectangle(4, 4, 23, 23);

            using (var track = new Drawing.Pen(Drawing.Color.FromArgb(80, 190, 215, 225), 4f))
                g.DrawEllipse(track, ring);

            var fill = paused
                ? Drawing.Color.FromArgb(255, 150, 162, 170)
                : Drawing.Color.FromArgb(255, 79, 209, 197);

            using (var pen = new Drawing.Pen(fill, 4f))
                g.DrawArc(pen, ring, -90f, (float)(360.0 * Math.Clamp(progress, 0, 1)));
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var temp = Drawing.Icon.FromHandle(handle);
            return (Drawing.Icon)temp.Clone();
        }
        finally
        {
            Native.DestroyIcon(handle);
        }
    }

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

    public void Dispose()
    {
        Loc.Changed -= ApplyTexts;
        _scheduler.StateChanged -= Refresh;
        _scheduler.BreakImminent -= OnBreakImminent;

        _icon.Visible = false;
        _icon.Dispose();
        _currentIcon?.Dispose();
    }
}
