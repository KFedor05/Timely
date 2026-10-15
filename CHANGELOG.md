# Changelog

## 0.1.0 — release candidate

The first public release is being prepared. This entry does not mean it has been published.

- Continuous break timer with no input-activity or presence detection.
- 20- or 30-minute intervals, with a fresh countdown when the interval changes.
- Mini-break durations of 20 seconds, 30 seconds, or 1 minute.
- Long-break durations of 1, 3, or 5 minutes.
- Long breaks every 2nd, 3rd, 4th, or 5th break, or completely disabled.
- Defaults: a 30-second mini break every 20 minutes, with a 5-minute long break every third break.
- Pause for 30 minutes, 1 hour, 2 hours, or until manually resumed.
- Break now, Skip, and manual counter reset.
- Tray progress and one advance notification, selectable at 10/30/60 seconds.
- Strict mode, off by default, blocks skipping, Esc, Alt+F4, tray exit,
  pause, and reset during a strict break.
- English and Russian UI; System, Light, and Dark themes.
- Start with Windows and reopen the existing window by launching the executable again.
- Atomic settings and state saves, with invalid numeric state normalization.
- Known Windows startup-approval states are respected; the switch refreshes on window activation.
- Scrollable settings constrained to the available screen area.
- Correct accounting around pause and reset commands between ticks.
- Retry after a break-window failure; overdue breaks do not show a misleading advance warning.
- .NET 10 LTS for the app, helper tools, and GitHub Actions.
- Windows x64 ZIP with bundled runtime, build metadata, license notices, and SHA-256.

Known limitations: primary-monitor-only breaks, JSON for custom intervals, and no
protection against forced process termination through Windows.
