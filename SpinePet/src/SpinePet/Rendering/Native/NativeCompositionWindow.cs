using System.Runtime.InteropServices;

namespace SpinePet.Rendering.Native;

internal sealed class NativeCompositionWindow : IDisposable
{
    private const uint WsPopup = 0x80000000;
    private const uint WsExTopmost = 0x00000008;
    private const uint WsExTransparent = 0x00000020;
    private const uint WsExToolWindow = 0x00000080;
    private const uint WsExLayered = 0x00080000;
    private const uint WsExNoRedirectionBitmap = 0x00200000;
    private const uint WsExNoActivate = 0x08000000;
    private const int SwShowNoActivate = 4;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;
    private const int SmCxVirtualScreen = 78;
    private const int SmCyVirtualScreen = 79;

    private static readonly object RegistrationLock = new();
    private static readonly WindowProcedure WindowProcedureCallback =
        WindowProcedureRouter;
    private static readonly string WindowClassName =
        $"SpinePet.NativeComposition.{Environment.ProcessId}";
    private static ushort _windowClass;

    public NativeCompositionWindow()
    {
        EnsureWindowClass();
        Left = GetSystemMetrics(SmXVirtualScreen);
        Top = GetSystemMetrics(SmYVirtualScreen);
        Width = Math.Max(1, GetSystemMetrics(SmCxVirtualScreen));
        Height = Math.Max(1, GetSystemMetrics(SmCyVirtualScreen));
        Handle = CreateWindowEx(
            WsExTopmost |
            WsExTransparent |
            WsExToolWindow |
            WsExLayered |
            WsExNoRedirectionBitmap |
            WsExNoActivate,
            WindowClassName,
            "SpinePet Native Render Host",
            WsPopup,
            Left,
            Top,
            Width,
            Height,
            IntPtr.Zero,
            IntPtr.Zero,
            GetModuleHandle(null),
            IntPtr.Zero);
        if (Handle == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"Unable to create native composition window. Win32 error: {Marshal.GetLastWin32Error()}");
        }

        uint dpi = GetDpiForWindow(Handle);
        DpiScale = dpi > 0 ? dpi / 96f : 1f;
        SetWindowPos(
            Handle,
            IntPtr.Zero,
            0,
            0,
            0,
            0,
            SwpNoSize |
            SwpNoMove |
            SwpNoZOrder |
            SwpNoActivate |
            SwpFrameChanged);
        ShowWindow(Handle, SwShowNoActivate);
    }

    public IntPtr Handle { get; private set; }
    public int Left { get; }
    public int Top { get; }
    public int Width { get; }
    public int Height { get; }
    public float DpiScale { get; }

    private static void EnsureWindowClass()
    {
        if (_windowClass != 0)
            return;

        lock (RegistrationLock)
        {
            if (_windowClass != 0)
                return;

            WindowClass registration = new()
            {
                Size = (uint)Marshal.SizeOf<WindowClass>(),
                Style = 0,
                WindowProcedure = Marshal.GetFunctionPointerForDelegate(
                    WindowProcedureCallback),
                Instance = GetModuleHandle(null),
                ClassName = WindowClassName
            };
            _windowClass = RegisterClassEx(ref registration);
            if (_windowClass == 0)
            {
                throw new InvalidOperationException(
                    $"Unable to register native composition window. Win32 error: {Marshal.GetLastWin32Error()}");
            }
        }
    }

    private static IntPtr WindowProcedureRouter(
        IntPtr window,
        uint message,
        IntPtr wParam,
        IntPtr lParam) =>
        DefWindowProc(window, message, wParam, lParam);

    public void Dispose()
    {
        IntPtr handle = Handle;
        Handle = IntPtr.Zero;
        if (handle != IntPtr.Zero)
            DestroyWindow(handle);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint Size;
        public uint Style;
        public IntPtr WindowProcedure;
        public int ClassExtra;
        public int WindowExtra;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr Background;
        public string? MenuName;
        public string ClassName;
        public IntPtr SmallIcon;
    }

    private delegate IntPtr WindowProcedure(
        IntPtr window,
        uint message,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport(
        "user32.dll",
        EntryPoint = "RegisterClassExW",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    private static extern ushort RegisterClassEx(
        ref WindowClass windowClass);

    [DllImport(
        "user32.dll",
        EntryPoint = "CreateWindowExW",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        uint extendedStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        IntPtr parent,
        IntPtr menu,
        IntPtr instance,
        IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(
        IntPtr window,
        int command);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(
        IntPtr window,
        uint message,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr window,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport(
        "kernel32.dll",
        EntryPoint = "GetModuleHandleW",
        CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
}
