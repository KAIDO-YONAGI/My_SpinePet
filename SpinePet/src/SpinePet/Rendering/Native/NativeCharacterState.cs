using System.Drawing;
using SpinePet.Models;

namespace SpinePet.Rendering.Native;

internal sealed class NativeCharacterState : IDisposable
{
    public required CharacterConfig Config { get; set; }
    public NativeSpineResource? Resource { get; set; }
    public NativeCompositionSurface? Surface { get; set; }
    public NativeSpineGeometry Geometry { get; } = new();
    public NativeSpineBounds SetupBounds { get; set; } = NativeSpineBounds.Empty;
    public NativeSpineBounds Envelope { get; set; } = NativeSpineBounds.Empty;
    public RectangleF ScreenBounds { get; set; } = RectangleF.Empty;
    public RectangleF PreviousWindowRegionBounds { get; set; } =
        RectangleF.Empty;
    public RectangleF WindowRegionBounds { get; set; } =
        RectangleF.Empty;
    public IReadOnlyList<NativeSpineDrawBatch> LastBatches { get; set; } =
        Array.Empty<NativeSpineDrawBatch>();
    public bool IsVisible { get; set; }
    public bool IsLoading { get; set; }
    public double CurrentScale { get; set; } = 0.2;
    public double MaxScale { get; set; } = 2;
    public int LoadVersion { get; set; }

    public float PivotX =>
        (SetupBounds.Left + SetupBounds.Right) * 0.5f;
    public float PivotY => SetupBounds.Bottom;

    public void Dispose()
    {
        Surface?.Dispose();
        Resource?.Dispose();
        Surface = null;
        Resource = null;
    }
}
