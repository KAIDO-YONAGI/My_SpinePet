using System.ComponentModel;
using System.Windows;
using SpinePet.Infrastructure;
using SpinePet.Models;
using SpinePet.Services;

namespace SpinePet.Views;

internal sealed class MainWindowLifecycleController : IDisposable
{
    private readonly Window _window;
    private readonly CharacterManager _characterManager;
    private int _allowClose;
    private int _disposeState;

    public MainWindowLifecycleController(
        Window window,
        CharacterManager characterManager)
    {
        _window = window;
        _characterManager = characterManager;
        LifetimeCancellation = new CancellationTokenSource();
    }

    public CancellationTokenSource LifetimeCancellation { get; }

    public CancellationToken LifetimeToken =>
        LifetimeCancellation.Token;

    public bool IsConfigMode { get; private set; } = true;

    public bool IsDisposed =>
        Volatile.Read(ref _disposeState) != 0;

    /// <summary>Raised after the panel switched to configuration mode.</summary>
    public event Action? ConfigModeEntered;

    public void HandleLoaded(Action applyThumbnailScale)
    {
        Rect workArea = SystemParameters.WorkArea;
        double width = GlobalConfig.DefaultConfigPanelWidth;
        double height = workArea.Height * 0.6;
        _window.Width = Math.Clamp(width, _window.MinWidth, workArea.Width);
        _window.Height = Math.Clamp(height, _window.MinHeight, workArea.Height);
        _window.Left = workArea.Right - _window.Width;
        _window.Top = workArea.Top;
        applyThumbnailScale();
        ApplyConfigMode();
    }

    public void SwitchToConfigMode()
    {
        if (IsDisposed ||
            Volatile.Read(ref _allowClose) != 0 ||
            _window.Dispatcher.HasShutdownStarted ||
            _window.Dispatcher.HasShutdownFinished)
        {
            return;
        }

        AppLogger.Write(
            nameof(MainWindowLifecycleController),
            "configuration-panel-opened");
        IsConfigMode = true;
        ApplyConfigMode();
        _window.Show();
        _window.Activate();
        ConfigModeEntered?.Invoke();
    }

    public void PrepareForShutdown()
    {
        Interlocked.Exchange(ref _allowClose, 1);
        Dispose();
    }

    public void HandleClosing(CancelEventArgs e)
    {
        if (_window.Dispatcher.HasShutdownStarted ||
            _window.Dispatcher.HasShutdownFinished)
        {
            return;
        }

        AppLogger.Write(
            nameof(MainWindowLifecycleController),
            "configuration-panel-close-intercepted");
        e.Cancel = true;
        _characterManager.SaveAllState();
        IsConfigMode = false;
        ApplyConfigMode();
    }

    public void ApplyConfigMode()
    {
        if (IsConfigMode)
        {
            _window.Topmost = true;
            _window.Show();
            _window.Activate();
        }
        else
        {
            _window.Hide();
        }

        _characterManager.SetConfigMode(IsConfigMode);
    }

    public void ExitConfiguration()
    {
        AppLogger.Write(
            nameof(MainWindowLifecycleController),
            "configuration-finished");
        _characterManager.SaveAllState();
        IsConfigMode = false;
        ApplyConfigMode();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        try
        {
            LifetimeCancellation.Cancel();
        }
        catch (Exception exception)
        {
            AppLogger.Write(
                nameof(MainWindowLifecycleController),
                $"lifetime-cancellation-failed message={exception.Message}");
        }
        finally
        {
            LifetimeCancellation.Dispose();
        }

        GC.SuppressFinalize(this);
    }
}
