using System;
using System.Threading;

namespace Timely;

public partial class App : System.Windows.Application
{
    private const string ShowSettingsEventName = @"Local\Timely.ShowSettings";

    private Mutex? _instanceLock;
    private EventWaitHandle? _showSettingsSignal;
    private RegisteredWaitHandle? _showSettingsWait;
    private AppSettings? _settings;
    private BreakScheduler? _scheduler;
    private TrayController? _tray;
    private BreakWindow? _breakWindow;
    private MainWindow? _mainWindow;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        _instanceLock = new Mutex(true, @"Local\Timely.SingleInstance", out var isFirstInstance);
        if (!isFirstInstance)
        {
            // Launching a tray app that is already running is a request to see
            // it, not a no-op — silently exiting reads as "nothing happened".
            AskRunningInstanceToOpen();
            Shutdown();
            return;
        }

        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("unhandled exception on the UI thread", args.Exception);
            args.Handled = true;
        };

        _settings = AppSettings.Load();

        // Language and theme first: everything built below reads them.
        Loc.Set(_settings.LanguageChoice);
        Theme.Set(_settings.ThemeChoice);
        Theme.FollowSystem();

        // The exe may have been rebuilt somewhere else since autostart was set.
        Autostart.RepointIfStale();

        _scheduler = new BreakScheduler(_settings);
        _scheduler.BreakStarting += ShowBreak;

        _tray = new TrayController(_scheduler);
        _tray.ExitRequested += RequestExit;
        _tray.OpenRequested += ShowMainWindow;

        ListenForOpenRequests();

        _scheduler.Start();
        Log.Info($"Timely started, data in {Paths.DataDir}");
    }

    private void RequestExit()
    {
        if (_scheduler?.IsStrictBreakActive == true)
        {
            _breakWindow?.Activate();
            Log.Info("exit ignored during a strict break");
            return;
        }
        Shutdown();
    }

    private static void AskRunningInstanceToOpen()
    {
        try
        {
            if (EventWaitHandle.TryOpenExisting(ShowSettingsEventName, out var handle))
            {
                Native.AllowForegroundHandover();
                using (handle) handle.Set();
                Log.Info("already running — asked the live instance to open its window");
                return;
            }

            Log.Info("already running, but it is not listening for the open signal");
        }
        catch (Exception ex)
        {
            Log.Error("could not reach the running instance", ex);
        }
    }

    private void ListenForOpenRequests()
    {
        try
        {
            _showSettingsSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSettingsEventName);
            _showSettingsWait = ThreadPool.RegisterWaitForSingleObject(
                _showSettingsSignal,
                // Fired on a pool thread; windows belong to the UI thread.
                (_, _) => Dispatcher.BeginInvoke(new Action(ShowMainWindow)),
                state: null,
                millisecondsTimeOutInterval: Timeout.Infinite,
                executeOnlyOnce: false);
        }
        catch (Exception ex)
        {
            // Not being reachable from a second launch is a lost convenience,
            // not a reason to fail to start.
            Log.Error("could not set up the open signal", ex);
        }
    }

    private void ShowBreak(BreakKind kind, TimeSpan duration)
    {
        if (_breakWindow is not null || _settings is null) return;

        var window = new BreakWindow(kind, duration, _settings);
        _breakWindow = window;

        void OnFinished(BreakKind finishedKind, bool skipped)
        {
            _breakWindow = null;
            _scheduler?.FinishBreak(finishedKind, skipped);
        }

        window.Finished += OnFinished;
        try
        {
            window.Show();
            window.Activate();
        }
        catch
        {
            _breakWindow = null;
            window.Finished -= OnFinished;
            window.CloseAfterFailure();
            throw;
        }
    }

    private void ShowMainWindow()
    {
        if (_settings is null || _scheduler is null) return;

        if (_mainWindow is not null)
        {
            if (_mainWindow.WindowState == System.Windows.WindowState.Minimized)
                _mainWindow.WindowState = System.Windows.WindowState.Normal;
            _mainWindow.Activate();
            return;
        }

        var window = new MainWindow(_scheduler);
        _mainWindow = window;
        window.Closed += (_, _) => _mainWindow = null;

        window.Show();
        window.Activate();
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        _scheduler?.Dispose();
        _tray?.Dispose();
        _showSettingsWait?.Unregister(null);
        _showSettingsSignal?.Dispose();
        _instanceLock?.Dispose();
        Log.Info("Timely exited");
        base.OnExit(e);
    }
}
