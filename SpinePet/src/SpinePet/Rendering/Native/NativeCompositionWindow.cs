using System.Collections.Concurrent;
using System.Drawing;
using System.Runtime.InteropServices;
using SpinePet.Infrastructure;

namespace SpinePet.Rendering.Native;

internal sealed class NativeCompositionWindow : IDisposable
{
    private const uint CsOwnDc = 0x0020;
    private const uint WsPopup = 0x80000000;
    private const uint WsExTopmost = 0x00000008;
    private const uint WsExTransparent = 0x00000020;
    private const uint WsExToolWindow = 0x00000080;
    private const uint WsExNoRedirectionBitmap = 0x00200000;
    private const uint WsExNoActivate = 0x08000000;
    private const uint WmNcHitTest = 0x0084;
    private const uint WmMouseMove = 0x0200;
    private const uint WmLeftButtonDown = 0x0201;
    private const uint WmLeftButtonUp = 0x0202;
    private const uint WmCaptureChanged = 0x0215;
    private const int HitTransparent = -1;
    private const int HitClient = 1;
    private const int GwlExStyle = -20;
    private const int SwShowNoActivate = 4;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private const int RegionOr = 2;
    private const int RegionDiff = 4;
    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;
    private const int SmCxVirtualScreen = 78;
    private const int SmCyVirtualScreen = 79;

    private static readonly object RegistrationLock = new();
    private static readonly ConcurrentDictionary<
        IntPtr,
        NativeCompositionWindow> Instances = new();
    private static readonly WindowProcedure WindowProcedureCallback =
        WindowProcedureRouter;
    private static readonly string WindowClassName =
        $"SpinePet.NativeComposition.{Environment.ProcessId}";
    private static ushort _windowClass;

    private readonly List<Rectangle> _interactiveRegions = [];
    private readonly List<Rectangle> _normalizedRegionBuffer = [];
    private Rectangle? _passThroughHole;
    private bool _hasInteractiveRegions;
    private bool _inputEnabled;

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

