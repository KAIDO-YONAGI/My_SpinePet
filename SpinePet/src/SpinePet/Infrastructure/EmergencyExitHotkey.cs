using System.Runtime.InteropServices;

namespace SpinePet.Infrastructure;

internal sealed class EmergencyExitHotkey : IDisposable
{
    private const int HotkeyId = 0x5350;
    private const int WmHotkey = 0x0312;
    private const int WmQuit = 0x0012;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModNoRepeat = 0x4000;
    private const uint VirtualKeyF12 = 0x7B;

    private readonly Action _onPressed;
    private readonly ManualResetEventSlim _started = new();
    private Thread? _messageThread;
    private uint _messageThreadId;
    private bool _disposed;

    public EmergencyExitHotkey(Action onPressed)
    {
        _onPressed = onPressed;
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_messageThread != null)
            return;

        _messageThread = new Thread(RunMessageLoop)
        {
            IsBackground = true,
            Name = "SpinePet emergency exit hotkey"
        };
        _messageThread.Start();
        _started.Wait(TimeSpan.FromSeconds(1));
    }

    private void RunMessageLoop()
    {
        _messageThreadId = GetCurrentThreadId();
        bool registered = RegisterHotKey(
            IntPtr.Zero,
            HotkeyId,
            ModControl | ModAlt | ModShift | ModNoRepeat,
            VirtualKeyF12);
        _started.Set();

        if (!registered)
        {
            AppLogger.Write(
                nameof(EmergencyExitHotkey),
                $"registration-failed error={Marshal.GetLastWin32Error()}");
            return;
        }

        AppLogger.Write(
            nameof(EmergencyExitHotkey),
            "registered shortcut=Ctrl+Alt+Shift+F12");

        try
        {
            while (GetMessage(out NativeMessage message, IntPtr.Zero, 0, 0) > 0)
            {
                if (message.Message == WmHotkey &&
                    message.WParam == (IntPtr)HotkeyId)
                {
                    _onPressed();
                }
            }
        }
        finally
        {
            UnregisterHotKey(IntPtr.Zero, HotkeyId);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        uint threadId = _messageThreadId;
        if (threadId != 0)
            PostThreadMessage(threadId, WmQuit, IntPtr.Zero, IntPtr.Zero);

        if (_messageThread is { IsAlive: true } &&
            Thread.CurrentThread != _messageThread)
        {
            _messageThread.Join(TimeSpan.FromMilliseconds(500));
        }

        _started.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(
        IntPtr windowHandle,
        int id,
        uint modifiers,
        uint virtualKey);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr windowHandle, int id);

    [DllImport("user32.dll")]
    private static extern int GetMessage(
        out NativeMessage message,
        IntPtr windowHandle,
        uint minimumMessage,
        uint maximumMessage);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(
        uint threadId,
        uint message,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public IntPtr WindowHandle;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public NativePoint Point;
        public uint Private;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }
}
