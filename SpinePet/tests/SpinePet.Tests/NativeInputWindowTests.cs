using System.Drawing;
using System.Runtime.InteropServices;
using SpinePet.Rendering.Native;

namespace SpinePet.Tests;

public sealed class NativeInputWindowTests
{
    private const uint WmNcHitTest = 0x0084;
    private static readonly IntPtr HitClient = new(1);

    [Fact]
    public void SetInteractiveRegionsCreatesTheInputWindowRegion()
    {
        using NativeInputWindow window = new(0, 0, 1000, 800);
        Rectangle expected = new(100, 120, 240, 360);
        window.SetInteractiveRegions([expected]);

        IntPtr region = CreateRectRgn(0, 0, 0, 0);
        Assert.NotEqual(IntPtr.Zero, region);
        try
        {
            Assert.True(GetWindowRgn(window.Handle, region) > 0);
            Assert.True(
                GetRgnBox(region, out NativeRectangle bounds) > 0);
            Assert.Equal(expected.Left, bounds.Left);
            Assert.Equal(expected.Top, bounds.Top);
            Assert.Equal(expected.Right, bounds.Right);
            Assert.Equal(expected.Bottom, bounds.Bottom);
        }
        finally
        {
            DeleteObject(region);
        }
    }

    [Fact]
    public void CharacterRegionWinsHitTestWhileOverflowStaysReachable()
    {
        using NativeCompositionWindow renderWindow = new();
        using NativeInputWindow inputWindow = new(
            renderWindow.Left,
            renderWindow.Top,
            renderWindow.Width,
            renderWindow.Height);
        // 核心区=角色轮廓；轮廓之外的溢出区不进入区域，交由系统直接穿透。
        Rectangle characterRegion = new(500, 200, 100, 100);
        inputWindow.SetInteractiveRegions([characterRegion]);

        NativePoint characterPoint = new()
        {
            X = inputWindow.Left + characterRegion.Left + 20,
            Y = inputWindow.Top + characterRegion.Top + 20
        };
        NativePoint overflowPoint = new()
        {
            X = inputWindow.Left + 320,
            Y = inputWindow.Top + 170
        };

        Assert.Equal(
            HitClient,
            SendMessage(
                inputWindow.Handle,
                WmNcHitTest,
                IntPtr.Zero,
                PackScreenPoint(characterPoint.X, characterPoint.Y)));
        Assert.Equal(
            inputWindow.Handle,
            WindowFromPoint(characterPoint));

        Assert.NotEqual(
            inputWindow.Handle,
            WindowFromPoint(overflowPoint));
        Assert.NotEqual(
            renderWindow.Handle,
            WindowFromPoint(overflowPoint));
    }

    [Fact]
    public void RegionApplicationIsStableAcrossRepeatedHitTests()
    {
        using NativeInputWindow window = new(0, 0, 1000, 800);
        Rectangle region = new(100, 120, 240, 360);
        window.SetInteractiveRegions([region]);
        int applied = window.RegionApplyCount;
        Assert.True(applied >= 1);

        for (int attempt = 0; attempt < 50; attempt++)
        {
            window.SetInteractiveRegions([region]);
            SendMessage(
                window.Handle,
                WmNcHitTest,
                IntPtr.Zero,
                PackScreenPoint(200, 300));
            SendMessage(
                window.Handle,
                WmNcHitTest,
                IntPtr.Zero,
                PackScreenPoint(900, 700));
        }

        Assert.Equal(applied, window.RegionApplyCount);
    }

    [Fact]
    public void EmptyRegionsLeaveDesktopReachable()
    {
        using NativeCompositionWindow renderWindow = new();
        using NativeInputWindow inputWindow = new(
            renderWindow.Left,
            renderWindow.Top,
            renderWindow.Width,
            renderWindow.Height);
        inputWindow.SetInteractiveRegions([]);

        NativePoint center = new()
        {
            X = inputWindow.Left + inputWindow.Width / 2,
            Y = inputWindow.Top + inputWindow.Height / 2
        };

        Assert.NotEqual(inputWindow.Handle, WindowFromPoint(center));
        Assert.NotEqual(renderWindow.Handle, WindowFromPoint(center));
    }

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRectRgn(
        int left,
        int top,
        int right,
        int bottom);

    [DllImport("user32.dll")]
    private static extern int GetWindowRgn(
        IntPtr window,
        IntPtr region);

    [DllImport("gdi32.dll")]
    private static extern int GetRgnBox(
        IntPtr region,
        out NativeRectangle rectangle);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(
        IntPtr window,
        uint message,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(NativePoint point);

    private static IntPtr PackScreenPoint(int x, int y) =>
        new((y << 16) | (x & 0xffff));

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }
}
