using System.Diagnostics;
using System.Drawing;
using System.Windows;

namespace SpinePet.Rendering.Native;

internal sealed class NativeInputRegionCoordinator
{
    private static readonly TimeSpan WorkingAreaRefreshInterval =
        TimeSpan.FromSeconds(1);

    private readonly List<Rectangle> _inputRegions = [];
    private readonly List<Rectangle> _workingAreas = [];
    private long _workingAreasRefreshTimestamp;

    public void Update(
        NativeRenderSession session,
        IEnumerable<NativeCharacterState> states,
        bool pointerDragging)
    {
        NativeCompositionWindow? window = session.Window;
        NativeInputWindow? inputWindow = session.InputWindow;
        if (window == null || inputWindow == null || pointerDragging)
            return;

        _inputRegions.Clear();
        RefreshWorkingAreas(inputWindow);
        long now = Stopwatch.GetTimestamp();
        foreach (NativeCharacterState state in states)
        {
            if (!state.IsVisible)
                continue;

            float anchorX =
                window.Left + ToClientPixelX(window, state.Config.PositionX);
            float anchorY =
                window.Top + ToClientPixelY(window, state.Config.PositionY);
            float pixelScale =
                (float)state.CurrentScale * window.DpiScale;
            if (NativeSilhouetteRasterizer.NeedsRefresh(
                    state,
                    anchorX,
                    anchorY,
                    pixelScale,
                    now))
            {
                NativeSilhouetteRasterizer.CopyToCache(
                    state,
                    NativeSilhouetteRasterizer.Rasterize(
                        state.LastBatches,
                        state.ScreenBounds,
                        anchorX,
                        anchorY,
                        state.PivotX,
                        state.PivotY,
                        pixelScale,
                        window.Left,
                        window.Top));
                state.HasCachedSilhouette = true;
                state.CachedAnchorX = anchorX;
                state.CachedAnchorY = anchorY;
                state.CachedPixelScale = pixelScale;
                state.CachedSilhouetteTimestamp = now;
            }

            foreach (Rectangle run in state.CachedSilhouetteRuns)
                ClipToWorkingAreas(_inputRegions, run, _workingAreas);
        }

        if (_inputRegions.Count == 0)
        {
            inputWindow.ClearAndHide();
            return;
        }

        inputWindow.SetInteractiveRegions(_inputRegions);
        inputWindow.Show();
    }

    public static void AlignCacheToCurrentPosition(
        NativeCompositionWindow? window,
        NativeCharacterState state)
    {
        if (window == null || !state.HasCachedSilhouette)
            return;

        float anchorX =
            window.Left + ToClientPixelX(window, state.Config.PositionX);
        float anchorY =
            window.Top + ToClientPixelY(window, state.Config.PositionY);
        int offsetX = (int)Math.Round(anchorX - state.CachedAnchorX);
        int offsetY = (int)Math.Round(anchorY - state.CachedAnchorY);
        if (offsetX != 0 || offsetY != 0)
        {
            for (int index = 0;
                 index < state.CachedSilhouetteRuns.Count;
                 index++)
            {
                Rectangle translated = state.CachedSilhouetteRuns[index];
                translated.Offset(offsetX, offsetY);
                state.CachedSilhouetteRuns[index] = translated;
            }
        }

        state.CachedAnchorX = anchorX;
        state.CachedAnchorY = anchorY;
        state.CachedSilhouetteTimestamp = Stopwatch.GetTimestamp();
    }

    internal static RectangleF GetSurfaceScreenBounds(
        NativeCompositionSurface? surface,
        float anchorX,
        float anchorY)
    {
        if (surface == null ||
            surface.PixelWidth <= 0 ||
            surface.PixelHeight <= 0)
        {
            return RectangleF.Empty;
        }

        return new RectangleF(
            anchorX - surface.AnchorPixelX,
            anchorY - surface.AnchorPixelY,
            surface.PixelWidth,
            surface.PixelHeight);
    }

    internal static void ClipToWorkingAreas(
        List<Rectangle> clipped,
        Rectangle characterRegion,
        IReadOnlyList<Rectangle> workingAreas)
    {
        foreach (Rectangle area in workingAreas)
        {
            Rectangle intersection =
                Rectangle.Intersect(characterRegion, area);
            if (intersection.Width > 0 && intersection.Height > 0)
                clipped.Add(intersection);
        }
    }

    internal static Rectangle ToClientPixelRectangle(
        Rectangle physicalScreenRectangle,
        int windowLeft,
        int windowTop)
    {
        return Rectangle.FromLTRB(
            physicalScreenRectangle.Left - windowLeft,
            physicalScreenRectangle.Top - windowTop,
            physicalScreenRectangle.Right - windowLeft,
            physicalScreenRectangle.Bottom - windowTop);
    }

    internal static float ToClientPixelX(
        NativeCompositionWindow window,
        double x) =>
        (float)((x - SystemParameters.VirtualScreenLeft) *
                window.DpiScale);

    internal static float ToClientPixelY(
        NativeCompositionWindow window,
        double y) =>
        (float)((y - SystemParameters.VirtualScreenTop) *
                window.DpiScale);

    private void RefreshWorkingAreas(NativeInputWindow inputWindow)
    {
        long now = Stopwatch.GetTimestamp();
        if (_workingAreas.Count > 0 &&
            Stopwatch.GetElapsedTime(
                _workingAreasRefreshTimestamp,
                now) < WorkingAreaRefreshInterval)
        {
            return;
        }

        _workingAreas.Clear();
        foreach (System.Windows.Forms.Screen screen in
                 System.Windows.Forms.Screen.AllScreens)
        {
            _workingAreas.Add(ToClientPixelRectangle(
                screen.WorkingArea,
                inputWindow.Left,
                inputWindow.Top));
        }

        _workingAreasRefreshTimestamp = now;
    }
}