        Instances[Handle] = this;
        uint dpi = GetDpiForWindow(Handle);
        DpiScale = dpi > 0 ? dpi / 96f : 1f;
        SetInteractiveRegions([]);
        ShowWindow(Handle, SwShowNoActivate);
    }

    public IntPtr Handle { get; private set; }
    public int Left { get; }
    public int Top { get; }
    public int Width { get; }
    public int Height { get; }
    public float DpiScale { get; }
    public Func<int, int, bool>? HitTestScreenPoint { get; set; }
    public Action<uint, int, int>? MouseInput { get; set; }

    public void SetInteractiveRegions(
        IReadOnlyCollection<Rectangle> regions,
        Rectangle? passThroughHole = null)
    {
        if (Handle == IntPtr.Zero)
            return;

        Rectangle windowBounds = new(0, 0, Width, Height);
        _normalizedRegionBuffer.Clear();
        foreach (Rectangle region in regions)
        {
            Rectangle normalized = Rectangle.Intersect(
                windowBounds,
                region);
            if (normalized.Width > 0 && normalized.Height > 0)
            {
                _normalizedRegionBuffer.Add(normalized);
            }
        }

        _normalizedRegionBuffer.Sort(static (left, right) =>
        {
            int comparison = left.X.CompareTo(right.X);
            if (comparison != 0)
                return comparison;
            comparison = left.Y.CompareTo(right.Y);
            if (comparison != 0)
                return comparison;
            comparison = left.Width.CompareTo(right.Width);
            return comparison != 0
                ? comparison
                : left.Height.CompareTo(right.Height);
        });
        Rectangle? normalizedHole = passThroughHole is { } hole
            ? Rectangle.Intersect(windowBounds, hole)
            : null;
        if (normalizedHole is { Width: <= 0 } or { Height: <= 0 })
            normalizedHole = null;
        if (_hasInteractiveRegions &&
            RegionsEqual(
                _interactiveRegions,
                _normalizedRegionBuffer) &&
            _passThroughHole == normalizedHole)
        {
            return;
        }

        IntPtr combinedRegion = CreateRectRgn(0, 0, 0, 0);
        if (combinedRegion == IntPtr.Zero)
            return;

        bool transferred = false;
        try
        {
            foreach (Rectangle rectangle in _normalizedRegionBuffer)
            {
                IntPtr part = CreateRectRgn(
                    rectangle.Left,
                    rectangle.Top,
                    rectangle.Right,
                    rectangle.Bottom);
                if (part == IntPtr.Zero)
                    continue;

                try
                {
                    if (CombineRgn(
                            combinedRegion,
                            combinedRegion,
                            part,
                            RegionOr) == 0)
                    {
                        AppLogger.Write(
                            nameof(NativeCompositionWindow),
                            $"combine-region-failed error={Marshal.GetLastWin32Error()}");
                        return;
                    }
                }
                finally
                {
                    DeleteObject(part);
                }
            }

            if (normalizedHole is { } excluded)
            {
                IntPtr holeRegion = CreateRectRgn(
                    excluded.Left,
                    excluded.Top,
                    excluded.Right,
                    excluded.Bottom);
                if (holeRegion != IntPtr.Zero)
                {
                    try
                    {
                        if (CombineRgn(
                                combinedRegion,
                                combinedRegion,
                                holeRegion,
                                RegionDiff) == 0)
                        {
                            AppLogger.Write(
                                nameof(NativeCompositionWindow),
                                $"subtract-region-failed error={Marshal.GetLastWin32Error()}");
                            return;
                        }
                    }
                    finally
                    {
                        DeleteObject(holeRegion);
                    }
                }
            }

            transferred = SetWindowRgn(
                Handle,
                combinedRegion,
                redraw: false) != 0;
            if (transferred)
            {
                _interactiveRegions.Clear();
                _interactiveRegions.AddRange(_normalizedRegionBuffer);
                _passThroughHole = normalizedHole;
                _hasInteractiveRegions = true;
            }
        }
        finally
        {
            if (!transferred)
                DeleteObject(combinedRegion);
        }
    }

    private static bool RegionsEqual(
        List<Rectangle> left,
        List<Rectangle> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (int index = 0; index < left.Count; index++)
        {
            if (left[index] != right[index])
            {
                return false;
            }
        }

        return true;
    }

    public void SetInputEnabled(bool enabled)
    {
        if (Handle == IntPtr.Zero || _inputEnabled == enabled)
            return;

        long style = GetWindowLongPtr(Handle, GwlExStyle).ToInt64();
        long updatedStyle = enabled
            ? style & ~WsExTransparent
            : style | WsExTransparent;
        SetWindowLongPtr(
            Handle,
            GwlExStyle,
            new IntPtr(updatedStyle));
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
        _inputEnabled = enabled;
    }

    public static bool TryGetCursorPosition(out int x, out int y)
    {
        bool succeeded = GetCursorPos(out NativePoint point);
        x = point.X;
        y = point.Y;
        return succeeded;
    }

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
                Style = CsOwnDc,
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
        IntPtr lParam)
    {
        if (Instances.TryGetValue(
                window,
                out NativeCompositionWindow? instance))
        {
            try
            {
                if (message == WmNcHitTest)
                {
                    GetSignedPoint(lParam, out int x, out int y);
                    return instance.HitTestScreenPoint?.Invoke(x, y) == true
                        ? new IntPtr(HitClient)
                        : new IntPtr(HitTransparent);
                }

                if (message == WmLeftButtonDown ||
                    message == WmMouseMove ||
                    message == WmLeftButtonUp)
                {
                    if (message == WmLeftButtonDown)
                        SetCapture(window);

                    GetCursorPos(out NativePoint point);
                    instance.MouseInput?.Invoke(
                        message,
                        point.X,
                        point.Y);

                    if (message == WmLeftButtonUp)
                        ReleaseCapture();
                    return IntPtr.Zero;
                }

                if (message == WmCaptureChanged)
                {
                    GetCursorPos(out NativePoint point);
                    instance.MouseInput?.Invoke(
                        message,
                        point.X,
                        point.Y);
                }
            }
            catch (Exception exception)
            {
                AppLogger.Write(
                    nameof(NativeCompositionWindow),
                    $"input-failed message={exception.Message}");
                if (message == WmNcHitTest)
                    return new IntPtr(HitTransparent);
            }
        }

        return DefWindowProc(window, message, wParam, lParam);
    }

    private static void GetSignedPoint(
        IntPtr packedPoint,
        out int x,
        out int y)
    {
        long value = packedPoint.ToInt64();
        x = (short)(value & 0xffff);
        y = (short)((value >> 16) & 0xffff);
    }

    public void Dispose()
    {
        IntPtr handle = Handle;
        Handle = IntPtr.Zero;
        HitTestScreenPoint = null;
        MouseInput = null;
        if (handle != IntPtr.Zero)
        {
            Instances.TryRemove(handle, out _);
            DestroyWindow(handle);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
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
    private static extern ushort RegisterClassEx(ref WindowClass windowClass);

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
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(
        IntPtr window,
        uint message,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);

    [DllImport(
        "user32.dll",
        EntryPoint = "GetWindowLongPtrW",
        SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(
        IntPtr window,
        int index);

    [DllImport(
        "user32.dll",
        EntryPoint = "SetWindowLongPtrW",
        SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(
        IntPtr window,
        int index,
        IntPtr value);

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

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRectRgn(
        int left,
        int top,
        int right,
        int bottom);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern int CombineRgn(
        IntPtr destination,
        IntPtr source1,
        IntPtr source2,
        int mode);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr handle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowRgn(
        IntPtr window,
        IntPtr region,
        [MarshalAs(UnmanagedType.Bool)] bool redraw);

    [DllImport("user32.dll")]
    private static extern IntPtr SetCapture(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport(
        "kernel32.dll",
        EntryPoint = "GetModuleHandleW",
        CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
}
