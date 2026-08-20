using System.Collections.Concurrent;
using System.Drawing;
using System.Runtime.InteropServices;
using SpinePet.Infrastructure;

namespace SpinePet.Rendering.Native;

internal sealed class NativeInputWindow : IDisposable
{
    private const uint CsOwnDc = 0x0020;
    private const uint WsPopup = 0x80000000;
    private const uint WsExTopmost = 0x00000008;
    private const uint WsExToolWindow = 0x00000080;
    private const uint WsExNoActivate = 0x08000000;
    private const uint WmNcHitTest = 0x0084;
    private const uint WmMouseMove = 0x0200;
    private const uint WmLeftButtonDown = 0x0201;
    private const uint WmLeftButtonUp = 0x0202;
    private const uint WmRightButtonDown = 0x0204;
    private const uint WmCaptureChanged = 0x0215;
    private const int HitClient = 1;
    private const int RegionOr = 2;
    private const int IdcHand = 32649;
    private const int SwShowNoActivate = 4;
    private const int SwHide = 0;

    private static readonly object RegistrationLock = new();
    private static readonly ConcurrentDictionary<
        IntPtr,
        NativeInputWindow> Instances = new();
    private static readonly WindowProcedure WindowProcedureCallback =
        WindowProcedureRouter;
    private static readonly string WindowClassName =
        $"SpinePet.NativeInput.{Environment.ProcessId}";
    private static ushort _windowClass;

    private readonly List<Rectangle> _regions = [];
    private readonly List<Rectangle> _normalizedRegionBuffer = [];
    private bool _hasRegion;
    private bool _visible = true;

    public NativeInputWindow(
        int left,
        int top,
        int width,
        int height)
    {
        EnsureWindowClass();
        Left = left;
        Top = top;
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
        Handle = CreateWindowEx(
            WsExTopmost |
            WsExToolWindow |
            WsExNoActivate,
            WindowClassName,
            "SpinePet Native Input Host",
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
                $"Unable to create native input window. Win32 error: {Marshal.GetLastWin32Error()}");
        }

        Instances[Handle] = this;
        SetInteractiveRegions([]);
        ShowWindow(Handle, SwShowNoActivate);
    }

    public IntPtr Handle { get; private set; }
    public int Left { get; }
    public int Top { get; }
    public int Width { get; }
    public int Height { get; }
    public Action<uint, int, int>? MouseInput { get; set; }
    internal int RegionApplyCount { get; private set; }

    public void SetInteractiveRegions(
        IReadOnlyCollection<Rectangle> regions)
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
                _normalizedRegionBuffer.Add(normalized);
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

        if (_hasRegion && RegionsEqual(_regions, _normalizedRegionBuffer))
            return;

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
                            nameof(NativeInputWindow),
                            $"combine-region-failed error={Marshal.GetLastWin32Error()}");
                        return;
                    }
                }
                finally
                {
                    DeleteObject(part);
                }
            }

            transferred = SetWindowRgn(
                Handle,
                combinedRegion,
                redraw: false) != 0;
            if (transferred)
            {
                _regions.Clear();
                _regions.AddRange(_normalizedRegionBuffer);
                _hasRegion = true;
                RegionApplyCount++;
            }
        }
        finally
        {
            if (!transferred)
                DeleteObject(combinedRegion);
        }
    }

    public void ClearAndHide()
    {
        SetInteractiveRegions([]);
        if (_visible && Handle != IntPtr.Zero)
        {
            _visible = false;
            ShowWindow(Handle, SwHide);
        }
    }

    public void Show()
    {
        if (!_visible && Handle != IntPtr.Zero)
        {
            _visible = true;
            ShowWindow(Handle, SwShowNoActivate);
        }
    }

    private static bool RegionsEqual(
        List<Rectangle> left,
        List<Rectangle> right)
    {
        if (left.Count != right.Count)
            return false;

        for (int index = 0; index < left.Count; index++)
        {
            if (left[index] != right[index])
                return false;
        }

        return true;
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
                Cursor = LoadCursor(IntPtr.Zero, new IntPtr(IdcHand)),
                ClassName = WindowClassName
            };
            _windowClass = RegisterClassEx(ref registration);
            if (_windowClass == 0)
            {
                throw new InvalidOperationException(
                    $"Unable to register native input window. Win32 error: {Marshal.GetLastWin32Error()}");
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
                out NativeInputWindow? instance))
        {
            try
            {
                // 区域是唯一命中门槛：系统只会把落在窗口区域内的点发过来，
                // 因此恒返 HTCLIENT，命中测试无任何副作用。
                if (message == WmNcHitTest)
                    return new IntPtr(HitClient);

                if (message == WmLeftButtonDown ||
                    message == WmRightButtonDown ||
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
                    nameof(NativeInputWindow),
                    $"input-failed message={exception.Message}");
            }
        }

        return DefWindowProc(window, message, wParam, lParam);
    }

    public void Dispose()
    {
        IntPtr handle = Handle;
        Handle = IntPtr.Zero;
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

    [DllImport(
        "user32.dll",
        EntryPoint = "LoadCursorW",
        SetLastError = true)]
    private static extern IntPtr LoadCursor(
        IntPtr instance,
        IntPtr resourceName);

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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowRgn(
        IntPtr window,
        IntPtr region,
        [MarshalAs(UnmanagedType.Bool)] bool redraw);

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
