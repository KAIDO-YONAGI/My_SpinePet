using System.Drawing;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Spine;
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

    [Fact]
    public void SilhouetteRasterizationProducesGridAlignedRegion()
    {
        NativeTextureSource texture = CreateOpaqueTexture();
        NativeSpineDrawBatch batch = CreateQuadBatch(
            texture,
            (0, 0, 160, 160));

        IReadOnlyList<Rectangle> runs =
            NativeCharacterRenderHost.RasterizeSilhouette(
                [batch],
                RectangleF.FromLTRB(400, 400, 560, 560),
                anchorX: 400,
                anchorY: 400,
                pivotX: 0,
                pivotY: 0,
                pixelScale: 1,
                windowLeft: 0,
                windowTop: 0);

        Rectangle region = Assert.Single(runs);
        Assert.Equal(Rectangle.FromLTRB(400, 400, 560, 560), region);
    }

    [Fact]
    public void SilhouetteRasterizationKeepsGapBetweenQuadsTransparent()
    {
        NativeTextureSource texture = CreateOpaqueTexture();
        NativeSpineDrawBatch batch = CreateQuadBatch(
            texture,
            (0, 0, 80, 80),
            (120, 0, 200, 80));

        IReadOnlyList<Rectangle> runs =
            NativeCharacterRenderHost.RasterizeSilhouette(
                [batch],
                RectangleF.FromLTRB(400, 400, 600, 480),
                anchorX: 400,
                anchorY: 400,
                pivotX: 0,
                pivotY: 0,
                pixelScale: 1,
                windowLeft: 0,
                windowTop: 0);

        Assert.Equal(
        [
            Rectangle.FromLTRB(400, 400, 480, 480),
            Rectangle.FromLTRB(520, 400, 600, 480)
        ], runs);
    }

    [Fact]
    public void SilhouetteUsesCoarserCellsForLargeBounds()
    {
        NativeTextureSource texture = CreateOpaqueTexture();
        NativeSpineDrawBatch batch = CreateQuadBatch(
            texture,
            (0, 0, 160, 160));

        // 大边界自动把格子从 8px 倍增至 64px（78×78 格），仍保留轮廓，
        // 而不是退化为外接矩形。
        IReadOnlyList<Rectangle> runs =
            NativeCharacterRenderHost.RasterizeSilhouette(
                [batch],
                RectangleF.FromLTRB(0, 0, 4992, 4992),
                anchorX: 0,
                anchorY: 0,
                pivotX: 0,
                pivotY: 0,
                pixelScale: 1,
                windowLeft: 0,
                windowTop: 0);

        Rectangle region = Assert.Single(runs);
        Assert.Equal(Rectangle.FromLTRB(0, 0, 192, 192), region);
    }

    [Fact]
    public void SilhouetteFallsBackToInflatedBoundsForPathologicalSize()
    {
        NativeTextureSource texture = CreateOpaqueTexture();
        NativeSpineDrawBatch batch = CreateQuadBatch(
            texture,
            (0, 0, 160, 160));

        // 即使 128px 格子仍超出格数上限时，退化为外扩 12px 的对齐矩形。
        IReadOnlyList<Rectangle> runs =
            NativeCharacterRenderHost.RasterizeSilhouette(
                [batch],
                RectangleF.FromLTRB(0, 0, 17000, 17000),
                anchorX: 0,
                anchorY: 0,
                pivotX: 0,
                pivotY: 0,
                pixelScale: 1,
                windowLeft: 0,
                windowTop: 0);

        Rectangle fallback = Assert.Single(runs);
        Assert.Equal(
            Rectangle.FromLTRB(-16, -16, 17016, 17016),
            fallback);
    }

    [Fact]
    public void SilhouetteRasterizationReturnsEmptyWithoutBatches()
    {
        IReadOnlyList<Rectangle> runs =
            NativeCharacterRenderHost.RasterizeSilhouette(
                Array.Empty<NativeSpineDrawBatch>(),
                RectangleF.FromLTRB(0, 0, 160, 160),
                anchorX: 0,
                anchorY: 0,
                pivotX: 0,
                pivotY: 0,
                pixelScale: 1,
                windowLeft: 0,
                windowTop: 0);

        Assert.Empty(runs);
    }

    private static NativeTextureSource CreateOpaqueTexture()
    {
        byte[] pixels = [0, 0, 0, 255];
        BitmapSource bitmap = BitmapSource.Create(
            1,
            1,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            4);
        return new NativeTextureSource(
            "test://silhouette",
            bitmap,
            sourcePremultipliedAlpha: false,
            pixels);
    }

    private static NativeSpineDrawBatch CreateQuadBatch(
        NativeTextureSource texture,
        params (float Left, float Top, float Right, float Bottom)[]
            quads)
    {
        int quadCount = quads.Length;
        float[] positions = new float[quadCount * 8];
        float[] uvs = new float[quadCount * 8];
        int[] indices = new int[quadCount * 6];
        for (int index = 0; index < quadCount; index++)
        {
            (float left, float top, float right, float bottom) =
                quads[index];
            int vertexOffset = index * 8;
            positions[vertexOffset] = left;
            positions[vertexOffset + 1] = top;
            positions[vertexOffset + 2] = right;
            positions[vertexOffset + 3] = top;
            positions[vertexOffset + 4] = right;
            positions[vertexOffset + 5] = bottom;
            positions[vertexOffset + 6] = left;
            positions[vertexOffset + 7] = bottom;
            uvs[vertexOffset] = 0;
            uvs[vertexOffset + 1] = 0;
            uvs[vertexOffset + 2] = 1;
            uvs[vertexOffset + 3] = 0;
            uvs[vertexOffset + 4] = 1;
            uvs[vertexOffset + 5] = 1;
            uvs[vertexOffset + 6] = 0;
            uvs[vertexOffset + 7] = 1;
            int indexOffset = index * 6;
            int vertexBase = index * 4;
            indices[indexOffset] = vertexBase;
            indices[indexOffset + 1] = vertexBase + 1;
            indices[indexOffset + 2] = vertexBase + 2;
            indices[indexOffset + 3] = vertexBase + 2;
            indices[indexOffset + 4] = vertexBase + 3;
            indices[indexOffset + 5] = vertexBase;
        }

        NativeSpineDrawBatch batch = new();
        batch.Reset(texture, BlendMode.Normal);
        int vertexOffsetAfterAppend = batch.AppendVertices(
            positions,
            uvs,
            positions.Length,
            new Vector4(1, 1, 1, 1),
            Vector4.Zero);
        batch.AppendIndices(
            indices,
            indices.Length,
            vertexOffsetAfterAppend);
        return batch;
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
