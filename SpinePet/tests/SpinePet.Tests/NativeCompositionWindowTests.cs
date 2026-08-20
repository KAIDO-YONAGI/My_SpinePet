using System.Drawing;
using System.Runtime.InteropServices;
using SpinePet.Rendering.Native;

namespace SpinePet.Tests;

public sealed class NativeCompositionWindowTests
{
    private const uint WmNcHitTest = 0x0084;
    private static readonly IntPtr HitTransparent = new(-1);
    private static readonly IntPtr HitClient = new(1);

    [Fact]
    public void RenderingRegionExcludesDesktopOutsideCharacterArea()
    {
        using NativeCompositionWindow window = new();
        window.SetRenderingRegions(
            [new Rectangle(100, 120, 240, 360)]);

        IntPtr region = CreateRectRgn(0, 0, 0, 0);
        Assert.NotEqual(IntPtr.Zero, region);
        try
        {
            Assert.True(GetWindowRgn(window.Handle, region) > 0);
            Assert.True(GetRgnBox(region, out NativeRectangle bounds) > 0);
            Assert.True(PtInRegion(
                region,
                bounds.Left + bounds.Width / 2,
                bounds.Top + bounds.Height / 2));
            Assert.False(PtInRegion(
                region,
                bounds.Left - 1,
                bounds.Top - 1));
            Assert.False(PtInRegion(
                region,
                bounds.Right + 1,
                bounds.Bottom + 1));
            Assert.True(bounds.Width < window.Width);
            Assert.True(bounds.Height < window.Height);
        }
        finally
        {
            DeleteObject(region);
        }
    }

    [Fact]
    public void CharacterRegionIncludesRenderingSafetyMargin()
    {
        Rectangle result =
            NativeCharacterRenderHost.GetWindowRegionBounds(
                new RectangleF(510.25f, 240.75f, 120.5f, 300.5f),
                windowLeft: -100,
                windowTop: -50);

        Assert.Equal(
            Rectangle.FromLTRB(578, 258, 763, 624),
            result);
    }

    [Fact]
    public void LogicalWorkingAreaIsConvertedToClientPixels()
    {
        Rectangle result =
            NativeCharacterRenderHost.ToClientPixelRectangle(
                new Rectangle(0, 31, 1707, 929),
                dpiScale: 1.5f,
                windowLeft: 0,
                windowTop: 0);

        Assert.Equal(
            Rectangle.FromLTRB(0, 46, 2561, 1440),
            result);
    }

    [Fact]
    public void RenderingRegionUsesPhysicalClientPixels()
    {
        using NativeCompositionWindow window = new();
        window.SetRenderingRegions(
            [new Rectangle(300, 150, 600, 300)]);

        IntPtr region = CreateRectRgn(0, 0, 0, 0);
        Assert.NotEqual(IntPtr.Zero, region);
        try
        {
            Assert.True(GetWindowRgn(window.Handle, region) > 0);
            Assert.True(GetRgnBox(region, out NativeRectangle bounds) > 0);
            Assert.Equal(300, bounds.Left);
            Assert.Equal(150, bounds.Top);
            Assert.Equal(900, bounds.Right);
            Assert.Equal(450, bounds.Bottom);
        }
        finally
        {
            DeleteObject(region);
        }
    }

    [Fact]
    public void PassThroughHoleDoesNotShrinkRenderingRegion()
    {
        using NativeCompositionWindow window = new();
        Rectangle renderingRegion = new(100, 120, 240, 360);
        Rectangle passThroughPixel = new(150, 200, 1, 1);
        window.SetRenderingRegions(
            [renderingRegion],
            passThroughPixel);

        IntPtr region = CreateRectRgn(0, 0, 0, 0);
        Assert.NotEqual(IntPtr.Zero, region);
        try
        {
            Assert.True(GetWindowRgn(window.Handle, region) > 0);
            Assert.True(GetRgnBox(region, out NativeRectangle bounds) > 0);
            Assert.Equal(renderingRegion.Left, bounds.Left);
            Assert.Equal(renderingRegion.Top, bounds.Top);
            Assert.Equal(renderingRegion.Right, bounds.Right);
            Assert.Equal(renderingRegion.Bottom, bounds.Bottom);
            Assert.False(PtInRegion(region, 150, 200));
            Assert.True(PtInRegion(region, 151, 200));
        }
        finally
        {
            DeleteObject(region);
        }
    }

    [Fact]
    public void RenderingOverflowRemainsVisibleButReturnsTransparentHitTest()
    {
        using NativeCompositionWindow window = new();
        Rectangle renderingRegion = new(300, 150, 600, 300);
        Rectangle characterRegion = new(500, 200, 100, 100);
        window.SetRenderingRegions([renderingRegion]);
        window.HitTestScreenPoint = (x, y) =>
            characterRegion.Contains(x - window.Left, y - window.Top);

        IntPtr characterPoint = SendMessage(
            window.Handle,
            WmNcHitTest,
            IntPtr.Zero,
            PackScreenPoint(
                window.Left + characterRegion.Left + 20,
                window.Top + characterRegion.Top + 20));
        IntPtr overflowPoint = SendMessage(
            window.Handle,
            WmNcHitTest,
            IntPtr.Zero,
            PackScreenPoint(
                window.Left + renderingRegion.Left + 20,
                window.Top + renderingRegion.Top + 20));

        Assert.Equal(HitClient, characterPoint);
        Assert.Equal(HitTransparent, overflowPoint);
    }

    [Fact]
    public void CharacterRegionNeverIncludesTaskbarArea()
    {
        Rectangle characterRegion = new(0, 0, 2560, 1440);
        Rectangle bottomTaskbarWorkArea = new(0, 0, 2560, 1392);

        Rectangle clipped = Assert.Single(
            NativeCharacterRenderHost.ClipToWorkingAreas(
                characterRegion,
                [bottomTaskbarWorkArea]));

        Assert.Equal(bottomTaskbarWorkArea, clipped);
        Assert.False(clipped.Contains(100, 1420));
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

    private static IntPtr PackScreenPoint(int x, int y) =>
        new((y << 16) | (x & 0xffff));

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }
}
