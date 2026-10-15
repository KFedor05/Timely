# First-release checklist

Candidate version: 0.1.0. Local validation date: October 15, 2026.
Source code has been pushed to GitHub. The first public binary release is still pending.

## Prepared and checked locally

- The app, SchedulerSim, IconGen, and GitHub Actions target .NET 10.
  global.json selects a stable SDK; local builds used SDK 10.0.402.
- Application builds without warnings or errors.
- Automated scenarios cover continuous timing, interval changes, sleep and restart,
  pauses, mini-break durations of 20/30/60 seconds, long-break durations of 1/3/5 minutes,
  long-break frequency of 2/3/4/5 or off, one notification at 10/30/60 seconds,
  strict mode, reset, legacy JSON, invalid state numbers, atomic replacement with a
  locked destination, StartupApproved parsing, and recovery from an overlay failure.
- WPF layout checks use a small 440×340 DIP viewport: bottom controls remain
  reachable through scrolling in both UI languages and both themes.
  This does not replace testing actual DPI changes.
- UI images were rendered from the real WPF controls and visually inspected.
- Autostart respects the Run entry and known StartupApproved disabled states.
  Activating the window refreshes the switch; application startup does not clear a disabled state.
- Settings and state use temporary files and atomic replacement.
- MIT license, README, changelog, settings image, and bug-report template are present.
- tools/Publish.ps1 creates a Windows x64 ZIP with .NET 10, documentation,
  runtime license notices, build-info.json, and SHA-256.
- GitHub Actions uses SDK 10, with external actions pinned to commit SHAs.
  It builds, checks, and uploads an artifact; it does not publish a public release.

## Before publishing the binary release

1. Test the ZIP on a separate Windows 11 x64 installation without .NET installed.
   A clean Windows installation or VM was not available for local validation.
2. Test autostart across a real Windows sign-in, including disabling it in Task Manager.
   Automated checks cover known approval values, not an actual sign-in.
   StartupApproved is an undocumented Windows format; unknown records are treated as disabled.
3. Test actual 100%, 150%, and 200% DPI settings and multiple monitors.
4. Confirm a successful GitHub Actions run for the exact commit being released.
5. Publish v0.1.0 using the ZIP and SHA-256 from that successful CI run.
   Add the release date to CHANGELOG.md. LICENSE currently names "Timely contributors".

## Manual smoke tests

- Extract the ZIP to a permanent path containing spaces and non-ASCII characters; run without elevation.
- Find the tray icon; open the window by double-clicking and by launching the executable again.
- Switch 20 → 30 → 20 minutes: each change starts a full interval and persists after exit.
- Select 20/30/60-second mini breaks and confirm persistence.
  A duration change during a break must apply only to future breaks.
- Select 1/3/5-minute long breaks and confirm persistence.
  An active long break must keep its original duration.
- Test every 2nd/3rd/4th/5th long break and Off.
  When off, all breaks must use the selected mini-break duration.
- Wait without mouse or keyboard input and check the selected warning, break timer,
  completion, skipping, and long-break cadence.
- Test timed and indefinite pauses, Resume, and Reset the counter.
- Enable strict mode before a break. Check that Skip is hidden and Esc,
  Alt+F4, tray exit, and changing strict mode cannot bypass it.
  Wait for automatic completion, then disable strict mode and check normal skipping.
- Switch both languages and all themes; verify that all controls remain reachable at different DPI settings.
- Test sleep, Windows lock, and restarting after a long shutdown.
- Enable and disable autostart; sign in again. Repeat through Task Manager.
- Test tray exit and replacing the app folder during an update.

## First-version limitations

- Breaks appear only on the primary monitor; no global hotkeys.
- Strict mode does not prevent forced termination through Windows or computer shutdown.
  In normal mode, allowSkip=false hides Skip and disables Esc but does not block Alt+F4.
- Sleep and application downtime count toward the interval. An overdue break may
  appear immediately after restart; missed breaks are not queued.
- Explicit pauses do not survive restarts.
- No publisher signature; Windows may show a SmartScreen warning.
