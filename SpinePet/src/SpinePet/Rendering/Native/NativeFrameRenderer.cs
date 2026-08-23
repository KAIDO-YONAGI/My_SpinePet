using System.Diagnostics;
using System.Drawing;
using SpinePet.Infrastructure;

namespace SpinePet.Rendering.Native;

internal sealed class NativeFrameRenderer
{
    private const double PerformanceWindowSeconds = 2;

    private readonly NativeCharacterScene _scene;
    private readonly NativeRenderSession _session;
    private readonly NativeInputRegionCoordinator _inputRegions;
    private readonly List<PendingFrame> _pendingFrames = [];
    private readonly bool _performanceTelemetryEnabled = string.Equals(
        Environment.GetEnvironmentVariable("SPINEPET_PERF_LOG"),
        "1",
        StringComparison.Ordinal);
    private bool _renderingFrame;
    private bool _compositionDirty;
    private long _performanceWindowStartTimestamp;
    private long _performanceFrameTicks;
    private long _performanceMaximumFrameTicks;
    private long _performanceAllocatedBytes;
    private int _performanceFrameCount;

    public NativeFrameRenderer(
        NativeCharacterScene scene,
        NativeRenderSession session,
        NativeInputRegionCoordinator inputRegions)
    {
        _scene = scene;
        _session = session;
        _inputRegions = inputRegions;
    }

    public void MarkCompositionDirty() => _compositionDirty = true;

    public void UpdateSurfacePosition(NativeCharacterState state)
    {
        NativeCompositionWindow? window = _session.Window;
        if (window == null || state.Surface == null)
            return;

        state.Surface.SetAnchorPosition(
            NativeInputRegionCoordinator.ToClientPixelX(
                window,
                state.Config.PositionX),
            NativeInputRegionCoordinator.ToClientPixelY(
                window,
                state.Config.PositionY));
    }

    public void ShrinkSurface(NativeCharacterState state)
    {
        NativeCompositionWindow? window = _session.Window;
        if (window == null || state.Surface == null)
            return;

        state.Surface.ShrinkToScale(
            state.Envelope,
            state.PivotX,
            state.PivotY,
            (float)state.CurrentScale * window.DpiScale);
    }

    public void RenderFrame(
        double elapsedSeconds,
        bool pointerDragging,
        Action flushPendingPointerMove,
        int targetFrameRate)
    {
        NativeGraphicsDevice? graphics = _session.Graphics;
        NativeCompositionWindow? window = _session.Window;
        if (_renderingFrame || graphics == null || window == null)
            return;

        long frameStartedTimestamp = _performanceTelemetryEnabled
            ? Stopwatch.GetTimestamp()
            : 0;
        long frameStartedAllocatedBytes = _performanceTelemetryEnabled
            ? GC.GetAllocatedBytesForCurrentThread()
            : 0;
        _renderingFrame = true;
        try
        {
            flushPendingPointerMove();
            _pendingFrames.Clear();
            foreach (NativeCharacterState state in _scene.States)
            {
                if (!state.IsVisible ||
                    state.Resource == null ||
                    state.Surface == null)
                {
                    continue;
                }

                float animationSpeed =
                    (float)state.Config.AnimationSpeed;
                if (state.Resource.AnimationState.TimeScale !=
                    animationSpeed)
                {
                    state.Resource.AnimationState.TimeScale =
                        animationSpeed;
                }

                state.Resource.Update((float)elapsedSeconds);
                NativeFrameRenderPlan plan =
                    state.Geometry.BuildFramePlan(
                        state.Resource.Skeleton);
                float pixelScale =
                    (float)state.CurrentScale * window.DpiScale;
                state.Surface.EnsureSize(
                    state.Envelope,
                    state.PivotX,
                    state.PivotY,
                    pixelScale);
                UpdateSurfacePosition(state);
                UpdateScreenBounds(
                    state,
                    window,
                    state.Geometry.Bounds,
                    pixelScale);
                state.LastBatches = plan.Batches;
                _pendingFrames.Add(new PendingFrame(
                    state,
                    plan,
                    pixelScale));
            }

            _inputRegions.Update(
                _session,
                _scene.States,
                pointerDragging);
            foreach (PendingFrame frame in _pendingFrames)
            {
                graphics.Render(
                    frame.State.Surface!,
                    frame.Plan,
                    frame.State.Surface!.GetTransform(frame.PixelScale));
            }

            bool rendered = _pendingFrames.Count > 0;
            if (rendered || _compositionDirty)
            {
                graphics.Commit();
                _compositionDirty = false;
            }
        }
        catch (Exception exception)
        {
            AppLogger.Write(
                nameof(NativeCharacterRenderHost),
                $"frame-failed message={exception.Message}");
        }
        finally
        {
            if (_performanceTelemetryEnabled)
            {
                RecordPerformanceFrame(
                    frameStartedTimestamp,
                    frameStartedAllocatedBytes,
                    _pendingFrames.Count,
                    targetFrameRate);
            }

            _pendingFrames.Clear();
            _renderingFrame = false;
        }
    }

