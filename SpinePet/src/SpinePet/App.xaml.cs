using System.Diagnostics;
using System.Windows;
using SpinePet.Infrastructure;
using SpinePet.Models;
using SpinePet.Services;
using SpinePet.Views;
using Application = System.Windows.Application;

namespace SpinePet;

public partial class App : Application
{
    private const string SingleInstanceMutexName =
        @"Local\SpinePet.SingleInstance.v1";
    private const string ActivationEventName =
        @"Local\SpinePet.Activate.v1";

    private TrayIconService? _trayIcon;
    private CharacterManager? _characterManager;
    private MainWindow? _mainWindow;
    private EmergencyExitHotkey? _emergencyExitHotkey;
    private readonly ManualResetEventSlim _shutdownCompleted = new(false);
    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _activationEvent;
    private ManualResetEvent? _activationStop;
    private Thread? _activationThread;
    private int _shutdownRequested;
    private int _emergencyExitRequested;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(
            initiallyOwned: true,
            SingleInstanceMutexName,
            out bool isFirstInstance);
        if (!isFirstInstance)
        {
            AppLogger.Write(nameof(App), "duplicate-instance-blocked");
            SignalExistingInstance();
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            ShutdownApplication();
            return;
        }

        try
        {
            _activationEvent = new EventWaitHandle(
                initialState: false,
                EventResetMode.AutoReset,
                ActivationEventName);
        }
        catch (Exception exception)
        {
            AppLogger.Write(
                nameof(App),
                $"activation-event-create-failed " +
                $"message={exception.Message}");
        }

        _emergencyExitHotkey = new EmergencyExitHotkey(
            RequestEmergencyExit);
        _emergencyExitHotkey.Start();

        ConfigService configService = new();
        CharacterIdentityService identityService = new();
        CharacterResourceDiscoveryService resourceDiscovery =
            new(identityService);
        UnityBundleImportService bundleImporter =
            new(identityService, resourceDiscovery);
        _characterManager = new CharacterManager(configService, identityService);
        _mainWindow = new MainWindow(
            _characterManager,
            resourceDiscovery,
            bundleImporter);
        StartActivationListener();

        _trayIcon = new TrayIconService(
            openPanel: () => DispatchToUi(() => _mainWindow.SwitchToConfigMode()),
            showAll: () => DispatchToUi(() => _ = ShowAllCharactersAsync()),
            hideAll: () => DispatchToUi(_characterManager.HideAll),
            exit: () => DispatchToUi(ShutdownApplication)
        );
        _trayIcon.Initialize();

        SynchronizeDiscoveredResources(resourceDiscovery);
        _ = RestoreCharactersAsync();

