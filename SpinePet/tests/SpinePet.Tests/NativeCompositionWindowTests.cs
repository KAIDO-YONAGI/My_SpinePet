using System.Drawing;
using System.Runtime.InteropServices;
using SpinePet.Rendering.Native;

namespace SpinePet.Tests;

public sealed class NativeCompositionWindowTests
{
    [Fact]
    public void RenderWindowNeverHasAWindowRegion()
    {
        using NativeCompositionWindow window = new();
        IntPtr region = CreateRectRgn(0, 0, 0, 0);
        Assert.NotEqual(IntPtr.Zero, region);
        try
        {
            Assert.Equal(0, GetWindowRgn(window.Handle, region));
        }
        finally
        {
            DeleteObject(region);
        }
    }

    [Fact]
    public void RenderWindowIsLayeredAndInputTransparent()
    {
        using NativeCompositionWindow window = new();
        const long wsExTransparent = 0x00000020;
        const long wsExLayered = 0x00080000;

        long style = GetWindowLongPtr(
            window.Handle,
            GwlExStyle).ToInt64();

        Assert.NotEqual(0, style & wsExTransparent);
        Assert.NotEqual(0, style & wsExLayered);
    }

    [Fact]
    public void RenderWindowDoesNotWinWindowFromPoint()
    {
        using NativeCompositionWindow window = new();
        NativePoint point = new()
        {
            X = window.Left + window.Width / 2,
            Y = window.Top + window.Height / 2
        };

        Assert.NotEqual(window.Handle, WindowFromPoint(point));
    }

    [Fact]
    public void LayeredRenderWindowAcceptsDirectCompositionTarget()
    {
        using NativeCompositionWindow window = new();
        using NativeGraphicsDevice graphics = new(window.Handle);

        graphics.Commit();
    }

    [Fact]
    public void InputRegionIncludesRenderingSafetyMargin()
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
    public void PhysicalWorkingAreaIsConvertedToClientPixels()
    {
        Rectangle result =
            NativeCharacterRenderHost.ToClientPixelRectangle(
                new Rectangle(0, 31, 1707, 929),
                windowLeft: 0,
                windowTop: 0);

        Assert.Equal(
            Rectangle.FromLTRB(0, 31, 1707, 960),
            result);
    }

    [Fact]
    public void InputRegionNeverIncludesTaskbarArea()
    {
        Rectangle inputRegion = new(0, 0, 2560, 1440);
        Rectangle bottomTaskbarWorkArea = new(0, 0, 2560, 1392);

        Rectangle clipped = Assert.Single(
            NativeCharacterRenderHost.ClipToWorkingAreas(
                inputRegion,
                [bottomTaskbarWorkArea]));

        Assert.Equal(bottomTaskbarWorkArea, clipped);
        Assert.False(clipped.Contains(100, 1420));
    }

    private const int GwlExStyle = -20;

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

    [DllImport(
        "user32.dll",
        EntryPoint = "GetWindowLongPtrW",
        SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(
        IntPtr window,
        int index);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(NativePoint point);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr handle);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }
}