    public void Commit()
    {
        _session.Commit();
        _compositionDirty = false;
    }

    public void PurgeUnusedTextures()
    {
        NativeGraphicsDevice? graphics = _session.Graphics;
        if (graphics == null)
            return;

        HashSet<string> activePaths =
            CollectRetainedTexturePaths(_scene.States);
        graphics.PurgeTextures(activePaths);
    }

    internal static HashSet<string> CollectRetainedTexturePaths(
        IEnumerable<NativeCharacterState> states)
    {
        HashSet<string> paths =
            new(StringComparer.OrdinalIgnoreCase);
        foreach (NativeCharacterState state in states)
        {
            if (state.Resource != null)
            {
                AddTexturePaths(paths, state.Resource);
            }

            foreach (NativeCharacterLoadResult slot in
                     state.ResourceSlots.Values)
            {
                AddTexturePaths(paths, slot.Resource);
            }
        }

        return paths;
    }

    private static void AddTexturePaths(
        HashSet<string> paths,
        NativeSpineResource resource)
    {
        foreach (NativeTextureSource texture in
                 resource.TextureLoader.Textures)
        {
            paths.Add(texture.Path);
        }
    }

    private static void UpdateScreenBounds(
        NativeCharacterState state,
        NativeCompositionWindow window,
        NativeSpineBounds bounds,
        float pixelScale)
    {
        float anchorX =
            window.Left +
            NativeInputRegionCoordinator.ToClientPixelX(
                window,
                state.Config.PositionX);
        float anchorY =
            window.Top +
            NativeInputRegionCoordinator.ToClientPixelY(
                window,
                state.Config.PositionY);
        state.PreviousRenderRegionBounds =
            state.RenderRegionBounds;
        if (bounds.IsEmpty)
        {
            state.ScreenBounds = RectangleF.Empty;
            state.RenderRegionBounds = RectangleF.Empty;
            return;
        }

        state.ScreenBounds = RectangleF.FromLTRB(
            anchorX + (bounds.Left - state.PivotX) * pixelScale,
            anchorY + (bounds.Top - state.PivotY) * pixelScale,
            anchorX + (bounds.Right - state.PivotX) * pixelScale,
            anchorY + (bounds.Bottom - state.PivotY) * pixelScale);
        state.RenderRegionBounds =
            NativeInputRegionCoordinator.GetSurfaceScreenBounds(
                state.Surface,
                anchorX,
                anchorY);
    }

    private void RecordPerformanceFrame(
        long frameStartedTimestamp,
        long frameStartedAllocatedBytes,
        int visibleCharacterCount,
        int targetFrameRate)
    {
        long completedTimestamp = Stopwatch.GetTimestamp();
        long frameTicks = completedTimestamp - frameStartedTimestamp;
        long allocatedBytes = Math.Max(
            0,
            GC.GetAllocatedBytesForCurrentThread() -
            frameStartedAllocatedBytes);
        if (_performanceWindowStartTimestamp == 0)
            _performanceWindowStartTimestamp = frameStartedTimestamp;

        _performanceFrameCount++;
        _performanceFrameTicks += frameTicks;
        _performanceMaximumFrameTicks = Math.Max(
            _performanceMaximumFrameTicks,
            frameTicks);
        _performanceAllocatedBytes += allocatedBytes;

        double windowSeconds =
            (completedTimestamp - _performanceWindowStartTimestamp) /
            (double)Stopwatch.Frequency;
        if (windowSeconds < PerformanceWindowSeconds)
            return;

        double actualFrameRate = _performanceFrameCount / windowSeconds;
        double averageFrameMilliseconds =
            _performanceFrameTicks * 1000.0 /
            Stopwatch.Frequency /
            _performanceFrameCount;
        double maximumFrameMilliseconds =
            _performanceMaximumFrameTicks * 1000.0 /
            Stopwatch.Frequency;
        long allocatedBytesPerFrame =
            _performanceAllocatedBytes / _performanceFrameCount;
        AppLogger.Write(
            nameof(NativeCharacterRenderHost),
            $"performance target-fps={targetFrameRate} " +
            $"actual-fps={actualFrameRate:F2} " +
            $"visible={visibleCharacterCount} " +
            $"avg-frame-ms={averageFrameMilliseconds:F3} " +
            $"max-frame-ms={maximumFrameMilliseconds:F3} " +
            $"allocated-bytes-per-frame={allocatedBytesPerFrame}");

        _performanceWindowStartTimestamp = completedTimestamp;
        _performanceFrameTicks = 0;
        _performanceMaximumFrameTicks = 0;
        _performanceAllocatedBytes = 0;
        _performanceFrameCount = 0;
    }

    private readonly record struct PendingFrame(
        NativeCharacterState State,
        NativeFrameRenderPlan Plan,
        float PixelScale);
}