        _mainWindow.Show();
    }

    private void SynchronizeDiscoveredResources(
        CharacterResourceDiscoveryService resourceDiscovery)
    {
        if (_characterManager == null)
        {
            return;
        }

        _characterManager.SynchronizeResources(
            resourceDiscovery.DiscoverAll(AppPaths.ResourceDirectory),
            AppPaths.ResourceDirectory);
    }

    private async Task RestoreCharactersAsync()
    {
        if (_characterManager == null)
        {
            return;
        }

        try
        {
            await _characterManager.RestoreAllAsync(configMode: false);
        }
        catch (Exception exception)
        {
            AppLogger.Write(
                nameof(App),
                $"restore-failed message={exception.Message}");
        }
    }

    private async Task ShowAllCharactersAsync()
    {
        if (_characterManager == null)
        {
            return;
        }

        try
        {
            await _characterManager.ShowAllAsync();
        }
        catch (Exception exception)
        {
            AppLogger.Write(
                nameof(App),
                $"show-all-failed message={exception.Message}");
        }
    }

    private void DispatchToUi(Action action)
    {
        MainWindow? mainWindow = _mainWindow;
        if (mainWindow == null ||
            IsShutdownRequested)
        {
            return;
        }

        if (mainWindow.Dispatcher.CheckAccess())
        {
            action();
            return;
        }

        if (!mainWindow.Dispatcher.HasShutdownStarted &&
            !mainWindow.Dispatcher.HasShutdownFinished)
        {
            mainWindow.Dispatcher.Invoke(action);
        }
    }

    private void ShutdownApplication()
    {
        PrepareForShutdown();
        Shutdown();
    }

    private bool IsShutdownRequested =>
        Volatile.Read(ref _shutdownRequested) != 0;

    private void PrepareForShutdown()
    {
        if (Interlocked.Exchange(ref _shutdownRequested, 1) != 0)
        {
            return;
        }

        RunExitStep(
            "stop-activation-listener",
            StopActivationListener);

        MainWindow? mainWindow = _mainWindow;
        _mainWindow = null;
        if (mainWindow != null)
        {
            RunExitStep(
                "prepare-main-window-shutdown",
                mainWindow.PrepareForShutdown);
        }
    }

    private static void SignalExistingInstance()
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                using EventWaitHandle activationEvent =
                    EventWaitHandle.OpenExisting(
                        ActivationEventName);
                activationEvent.Set();
                AppLogger.Write(
                    nameof(App),
                    "existing-instance-activation-signaled");
                return;
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                if (attempt < 19)
                {
                    Thread.Sleep(50);
                }
            }
            catch (Exception exception)
            {
                AppLogger.Write(
                    nameof(App),
                    $"existing-instance-activation-failed " +
                    $"message={exception.Message}");
                return;
            }
        }

        AppLogger.Write(
            nameof(App),
            "existing-instance-activation-unavailable");
    }

    private void StartActivationListener()
    {
        if (IsShutdownRequested ||
            _activationEvent == null ||
            _activationThread != null)
        {
            return;
        }

        _activationStop = new ManualResetEvent(false);
        _activationThread = new Thread(WatchForActivation)
        {
            IsBackground = true,
            Name = "SpinePet activation listener"
        };
        _activationThread.Start();
    }

    private void WatchForActivation()
    {
        EventWaitHandle? activationEvent = _activationEvent;
        ManualResetEvent? activationStop = _activationStop;
        if (activationEvent == null ||
            activationStop == null)
        {
            return;
        }

        WaitHandle[] waitHandles =
            [activationStop, activationEvent];
        try
        {
            while (!IsShutdownRequested &&
                   WaitHandle.WaitAny(waitHandles) == 1)
            {
                if (IsShutdownRequested ||
                    Dispatcher.HasShutdownStarted ||
                    Dispatcher.HasShutdownFinished)
                {
                    return;
                }

                try
                {
                    Dispatcher.BeginInvoke(() =>
                    {
                        if (IsShutdownRequested ||
                            Dispatcher.HasShutdownStarted ||
                            Dispatcher.HasShutdownFinished)
                        {
                            return;
                        }

                        MainWindow? mainWindow = _mainWindow;
                        if (mainWindow == null ||
                            mainWindow.IsDisposed)
                        {
                            return;
                        }

                        try
                        {
                            mainWindow.SwitchToConfigMode();
                        }
                        catch (Exception exception)
                        {
                            AppLogger.Write(
                                nameof(App),
                                $"activation-handler-failed " +
                                $"error={exception.GetType().Name} " +
                                $"message={exception.Message}");
                        }
                    });
                }
                catch (Exception exception)
                {
                    AppLogger.Write(
                        nameof(App),
                        $"activation-dispatch-failed " +
                        $"message={exception.Message}");
                }
            }
        }
        catch (ObjectDisposedException)
        {
            // A concurrent shutdown may have already released the wait handles.
        }
        catch (Exception exception)
        {
            AppLogger.Write(
                nameof(App),
                $"activation-listener-failed " +
                $"error={exception.GetType().Name} " +
                $"message={exception.Message}");
        }
    }

    private void StopActivationListener()
    {
        _activationStop?.Set();
        bool listenerStopped =
            _activationThread is not { IsAlive: true };
        if (_activationThread is { IsAlive: true } &&
            Thread.CurrentThread != _activationThread)
        {
            listenerStopped = _activationThread.Join(
                TimeSpan.FromMilliseconds(500));
        }

        if (!listenerStopped)
        {
            AppLogger.Write(
                nameof(App),
                "activation-listener-stop-timeout");
            return;
        }

        _activationThread = null;
        _activationEvent?.Dispose();
        _activationEvent = null;
        _activationStop?.Dispose();
        _activationStop = null;
    }

    protected override void OnSessionEnding(
        SessionEndingCancelEventArgs e)
    {
        base.OnSessionEnding(e);
        if (!e.Cancel)
        {
            PrepareForShutdown();
        }
    }

    private void RequestEmergencyExit()
    {
        if (Interlocked.Exchange(ref _emergencyExitRequested, 1) != 0)
            return;

        AppLogger.Write(
            nameof(App),
            "emergency-exit-requested shortcut=Ctrl+Alt+Shift+F12");

        try
        {
            Dispatcher.BeginInvoke(ShutdownApplication);
        }
        catch
        {
            // The watchdog below still terminates a broken dispatcher.
        }

        _ = Task.Run(() =>
        {
            if (_shutdownCompleted.Wait(TimeSpan.FromSeconds(1.5)))
                return;

            Process.GetCurrentProcess().Kill(entireProcessTree: true);
        });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            RunExitStep(
                "prepare-application-shutdown",
                PrepareForShutdown);
            RunExitStep(
                "stop-activation-listener",
                StopActivationListener);

            if (_characterManager != null)
            {
                RunExitStep(
                    "save-character-state",
                    _characterManager.SaveAllState);
                RunExitStep(
                    "close-character-manager",
                    _characterManager.Close);
            }

            if (_trayIcon != null)
            {
                RunExitStep(
                    "dispose-tray-icon",
                    _trayIcon.Dispose);
                _trayIcon = null;
            }

            if (_emergencyExitHotkey != null)
            {
                RunExitStep(
                    "dispose-emergency-hotkey",
                    _emergencyExitHotkey.Dispose);
                _emergencyExitHotkey = null;
            }

            if (_singleInstanceMutex != null)
            {
                RunExitStep(
                    "release-single-instance-mutex",
                    _singleInstanceMutex.ReleaseMutex);
                RunExitStep(
                    "dispose-single-instance-mutex",
                    _singleInstanceMutex.Dispose);
                _singleInstanceMutex = null;
            }

            RunExitStep(
                "base-application-exit",
                () => base.OnExit(e));
        }
        finally
        {
            _shutdownCompleted.Set();
        }
    }

    private static void RunExitStep(
        string step,
        Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            AppLogger.Write(
                nameof(App),
                $"exit-step-failed step={step} " +
                $"error={exception.GetType().Name} " +
                $"message={exception.Message}");
        }
    }
}
