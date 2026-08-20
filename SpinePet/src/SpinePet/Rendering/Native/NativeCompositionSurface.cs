using System.Numerics;
using Vortice.Direct3D11;
using Vortice.DirectComposition;
using Vortice.DXGI;

namespace SpinePet.Rendering.Native;

internal sealed class NativeCompositionSurface : IDisposable
{
    private const int MaximumSurfaceDimension = 16384;

    private readonly NativeGraphicsDevice _graphics;
    private readonly IDCompositionVisual _visual;
    private IDXGISwapChain1? _swapChain;
    private ID3D11Texture2D? _backBuffer;
    private bool _attached;
    private bool _disposed;

    public NativeCompositionSurface(
        NativeGraphicsDevice graphics,
        IDCompositionVisual visual)
    {
        _graphics = graphics;
        _visual = visual;
    }

    public ID3D11RenderTargetView? RenderTarget { get; private set; }
    public int PixelWidth { get; private set; }
    public int PixelHeight { get; private set; }
    public float CapacityScale { get; private set; }
    public float AnchorPixelX { get; private set; }
    public float AnchorPixelY { get; private set; }

    public bool CanContainScale(float pixelScale) =>
        _swapChain != null && pixelScale <= CapacityScale;

    public void EnsureSize(
        NativeSpineBounds envelope,
        float pivotX,
        float pivotY,
        float pixelScale)
    {
        if (CanContainScale(pixelScale))
            return;

        RecreateForScale(envelope, pivotX, pivotY, pixelScale);
    }

    public void ShrinkToScale(
        NativeSpineBounds envelope,
        float pivotX,
        float pivotY,
        float pixelScale)
    {
        if (_swapChain == null ||
            CapacityScale <= pixelScale * 1.5f)
        {
            return;
        }

        RecreateForScale(envelope, pivotX, pivotY, pixelScale);
    }

    private void RecreateForScale(
        NativeSpineBounds envelope,
        float pivotX,
        float pivotY,
        float pixelScale)
    {
        float capacityScale = Math.Max(pixelScale, pixelScale * 1.2f);
        float left = Math.Max(1, (pivotX - envelope.Left) * capacityScale);
        float right = Math.Max(1, (envelope.Right - pivotX) * capacityScale);
        float top = Math.Max(1, (pivotY - envelope.Top) * capacityScale);
        float bottom = Math.Max(1, (envelope.Bottom - pivotY) * capacityScale);
        int width = Math.Clamp(
            (int)Math.Ceiling(left + right),
            2,
            MaximumSurfaceDimension);
        int height = Math.Clamp(
            (int)Math.Ceiling(top + bottom),
            2,
            MaximumSurfaceDimension);
        Recreate(
            width,
            height,
            capacityScale,
            Math.Min(left, width - 1),
            Math.Min(top, height - 1));
    }

    public Vector4 GetTransform(float pixelScale)
    {
        float xScale = 2 * pixelScale / PixelWidth;
        float yScale = -2 * pixelScale / PixelHeight;
        float xOffset = 2 * AnchorPixelX / PixelWidth - 1;
        float yOffset = 1 - 2 * AnchorPixelY / PixelHeight;
        return new Vector4(xScale, yScale, xOffset, yOffset);
    }

    public void SetAnchorPosition(float x, float y)
    {
        _visual.SetOffsetX(x - AnchorPixelX).CheckError();
        _visual.SetOffsetY(y - AnchorPixelY).CheckError();
    }

    public bool SetVisible(bool visible)
    {
        if (visible == _attached)
            return false;

        if (visible)
            _graphics.AddVisual(_visual);
        else
            _graphics.RemoveVisual(_visual);
        _attached = visible;
        return true;
    }

    public void Present()
    {
        _swapChain?.Present(0, PresentFlags.None).CheckError();
    }

    private void Recreate(
        int width,
        int height,
        float capacityScale,
        float anchorPixelX,
        float anchorPixelY)
    {
        IDXGISwapChain1 nextSwapChain =
            _graphics.CreateSwapChain(width, height);
        ID3D11RenderTargetView nextRenderTarget =
            _graphics.CreateRenderTarget(
                nextSwapChain,
                out ID3D11Texture2D nextBackBuffer);

        ID3D11RenderTargetView? previousRenderTarget = RenderTarget;
        ID3D11Texture2D? previousBackBuffer = _backBuffer;
        IDXGISwapChain1? previousSwapChain = _swapChain;

        PixelWidth = width;
        PixelHeight = height;
        CapacityScale = capacityScale;
        AnchorPixelX = anchorPixelX;
        AnchorPixelY = anchorPixelY;
        _swapChain = nextSwapChain;
        _backBuffer = nextBackBuffer;
        RenderTarget = nextRenderTarget;
        _visual.SetContent(nextSwapChain).CheckError();

        previousRenderTarget?.Dispose();
        previousBackBuffer?.Dispose();
        previousSwapChain?.Dispose();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        if (_attached)
        {
            _graphics.RemoveVisual(_visual);
            _attached = false;
        }

        _visual.SetContent(null).CheckError();
        RenderTarget?.Dispose();
        _backBuffer?.Dispose();
        _swapChain?.Dispose();
        _visual.Dispose();
    }
}
