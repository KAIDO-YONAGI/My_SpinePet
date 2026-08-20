using System.Drawing;
using System.Runtime.InteropServices;
using SpinePet.Rendering.Native;

namespace SpinePet.Tests;

public sealed class NativeInputWindowTests
{
    private const uint WmNcHitTest = 0x0084;
    private static readonly IntPtr HitTransparent = new(-1);
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
    public void PassThroughHoleLeavesAdjacentInputPixelsIntact()
    {
        using NativeInputWindow window = new(0, 0, 1000, 800);
        window.SetInteractiveRegions(
            [new Rectangle(100, 120, 240, 360)],
            new Rectangle(150, 200, 1, 1));

        IntPtr region = CreateRectRgn(0, 0, 0, 0);
        Assert.NotEqual(IntPtr.Zero, region);
        try
        {
            Assert.True(GetWindowRgn(window.Handle, region) > 0);
            Assert.False(PtInRegion(region, 150, 200));
            Assert.True(PtInRegion(region, 151, 200));
        }
        finally
        {
            DeleteObject(region);
        }
    }

    [Fact]
    public void CharacterHitAndOverflowPassThroughAreSeparated()
    {
        using NativeCompositionWindow renderWindow = new();
        using NativeInputWindow inputWindow = new(
            renderWindow.Left,
            renderWindow.Top,
            renderWindow.Width,
            renderWindow.Height);
        Rectangle inputRegion = new(300, 150, 600, 300);
        Rectangle characterRegion = new(500, 200, 100, 100);
        inputWindow.SetInteractiveRegions([inputRegion]);
        inputWindow.HitTestScreenPoint = (x, y) =>
            characterRegion.Contains(
                x - inputWindow.Left,
                y - inputWindow.Top);

        NativePoint characterPoint = new()
        {
            X = inputWindow.Left + characterRegion.Left + 20,
            Y = inputWindow.Top + characterRegion.Top + 20
        };
        NativePoint overflowPoint = new()
        {
            X = inputWindow.Left + inputRegion.Left + 20,
            Y = inputWindow.Top + inputRegion.Top + 20
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

        Assert.Equal(
            HitTransparent,
            SendMessage(
                inputWindow.Handle,
                WmNcHitTest,
                IntPtr.Zero,
                PackScreenPoint(overflowPoint.X, overflowPoint.Y)));
        Assert.NotEqual(
            inputWindow.Handle,
            WindowFromPoint(overflowPoint));
        Assert.NotEqual(
            renderWindow.Handle,
            WindowFromPoint(overflowPoint));
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
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PtInRegion(
        IntPtr region,
        int x,
        int y);

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
