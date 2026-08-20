using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using SpinePet.Infrastructure;

namespace SpinePet.Rendering.Native;

internal sealed class NativeFrameScheduler : IDisposable
{
    private const uint CreateWaitableTimerHighResolution = 0x00000002;
    private const uint TimerAllAccess = 0x001F0003;
    private const uint Infinite = 0xFFFFFFFF;
    private const uint WaitObject0 = 0;

    private readonly object _syncRoot = new();
    private readonly Dispatcher _dispatcher;
    private readonly Action _tick;
    private readonly IntPtr _waitableTimer;
    private readonly Thread _thread;
    private TimeSpan _interval;
    private int _scheduleVersion;
    private int _dispatchPending;
    private int _dispatchAgain;
    private bool _running;
    private bool _disposed;

    public NativeFrameScheduler(
        Dispatcher dispatcher,
        Action tick,
        TimeSpan initialInterval)
    {
        _dispatcher = dispatcher;
        _tick = tick;
        _interval = initialInterval;
        _waitableTimer = CreateWaitableTimerEx(
            IntPtr.Zero,
            null,
            CreateWaitableTimerHighResolution,
            TimerAllAccess);
        if (_waitableTimer == IntPtr.Zero)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Unable to create the high-resolution frame timer.");
        }

        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "SpinePet.FrameScheduler"
        };
        _thread.Start();
    }

    public bool IsRunning
    {
        get
        {
            lock (_syncRoot)
            {
                return _running && !_disposed;
            }
        }
    }

    public TimeSpan Interval
    {
        get
        {
            lock (_syncRoot)
            {
                return _interval;
            }
        }
    }

    public void Start()
    {
        lock (_syncRoot)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_running)
            {
                return;
            }

            _running = true;
            _scheduleVersion++;
            Monitor.PulseAll(_syncRoot);
        }
    }

    public void Stop()
    {
        lock (_syncRoot)
        {
            if (!_running)
            {
                return;
            }

            _running = false;
            _scheduleVersion++;
            Volatile.Write(ref _dispatchAgain, 0);
        }
    }

    public void SetInterval(TimeSpan interval)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            interval,
            TimeSpan.Zero);
        lock (_syncRoot)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_interval == interval)
            {
                return;
            }

            _interval = interval;
            _scheduleVersion++;
        }
    }

    public void Dispose()
    {
        lock (_syncRoot)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _running = false;
            _scheduleVersion++;
            Monitor.PulseAll(_syncRoot);
        }

        if (Thread.CurrentThread != _thread)
        {
            _thread.Join(TimeSpan.FromSeconds(1));
        }

        CloseHandle(_waitableTimer);
    }

    private void Run()
    {
        try
        {
            RunCore();
        }
        catch (Exception exception)
        {
            lock (_syncRoot)
            {
                _running = false;
            }

            AppLogger.Write(
                nameof(NativeFrameScheduler),
                $"scheduler-failed error={exception.GetType().Name} " +
                $"message={exception.Message}");
        }
    }

    private void RunCore()
    {
        while (true)
        {
            int scheduleVersion;
            long intervalTicks;
            lock (_syncRoot)
            {
                while (!_disposed && !_running)
                {
                    Monitor.Wait(_syncRoot);
                }

                if (_disposed)
                {
                    return;
                }

                scheduleVersion = _scheduleVersion;
                intervalTicks = Math.Max(
                    1,
                    (long)Math.Round(
                        _interval.TotalSeconds * Stopwatch.Frequency));
            }

            long nextTimestamp = Stopwatch.GetTimestamp() + intervalTicks;
            while (true)
            {
                WaitUntil(nextTimestamp);
                lock (_syncRoot)
                {
                    if (_disposed)
                    {
                        return;
                    }

                    if (!_running || _scheduleVersion != scheduleVersion)
                    {
                        break;
                    }
                }

                DispatchTick();
                nextTimestamp += intervalTicks;
                long now = Stopwatch.GetTimestamp();
                if (nextTimestamp <= now - intervalTicks)
                {
                    nextTimestamp = now + intervalTicks;
                }
            }
        }
    }

    private void WaitUntil(long targetTimestamp)
    {
        long remainingTicks = targetTimestamp - Stopwatch.GetTimestamp();
        long dueTime100Nanoseconds = -Math.Max(
            1,
            remainingTicks * 10_000_000 / Stopwatch.Frequency);
        if (!SetWaitableTimer(
                _waitableTimer,
                ref dueTime100Nanoseconds,
                0,
                IntPtr.Zero,
                IntPtr.Zero,
                resume: false))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        if (WaitForSingleObject(_waitableTimer, Infinite) != WaitObject0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    private void DispatchTick()
    {
        if (Interlocked.Exchange(ref _dispatchPending, 1) != 0)
        {
            Volatile.Write(ref _dispatchAgain, 1);
            return;
        }

        try
        {
            _dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                () =>
                {
                    try
                    {
                        if (IsRunning)
                        {
                            _tick();
                        }
                    }
                    finally
                    {
                        Volatile.Write(ref _dispatchPending, 0);
                        if (Interlocked.Exchange(
                                ref _dispatchAgain,
                                0) != 0 &&
                            IsRunning)
                        {
                            DispatchTick();
                        }
                    }
                });
        }
        catch (InvalidOperationException)
        {
            Volatile.Write(ref _dispatchPending, 0);
            Volatile.Write(ref _dispatchAgain, 0);
        }
    }

    [DllImport(
        "kernel32.dll",
        EntryPoint = "CreateWaitableTimerExW",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    private static extern IntPtr CreateWaitableTimerEx(
        IntPtr timerAttributes,
        string? timerName,
        uint flags,
        uint desiredAccess);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWaitableTimer(
        IntPtr timer,
        ref long dueTime,
        int period,
        IntPtr completionRoutine,
        IntPtr completionArgument,
        [MarshalAs(UnmanagedType.Bool)] bool resume);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(
        IntPtr handle,
        uint milliseconds);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
