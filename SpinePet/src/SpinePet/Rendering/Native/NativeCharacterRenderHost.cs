using System.Diagnostics;
using System.Drawing;
using System.Windows;
using System.Windows.Threading;
using Spine;
using SpinePet.Infrastructure;
using SpinePet.Models;
using SpinePet.Rendering;

namespace SpinePet.Rendering.Native;

public sealed class NativeCharacterRenderHost :
    ICharacterRenderHost,
    IDisposable
{
    internal const int SilhouetteCellSize = 8;
    internal const int SilhouetteFallbackMargin = 12;
    internal const int MaxSilhouetteRunsPerCharacter = 400;
    private const int MaxSilhouetteCells = 16384;
    private const int MaxSilhouetteCellSize = 128;
    private const double DefaultScale = 0.2;
    private const double MaximumScale = 2.0;
    private const double MinimumScale = 0.05;
    private const double CharacterBottomMargin = 24;
    private const int WmMouseMove = 0x0200;
    private const int WmLeftButtonDown = 0x0201;
    private const int WmLeftButtonUp = 0x0202;
    private const int WmCaptureChanged = 0x0215;
    private const double PerformanceWindowSeconds = 2;
    private static readonly TimeSpan WorkingAreaRefreshInterval =
        TimeSpan.FromSeconds(1);

    private readonly Dictionary<string, NativeCharacterState> _states =
        new(StringComparer.Ordinal);
    private readonly List<PendingFrame> _pendingFrames = [];
    private readonly List<Rectangle> _inputRegions = [];
    private readonly List<Rectangle> _workingAreas = [];
    private readonly NativeCharacterZOrder _zOrder = new();
    private readonly Dispatcher _dispatcher;
    private readonly NativeFrameScheduler _frameScheduler;
    private readonly DispatcherTimer _scaleSettleTimer;
    private readonly Stopwatch _frameClock = new();
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly bool _performanceTelemetryEnabled = string.Equals(
        Environment.GetEnvironmentVariable("SPINEPET_PERF_LOG"),
        "1",
        StringComparison.Ordinal);
    private readonly HashSet<string> _scaleShrinkPending =
        new(StringComparer.Ordinal);
    private NativeCompositionWindow? _window;
    private NativeInputWindow? _inputWindow;
    private NativeGraphicsDevice? _graphics;
    private Task? _initializationTask;
    private bool _configMode;
    private bool _renderDragEnabled = true;
    private bool _renderingFrame;
    private bool _compositionDirty;
    private bool _closed;
    private string? _pointerCharacterId;
    private NativePoint _pointerStart;
    private NativePoint _pointerLatest;
    private double _pointerStartX;
    private double _pointerStartY;
    private bool _pointerDragging;
    private bool _pointerMovePending;
    private int _explicitMoveDepth;
    private int _targetFrameRate = GlobalConfig.DefaultTargetFrameRate;
    private long _workingAreasRefreshTimestamp;
    private long _performanceWindowStartTimestamp;
    private long _performanceFrameTicks;
    private long _performanceMaximumFrameTicks;
    private long _performanceAllocatedBytes;
    private int _performanceFrameCount;

    public NativeCharacterRenderHost()
    {
        _dispatcher = System.Windows.Application.Current?.Dispatcher ??
            Dispatcher.CurrentDispatcher;
        _frameScheduler = new NativeFrameScheduler(
            _dispatcher,
            OnFrame,
            TimeSpan.FromSeconds(
                1.0 / GlobalConfig.DefaultTargetFrameRate));
        _scaleSettleTimer = new DispatcherTimer(
            DispatcherPriority.Background,
            _dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _scaleSettleTimer.Tick += OnScaleSettle;
    }

    public event Action<string, double, double>? CharacterScaleChanged;
    public event Action<string, IReadOnlyList<string>>?
        CharacterAnimationsLoaded;
    public event Action<string>? CharacterLoadFailed;
    public event Action? CharactersStateChanged;
    public event Action<string, double, double>? CharacterPositionCommitted;

    public bool IsCharacterLoading(string characterId) =>
        _states.TryGetValue(characterId, out NativeCharacterState? state) &&
        state.IsLoading;

    public bool IsCharacterVisible(string characterId) =>
        _states.TryGetValue(characterId, out NativeCharacterState? state) &&
        state.IsVisible;

    public IReadOnlyList<string> GetAnimationNames(string characterId) =>
        _states.TryGetValue(characterId, out NativeCharacterState? state) &&
        state.Resource != null
            ? state.Resource.AnimationNames
            : Array.Empty<string>();

    public double GetMaxScale(string characterId) =>
        _states.TryGetValue(characterId, out NativeCharacterState? state)
            ? state.MaxScale
            : MaximumScale;

    public double GetCurrentScale(string characterId) =>
        _states.TryGetValue(characterId, out NativeCharacterState? state)
            ? state.CurrentScale
            : DefaultScale;

    internal bool IsFrameLoopRunning => _frameScheduler.IsRunning;

    internal int TargetFrameRate => _targetFrameRate;

    internal TimeSpan FrameInterval => _frameScheduler.Interval;

    public Task InitializeAsync()
    {
        _initializationTask ??= _dispatcher.CheckAccess()
            ? InitializeOnDispatcher()
            : _dispatcher.InvokeAsync(InitializeOnDispatcher).Task.Unwrap();
        return _initializationTask;
    }

    public async Task ShowCharacterAsync(
        CharacterConfig character,
        bool configMode,
        double speed)
    {
        await InitializeAsync();
        _configMode = configMode;

        NativeCharacterState state = GetOrCreateState(character);
        state.Config = character;
        state.CurrentScale = Math.Clamp(
            character.Scale > 0 ? character.Scale : DefaultScale,
            MinimumScale,
            state.MaxScale);
        state.Config.Scale = state.CurrentScale;
        state.Config.AnimationSpeed = Math.Clamp(speed, 0.1, 2);
        state.IsVisible = true;
        state.Config.Visible = true;

        if (state.Resource != null)
        {
            if (state.Surface?.SetVisible(true) == true)
                _zOrder.MoveToTop(state.Config.Id);
            SelectModeAnimation(state);
            UpdateSurfacePosition(state);
            _compositionDirty = true;
            RenderFrame(0);
            UpdateFrameTimerState();
            CharactersStateChanged?.Invoke();
            return;
        }

        state.IsLoading = true;
        int loadVersion = ++state.LoadVersion;
        CharactersStateChanged?.Invoke();

        try
        {
            var loaded = await Task.Run(() =>
            {
                _lifetimeCancellation.Token.ThrowIfCancellationRequested();
                NativeSpineResource resource =
                    NativeSpineResource.Load(character);
                try
                {
                    _lifetimeCancellation.Token.ThrowIfCancellationRequested();
                    var bounds =
                        NativeSpineEnvelopeCalculator.Calculate(
                            resource.SkeletonData);
                    _lifetimeCancellation.Token.ThrowIfCancellationRequested();
                    return (resource, bounds.Setup, bounds.Envelope);
                }
                catch
                {
                    resource.Dispose();
                    throw;
                }
            }, _lifetimeCancellation.Token);

            await _dispatcher.InvokeAsync(() =>
            {
                if (!_states.TryGetValue(
                        character.Id,
                        out NativeCharacterState? current) ||
                    !ReferenceEquals(current, state) ||
                    current.LoadVersion != loadVersion ||
                    _closed)
                {
                    loaded.resource.Dispose();
                    return;
                }

                current.Resource = loaded.resource;
                current.SetupBounds = loaded.Setup;
                current.Envelope = loaded.Envelope;
                current.Surface = _graphics!.CreateSurface();
                if (current.Surface.SetVisible(current.IsVisible) &&
                    current.IsVisible)
                {
                    _zOrder.MoveToTop(current.Config.Id);
                }
                current.IsLoading = false;
                SelectModeAnimation(current);
                UpdateSurfacePosition(current);
                _compositionDirty = true;
                RenderFrame(0);
                UpdateFrameTimerState();

                if (string.IsNullOrWhiteSpace(
                        current.Config.ConfiguredAnimation) &&
                    current.Resource.AnimationNames.Count > 0)
                {
                    current.Config.ConfiguredAnimation =
                        SelectIdleAnimationName(
                            current.Resource.AnimationNames) ??
                        current.Resource.AnimationNames[0];
                }

                CharacterAnimationsLoaded?.Invoke(
                    character.Id,
                    current.Resource.AnimationNames);
                CharacterScaleChanged?.Invoke(
                    character.Id,
                    current.MaxScale,
                    current.CurrentScale);
                CharactersStateChanged?.Invoke();
            });
        }
        catch (OperationCanceledException)
            when (_lifetimeCancellation.IsCancellationRequested)
        {
            // Closing the render host intentionally abandons in-flight loads.
        }
        catch (Exception exception)
        {
            await _dispatcher.InvokeAsync(() =>
            {
                if (!_states.TryGetValue(
                        character.Id,
                        out NativeCharacterState? current) ||
                    !ReferenceEquals(current, state) ||
                    current.LoadVersion != loadVersion)
                {
                    return;
                }

                current.IsLoading = false;
                current.IsVisible = false;
                current.Config.Visible = false;
                UpdateFrameTimerState();
                AppLogger.Write(
                    nameof(NativeCharacterRenderHost),
                    $"character-load-failed id={character.Id} message={exception.Message}");
                CharacterLoadFailed?.Invoke(character.Id);
                CharactersStateChanged?.Invoke();
            });
            throw;
        }
    }

    public void HideCharacter(string characterId)
    {
        if (!_states.TryGetValue(
                characterId,
                out NativeCharacterState? state))
        {
            return;
        }

        CancelPointerIfCharacter(characterId);
        state.IsVisible = false;
        state.IsLoading = false;
        state.Config.Visible = false;
        state.Surface?.SetVisible(false);
        UpdateFrameTimerState();
        CommitComposition();
        CharactersStateChanged?.Invoke();
    }

    public void RemoveCharacter(string characterId)
    {
        if (!_states.Remove(
                characterId,
                out NativeCharacterState? state))
        {
            return;
        }

        CancelPointerIfCharacter(characterId);
        state.LoadVersion++;
        _scaleShrinkPending.Remove(characterId);
        _zOrder.Remove(characterId);
        state.Dispose();
        UpdateFrameTimerState();
        PurgeUnusedTextures();
        CommitComposition();
        CharactersStateChanged?.Invoke();
    }

    public void SetCharacterScale(string characterId, double scale)
    {
        if (!_states.TryGetValue(
                characterId,
                out NativeCharacterState? state))
        {
            return;
        }

        state.CurrentScale = Math.Clamp(
            scale,
            MinimumScale,
            state.MaxScale);
        state.Config.Scale = state.CurrentScale;
        _scaleShrinkPending.Add(characterId);
        _scaleSettleTimer.Stop();
        _scaleSettleTimer.Start();
        CharacterScaleChanged?.Invoke(
            characterId,
            state.MaxScale,
            state.CurrentScale);
    }

    public void SetCharacterSpeed(string characterId, double speed)
    {
        if (!_states.TryGetValue(
                characterId,
                out NativeCharacterState? state))
        {
            return;
        }

        state.Config.AnimationSpeed = Math.Clamp(speed, 0.1, 2);
        if (state.Resource != null)
            state.Resource.AnimationState.TimeScale =
                (float)state.Config.AnimationSpeed;
    }

    public void PlayCharacterAnimation(
        string characterId,
        string animation,
        bool repeat)
    {
        if (!_states.TryGetValue(
                characterId,
                out NativeCharacterState? state) ||
            state.Resource == null ||
            state.Resource.SkeletonData.FindAnimation(animation) == null)
        {
            return;
        }

        state.Config.ConfiguredAnimation = animation;
        state.Resource.SetAnimation(animation, repeat);
    }

    public void SetConfigMode(bool configMode)
    {
        _configMode = configMode;
        CancelPointerInteraction(commitPosition: false);
        UpdateWindowRegions();
        foreach (NativeCharacterState state in _states.Values)
            SelectModeAnimation(state);
    }

    public void SetRenderDragEnabled(bool enabled)
    {
        _renderDragEnabled = enabled;
        if (!enabled && _pointerDragging)
            CancelPointerInteraction(commitPosition: true);
    }

    public void SetTargetFrameRate(int frameRate)
    {
        int normalized = GlobalConfig.NormalizeTargetFrameRate(frameRate);
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.Invoke(() => SetTargetFrameRate(normalized));
            return;
        }

        _targetFrameRate = normalized;
        _frameScheduler.SetInterval(
            TimeSpan.FromSeconds(1.0 / normalized));
    }

    public void MoveCharacter(
        string characterId,
        double left,
        double top)
    {
        if (!_states.TryGetValue(
                characterId,
                out NativeCharacterState? state))
        {
            return;
        }

        state.Config.PositionX = left;
        state.Config.PositionY = top;
        UpdateSurfacePosition(state);
        _compositionDirty = true;
    }

    public void BeginCharacterMove()
    {
        _explicitMoveDepth++;
    }

    public void EndCharacterMove()
    {
        if (_explicitMoveDepth > 0)
            _explicitMoveDepth--;
        CommitComposition();
    }

    public void ResetCharacterPosition(string characterId)
    {
        if (!_states.TryGetValue(
                characterId,
                out NativeCharacterState? state))
        {
            return;
        }

        Rect area = SystemParameters.WorkArea;
        MoveCharacter(
            characterId,
            area.Left + area.Width / 2,
            area.Bottom - CharacterBottomMargin);
        CommitComposition();
        CharacterPositionCommitted?.Invoke(
            characterId,
            state.Config.PositionX,
            state.Config.PositionY);
    }

    public void HideAll()
    {
        CancelPointerInteraction(commitPosition: false);
        foreach (NativeCharacterState state in _states.Values)
        {
            state.IsVisible = false;
            state.IsLoading = false;
            state.Config.Visible = false;
            state.Surface?.SetVisible(false);
        }

        UpdateFrameTimerState();
        CommitComposition();
        CharactersStateChanged?.Invoke();
    }

    public async Task RestoreVisibleCharactersAsync(
        IEnumerable<CharacterConfig> characters,
        bool configMode)
    {
        foreach (CharacterConfig character in characters.Where(
                     character => character.Visible))
        {
            try
            {
                await ShowCharacterAsync(
                    character,
                    configMode,
                    character.AnimationSpeed);
            }
            catch (Exception exception)
            {
                AppLogger.Write(
                    nameof(NativeCharacterRenderHost),
                    $"character-restore-skipped id={character.Id} " +
                    $"error={exception.GetType().Name} " +
                    $"message={exception.Message}");
            }
        }
    }

    public void Close()
    {
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.Invoke(Close);
            return;
        }

        if (_closed)
            return;
        _closed = true;
        _lifetimeCancellation.Cancel();

        _frameScheduler.Dispose();
        _scaleSettleTimer.Stop();
        foreach (NativeCharacterState state in _states.Values)
            state.Dispose();
        _states.Clear();
        _zOrder.Clear();
        _graphics?.Dispose();
        _graphics = null;
        _inputWindow?.Dispose();
        _inputWindow = null;
        _window?.Dispose();
        _window = null;
        _lifetimeCancellation.Dispose();
    }

    public void Dispose()
    {
        Close();
    }

    private Task InitializeOnDispatcher()
    {
        ObjectDisposedException.ThrowIf(_closed, this);
        if (_graphics != null)
            return Task.CompletedTask;

        _window = new NativeCompositionWindow();
        _graphics = new NativeGraphicsDevice(_window.Handle);
        _inputWindow = new NativeInputWindow(
            _window.Left,
            _window.Top,
            _window.Width,
            _window.Height);
        _inputWindow.MouseInput = OnNativeMouseInput;
        _inputWindow.ClearAndHide();
        AppLogger.Write(
            nameof(NativeCharacterRenderHost),
            $"initialized render-window={_window.Handle} " +
            $"input-window={_inputWindow.Handle} " +
            $"size={_window.Width}x{_window.Height} " +
            $"dpi={_window.DpiScale:F2}");
        return Task.CompletedTask;
    }

    private void UpdateFrameTimerState()
    {
        bool shouldRun = !_closed && _states.Values.Any(state =>
            state.IsVisible &&
            state.Resource != null &&
            state.Surface != null);
        if (shouldRun == _frameScheduler.IsRunning)
        {
            return;
        }

        if (shouldRun)
        {
            _frameClock.Restart();
            _frameScheduler.Start();
            return;
        }

        _frameScheduler.Stop();
        _frameClock.Reset();
    }

    private NativeCharacterState GetOrCreateState(CharacterConfig character)
    {
        if (_states.TryGetValue(
                character.Id,
                out NativeCharacterState? existing))
        {
            return existing;
        }

        NativeCharacterState state = new()
        {
            Config = character,
            CurrentScale = Math.Clamp(
                character.Scale > 0
                    ? character.Scale
                    : DefaultScale,
                MinimumScale,
                MaximumScale),
            MaxScale = MaximumScale
        };
        _states[character.Id] = state;
        _zOrder.MoveToTop(character.Id);
        return state;
    }

    private void OnFrame()
    {
        double elapsed = _frameClock.Elapsed.TotalSeconds;
        _frameClock.Restart();
        RenderFrame(Math.Min(elapsed, 0.1));
    }

    private void OnScaleSettle(object? sender, EventArgs eventArgs)
    {
        _scaleSettleTimer.Stop();
        if (_window == null)
            return;

        foreach (string characterId in _scaleShrinkPending)
        {
            if (!_states.TryGetValue(
                    characterId,
                    out NativeCharacterState? state) ||
                !state.IsVisible ||
                state.Surface == null)
            {
                continue;
            }

            state.Surface.ShrinkToScale(
                state.Envelope,
                state.PivotX,
                state.PivotY,
                (float)state.CurrentScale * _window.DpiScale);
        }

        _scaleShrinkPending.Clear();
        RenderFrame(0);
    }

    private void RenderFrame(double elapsedSeconds)
    {
        if (_renderingFrame || _graphics == null || _window == null)
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
            FlushPendingPointerMove();
            _pendingFrames.Clear();
            foreach (NativeCharacterState state in _states.Values)
            {
                if (!state.IsVisible ||
                    state.Resource == null ||
                    state.Surface == null)
                {
                    continue;
                }

                state.Resource.AnimationState.TimeScale =
                    (float)state.Config.AnimationSpeed;
                state.Resource.Update((float)elapsedSeconds);
                IReadOnlyList<NativeSpineDrawBatch> batches =
                    state.Geometry.Build(state.Resource.Skeleton);
                float pixelScale =
                    (float)state.CurrentScale * _window.DpiScale;
                state.Surface.EnsureSize(
                    state.Envelope,
                    state.PivotX,
                    state.PivotY,
                    pixelScale);
                UpdateSurfacePosition(state);
                UpdateScreenBounds(state, batches, pixelScale);
                state.LastBatches = batches;
                _pendingFrames.Add(new PendingFrame(
                    state,
                    batches,
                    pixelScale));
            }

            UpdateWindowRegions();
            foreach (PendingFrame frame in _pendingFrames)
            {
                _graphics.Render(
                    frame.State.Surface!,
                    frame.Batches,
                    frame.State.Surface!.GetTransform(frame.PixelScale));
            }

            bool rendered = _pendingFrames.Count > 0;
            if (rendered || _compositionDirty)
            {
                _graphics.Commit();
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
                    _pendingFrames.Count);
            }

            _pendingFrames.Clear();
            _renderingFrame = false;
        }
    }

    private void RecordPerformanceFrame(
        long frameStartedTimestamp,
        long frameStartedAllocatedBytes,
        int visibleCharacterCount)
    {
        long completedTimestamp = Stopwatch.GetTimestamp();
        long frameTicks = completedTimestamp - frameStartedTimestamp;
        long allocatedBytes = Math.Max(
            0,
            GC.GetAllocatedBytesForCurrentThread() -
            frameStartedAllocatedBytes);
        if (_performanceWindowStartTimestamp == 0)
        {
            _performanceWindowStartTimestamp = frameStartedTimestamp;
        }

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
        {
            return;
        }

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
            $"performance target-fps={_targetFrameRate} " +
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

    private void UpdateSurfacePosition(NativeCharacterState state)
    {
        if (_window == null || state.Surface == null)
            return;

        state.Surface.SetAnchorPosition(
            ToClientPixelX(state.Config.PositionX),
            ToClientPixelY(state.Config.PositionY));
    }

    private void UpdateScreenBounds(
        NativeCharacterState state,
        IReadOnlyList<NativeSpineDrawBatch> batches,
        float pixelScale)
    {
        if (_window == null)
            return;

        NativeSpineBounds bounds =
            NativeSpineEnvelopeCalculator.GetBounds(batches);
        float anchorX =
            _window.Left + ToClientPixelX(state.Config.PositionX);
        float anchorY =
            _window.Top + ToClientPixelY(state.Config.PositionY);
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
        state.RenderRegionBounds = GetSurfaceScreenBounds(
            state.Surface,
            anchorX,
            anchorY);
    }

    private float ToClientPixelX(double x) =>
        _window == null
            ? 0
            : (float)((x - SystemParameters.VirtualScreenLeft) *
                      _window.DpiScale);

    private float ToClientPixelY(double y) =>
        _window == null
            ? 0
            : (float)((y - SystemParameters.VirtualScreenTop) *
                      _window.DpiScale);

    private void CommitComposition()
    {
        if (_graphics == null)
            return;
        _graphics.Commit();
        _compositionDirty = false;
    }

    private void SelectModeAnimation(NativeCharacterState state)
    {
        NativeSpineResource? resource = state.Resource;
        if (resource == null)
            return;

        if (_configMode)
        {
            resource.SetAnimation(
                state.Config.ConfiguredAnimation,
                true);
            return;
        }

        string? configured = state.Config.ConfiguredAnimation;
        string? animation =
            !string.IsNullOrEmpty(configured) &&
            resource.SkeletonData.FindAnimation(configured) != null
                ? configured
                : SelectIdleAnimationName(resource.AnimationNames);
        resource.SetAnimation(animation, true);
    }

    private static void PlayClickAnimation(NativeCharacterState state)
    {
        NativeSpineResource? resource = state.Resource;
        if (resource == null)
            return;

        string? clickAnimation =
            SelectClickAnimationName(resource.AnimationNames);
        if (clickAnimation == null)
            return;

        resource.AnimationState.SetAnimation(0, clickAnimation, false);
        string? idleAnimation =
            SelectIdleAnimationName(resource.AnimationNames);
        if (idleAnimation != null &&
            !idleAnimation.Equals(
                clickAnimation,
                StringComparison.OrdinalIgnoreCase))
        {
            resource.AnimationState.AddAnimation(
                0,
                idleAnimation,
                true,
                0);
        }
    }

    internal static string? SelectClickAnimationName(
        IReadOnlyList<string> animationNames)
    {
        string[] preferredNames =
        [
            "action",
            "click",
            "touch",
            "tap",
            "reaction",
            "interact",
            "skillcut"
        ];
        foreach (string preferredName in preferredNames)
        {
            string? exact = animationNames.FirstOrDefault(name =>
                name.Equals(
                    preferredName,
                    StringComparison.OrdinalIgnoreCase));
            if (exact != null)
                return exact;

            string? prefixed = animationNames.FirstOrDefault(name =>
                name.StartsWith(
                    $"{preferredName}_",
                    StringComparison.OrdinalIgnoreCase));
            if (prefixed != null)
                return prefixed;
        }

        return null;
    }

    internal static string? SelectIdleAnimationName(
        IReadOnlyList<string> animationNames)
    {
        string? idle = animationNames.FirstOrDefault(name =>
            name.Equals("idle", StringComparison.OrdinalIgnoreCase));
        return idle ??
               animationNames.FirstOrDefault(name =>
                   name.StartsWith(
                       "idle",
                       StringComparison.OrdinalIgnoreCase)) ??
               (animationNames.Count > 0 ? animationNames[0] : null);
    }

    private static void PlayDragAnimation(NativeCharacterState state)
    {
        NativeSpineResource? resource = state.Resource;
        if (resource == null)
            return;

        string[] candidates = resource.AnimationNames
            .Where(name =>
                !name.Equals("idle", StringComparison.OrdinalIgnoreCase) &&
                !name.Equals("action", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (candidates.Length == 0)
            return;

        resource.SetAnimation(
            candidates[Random.Shared.Next(candidates.Length)],
            true);
    }

    private void UpdateWindowRegions()
    {
        if (_window == null || _inputWindow == null)
            return;

        // 每帧全速栅格化（AABB 空间网格，成本与可见面积成正比）；
        // 只有区域内容真正变化时 NativeInputWindow 才会重设窗口区域。
        // 配置模式同样保持区域活跃：面板遮住其矩形内区域，
        // 其余桌面可直接拖拽角色，不再依赖 WPF 覆盖层。
        _inputRegions.Clear();
        RefreshWorkingAreas();
        foreach (NativeCharacterState state in _states.Values)
        {
            if (!state.IsVisible)
                continue;

            float anchorX =
                _window.Left + ToClientPixelX(state.Config.PositionX);
            float anchorY =
                _window.Top + ToClientPixelY(state.Config.PositionY);
            foreach (Rectangle run in RasterizeSilhouette(
                     state.LastBatches,
                     state.ScreenBounds,
                     anchorX,
                     anchorY,
                     state.PivotX,
                     state.PivotY,
                     (float)state.CurrentScale * _window.DpiScale,
                     _window.Left,
                     _window.Top))
            {
                _inputRegions.AddRange(
                    ClipToWorkingAreas(run, _workingAreas));
            }
        }

        _inputWindow.SetInteractiveRegions(_inputRegions);
        _inputWindow.Show();
    }

    internal static IReadOnlyList<Rectangle> RasterizeSilhouette(
        IReadOnlyList<NativeSpineDrawBatch> batches,
        RectangleF screenBounds,
        float anchorX,
        float anchorY,
        float pivotX,
        float pivotY,
        float pixelScale,
        int windowLeft,
        int windowTop)
    {
        if (batches.Count == 0 ||
            screenBounds.IsEmpty ||
            pixelScale <= 0)
        {
            return Array.Empty<Rectangle>();
        }

        Rectangle clientBounds = Rectangle.FromLTRB(
            (int)Math.Floor(screenBounds.Left) - windowLeft,
            (int)Math.Floor(screenBounds.Top) - windowTop,
            (int)Math.Ceiling(screenBounds.Right) - windowLeft,
            (int)Math.Ceiling(screenBounds.Bottom) - windowTop);
        if (clientBounds.Width <= 0 || clientBounds.Height <= 0)
            return Array.Empty<Rectangle>();

        // 自适应格宽：大角色成倍放大格子，保证格数始终有界，
        // 只有病态尺寸才退化为外接矩形。
        int cellSize = SilhouetteCellSize;
        int firstColumn;
        int lastColumn;
        int firstRow;
        int lastRow;
        while (true)
        {
            firstColumn = (int)Math.Floor(
                clientBounds.Left / (double)cellSize);
            lastColumn = (int)Math.Ceiling(
                clientBounds.Right / (double)cellSize) - 1;
            firstRow = (int)Math.Floor(
                clientBounds.Top / (double)cellSize);
            lastRow = (int)Math.Ceiling(
                clientBounds.Bottom / (double)cellSize) - 1;
            int columnCount = lastColumn - firstColumn + 1;
            int rowCount = lastRow - firstRow + 1;
            if (columnCount <= 0 || rowCount <= 0)
                return Array.Empty<Rectangle>();
            if ((long)columnCount * rowCount <= MaxSilhouetteCells ||
                cellSize >= MaxSilhouetteCellSize)
            {
                break;
            }

            cellSize *= 2;
        }

        if ((long)(lastColumn - firstColumn + 1) *
                (lastRow - firstRow + 1) > MaxSilhouetteCells)
        {
            return CreateFallbackRegion(clientBounds);
        }

        SilhouetteScratch scratch = SilhouetteScratchCache ??= new();
        scratch.Triangles.Clear();
        BuildSilhouetteTriangles(batches, scratch.Triangles);
        if (scratch.Triangles.Count == 0)
            return Array.Empty<Rectangle>();

        int columnTotal = lastColumn - firstColumn + 1;
        int rowTotal = lastRow - firstRow + 1;
        scratch.EnsureGridCapacity(columnTotal * rowTotal);
        Array.Clear(scratch.Grid, 0, columnTotal * rowTotal);

        // 散射标记：每个三角形只扫自己包围盒覆盖的格子，
        // 单格被任一三角形标记后跳过，成本与可见面积成正比。
        foreach (SilhouetteTriangle triangle in scratch.Triangles)
        {
            float minClientX =
                anchorX + (triangle.MinX - pivotX) * pixelScale -
                windowLeft;
            float maxClientX =
                anchorX + (triangle.MaxX - pivotX) * pixelScale -
                windowLeft;
            float minClientY =
                anchorY + (triangle.MinY - pivotY) * pixelScale -
                windowTop;
            float maxClientY =
                anchorY + (triangle.MaxY - pivotY) * pixelScale -
                windowTop;
            int triangleFirstColumn = Math.Clamp(
                (int)Math.Floor(minClientX / cellSize),
                firstColumn,
                lastColumn);
            int triangleLastColumn = Math.Clamp(
                (int)Math.Floor(maxClientX / cellSize),
                firstColumn,
                lastColumn);
            int triangleFirstRow = Math.Clamp(
                (int)Math.Floor(minClientY / cellSize),
                firstRow,
                lastRow);
            int triangleLastRow = Math.Clamp(
                (int)Math.Floor(maxClientY / cellSize),
                firstRow,
                lastRow);
            for (int row = triangleFirstRow; row <= triangleLastRow;
                 row++)
            {
                float skeletonY = pivotY +
                    (windowTop +
                         row * cellSize +
                         cellSize / 2f -
                         anchorY) /
                    pixelScale;
                int gridRow = row - firstRow;
                for (int column = triangleFirstColumn;
                     column <= triangleLastColumn;
                     column++)
                {
                    int gridIndex =
                        gridRow * columnTotal + column - firstColumn;
                    if (scratch.Grid[gridIndex])
                        continue;

                    float skeletonX = pivotX +
                        (windowLeft +
                             column * cellSize +
                             cellSize / 2f -
                             anchorX) /
                        pixelScale;
                    if (IsVisibleSkeletonPoint(
                            triangle,
                            skeletonX,
                            skeletonY))
                    {
                        scratch.Grid[gridIndex] = true;
                    }
                }
            }
        }

        scratch.Runs.Clear();
        List<(int Start, int End)>? mergeSpans = null;
        List<(int Start, int End)> currentSpans = scratch.CurrentSpans;
        int mergeStartRow = firstRow;
        for (int row = firstRow; row <= lastRow; row++)
        {
            currentSpans.Clear();
            int gridRow = row - firstRow;
            int runStart = int.MinValue;
            for (int column = firstColumn; column <= lastColumn + 1;
                 column++)
            {
                bool visible = column <= lastColumn &&
                    scratch.Grid[
                        gridRow * columnTotal + column - firstColumn];
                if (visible)
                {
                    if (runStart == int.MinValue)
                        runStart = column;
                }
                else if (runStart != int.MinValue)
                {
                    currentSpans.Add((runStart, column));
                    runStart = int.MinValue;
                }
            }

            if (SpansEqual(mergeSpans, currentSpans))
                continue;

            FlushSilhouetteRuns(
                scratch.Runs,
                mergeSpans,
                mergeStartRow,
                row,
                cellSize);
            mergeSpans = currentSpans.Count == 0
                ? null
                : CopySpans(scratch, currentSpans);
            mergeStartRow = row;
            if (scratch.Runs.Count > MaxSilhouetteRunsPerCharacter)
                return CreateFallbackRegion(clientBounds);
        }

        FlushSilhouetteRuns(
            scratch.Runs,
            mergeSpans,
            mergeStartRow,
            lastRow + 1,
            cellSize);
        if (scratch.Runs.Count > MaxSilhouetteRunsPerCharacter)
            return CreateFallbackRegion(clientBounds);

        return scratch.Runs;
    }

    [ThreadStatic]
    private static SilhouetteScratch? SilhouetteScratchCache;

    private static List<(int Start, int End)> CopySpans(
        SilhouetteScratch scratch,
        List<(int Start, int End)> source)
    {
        scratch.CopyBuffer.Clear();
        scratch.CopyBuffer.AddRange(source);
        return scratch.CopyBuffer;
    }

    private static void FlushSilhouetteRuns(
        List<Rectangle> runs,
        List<(int Start, int End)>? spans,
        int startRow,
        int endRowExclusive,
        int cellSize)
    {
        if (spans == null)
            return;

        foreach ((int start, int end) in spans)
        {
            runs.Add(Rectangle.FromLTRB(
                start * cellSize,
                startRow * cellSize,
                end * cellSize,
                endRowExclusive * cellSize));
        }
    }

    private static bool SpansEqual(
        List<(int Start, int End)>? left,
        List<(int Start, int End)> right)
    {
        if (left == null || left.Count != right.Count)
            return false;

        for (int index = 0; index < left.Count; index++)
        {
            if (left[index] != right[index])
                return false;
        }

        return true;
    }

    private static void BuildSilhouetteTriangles(
        IReadOnlyList<NativeSpineDrawBatch> batches,
        List<SilhouetteTriangle> triangles)
    {
        foreach (NativeSpineDrawBatch batch in batches)
        {
            for (int triangle = batch.IndexCount - 3;
                 triangle >= 0;
                 triangle -= 3)
            {
                NativeSpineVertex first =
                    batch.Vertices[batch.Indices[triangle]];
                NativeSpineVertex second =
                    batch.Vertices[batch.Indices[triangle + 1]];
                NativeSpineVertex third =
                    batch.Vertices[batch.Indices[triangle + 2]];
                triangles.Add(new SilhouetteTriangle(
                    first,
                    second,
                    third,
                    batch.Texture));
            }
        }
    }

    private static bool IsVisibleSkeletonPoint(
        SilhouetteTriangle triangle,
        float x,
        float y)
    {
        if (x < triangle.MinX ||
            x > triangle.MaxX ||
            y < triangle.MinY ||
            y > triangle.MaxY)
        {
            return false;
        }

        if (!TryGetBarycentric(
                x,
                y,
                triangle.First.Position,
                triangle.Second.Position,
                triangle.Third.Position,
                out float firstWeight,
                out float secondWeight,
                out float thirdWeight))
        {
            return false;
        }

        float u =
            triangle.First.TextureCoordinate.X * firstWeight +
            triangle.Second.TextureCoordinate.X * secondWeight +
            triangle.Third.TextureCoordinate.X * thirdWeight;
        float v =
            triangle.First.TextureCoordinate.Y * firstWeight +
            triangle.Second.TextureCoordinate.Y * secondWeight +
            triangle.Third.TextureCoordinate.Y * thirdWeight;
        return triangle.Texture.IsVisiblePixel(u, v, 1f);
    }

    private static IReadOnlyList<Rectangle> CreateFallbackRegion(
        Rectangle clientBounds)
    {
        int cellSize = SilhouetteCellSize;
        int margin = SilhouetteFallbackMargin;
        int left = (int)Math.Floor(
            (clientBounds.Left - margin) / (double)cellSize) * cellSize;
        int top = (int)Math.Floor(
            (clientBounds.Top - margin) / (double)cellSize) * cellSize;
        int right = (int)Math.Ceiling(
            (clientBounds.Right + margin) / (double)cellSize) * cellSize;
        int bottom = (int)Math.Ceiling(
            (clientBounds.Bottom + margin) / (double)cellSize) * cellSize;
        return [Rectangle.FromLTRB(left, top, right, bottom)];
    }

    private sealed class SilhouetteScratch
    {
        public List<SilhouetteTriangle> Triangles { get; } = [];

        public bool[] Grid { get; private set; } = [];

        public List<Rectangle> Runs { get; } = [];

        public List<(int Start, int End)> CurrentSpans { get; } = [];

        public List<(int Start, int End)> CopyBuffer { get; } = [];

        public void EnsureGridCapacity(int required)
        {
            if (Grid.Length >= required)
                return;

            int capacity = Grid.Length;
            while (capacity < required)
                capacity = Math.Max(required, capacity * 2);
            Grid = new bool[capacity];
        }
    }

    private readonly struct SilhouetteTriangle(
        NativeSpineVertex first,
        NativeSpineVertex second,
        NativeSpineVertex third,
        NativeTextureSource texture)
    {
        public readonly NativeSpineVertex First = first;
        public readonly NativeSpineVertex Second = second;
        public readonly NativeSpineVertex Third = third;
        public readonly NativeTextureSource Texture = texture;
        public readonly float MinX = Math.Min(
            Math.Min(first.Position.X, second.Position.X),
            third.Position.X);
        public readonly float MinY = Math.Min(
            Math.Min(first.Position.Y, second.Position.Y),
            third.Position.Y);
        public readonly float MaxX = Math.Max(
            Math.Max(first.Position.X, second.Position.X),
            third.Position.X);
        public readonly float MaxY = Math.Max(
            Math.Max(first.Position.Y, second.Position.Y),
            third.Position.Y);
    }

    private void RefreshWorkingAreas()
    {
        if (_inputWindow == null)
        {
            return;
        }

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
                _inputWindow.Left,
                _inputWindow.Top));
        }

        _workingAreasRefreshTimestamp = now;
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

    internal static IReadOnlyList<Rectangle> ClipToWorkingAreas(
        Rectangle characterRegion,
        IEnumerable<Rectangle> workingAreas)
    {
        return workingAreas
            .Select(area => Rectangle.Intersect(characterRegion, area))
            .Where(region => region.Width > 0 && region.Height > 0)
            .ToArray();
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

    private void OnNativeMouseInput(
        uint nativeMessage,
        int x,
        int y)
    {
        if (_closed)
            return;

        int message = checked((int)nativeMessage);
        if (message != WmLeftButtonDown &&
            message != WmMouseMove &&
            message != WmLeftButtonUp &&
            message != WmCaptureChanged)
        {
            return;
        }

        NativePoint point = new() { X = x, Y = y };
        if (message == WmLeftButtonDown &&
            TryHitCharacter(point, out NativeCharacterState? state))
        {
            _pointerCharacterId = state.Config.Id;
            _pointerStart = point;
            _pointerLatest = point;
            _pointerStartX = state.Config.PositionX;
            _pointerStartY = state.Config.PositionY;
            _pointerDragging = false;
            _pointerMovePending = false;
            return;
        }

        if (_pointerCharacterId != null && message == WmMouseMove)
        {
            _pointerLatest = point;
            int deltaX = point.X - _pointerStart.X;
            int deltaY = point.Y - _pointerStart.Y;
            if (!_pointerDragging &&
                _renderDragEnabled &&
                (Math.Abs(deltaX) >=
                     SystemParameters.MinimumHorizontalDragDistance *
                     (_window?.DpiScale ?? 1) ||
                 Math.Abs(deltaY) >=
                     SystemParameters.MinimumVerticalDragDistance *
                     (_window?.DpiScale ?? 1)))
            {
                _pointerDragging = true;
                BeginCharacterMove();
                if (_states.TryGetValue(
                        _pointerCharacterId,
                        out NativeCharacterState? dragState))
                {
                    PlayDragAnimation(dragState);
                }
            }

            if (_pointerDragging)
                _pointerMovePending = true;
            return;
        }

        if (_pointerCharacterId != null && message == WmLeftButtonUp)
        {
            _pointerLatest = point;
            _pointerMovePending = _pointerDragging;
            string characterId = _pointerCharacterId;
            FlushPendingPointerMove();
            if (_states.TryGetValue(
                    characterId,
                    out NativeCharacterState? releasedState))
            {
                if (_pointerDragging)
                {
                    EndCharacterMove();
                    SelectModeAnimation(releasedState);
                    CharacterPositionCommitted?.Invoke(
                        characterId,
                        releasedState.Config.PositionX,
                        releasedState.Config.PositionY);
                }
                else
                {
                    PlayClickAnimation(releasedState);
                }
            }

            ResetPointerState();
            CommitComposition();
            return;
        }

        if (_pointerCharacterId != null && message == WmCaptureChanged)
            CancelPointerInteraction(commitPosition: true);
    }

    private bool TryHitCharacter(
        NativePoint point,
        out NativeCharacterState result)
    {
        foreach (string characterId in _zOrder.TopToBottom)
        {
            if (!_states.TryGetValue(
                    characterId,
                    out NativeCharacterState? state))
            {
                continue;
            }

            if (state.IsVisible &&
                !state.ScreenBounds.IsEmpty &&
                state.ScreenBounds.Contains(point.X, point.Y) &&
                HitTestGeometry(state, point))
            {
                result = state;
                return true;
            }
        }

        result = null!;
        return false;
    }

    private bool HitTestGeometry(
        NativeCharacterState state,
        NativePoint point)
    {
        if (_window == null || state.LastBatches.Count == 0)
            return false;

        float pixelScale =
            (float)state.CurrentScale * _window.DpiScale;
        if (pixelScale <= 0)
            return false;

        float anchorX =
            _window.Left + ToClientPixelX(state.Config.PositionX);
        float anchorY =
            _window.Top + ToClientPixelY(state.Config.PositionY);
        float x = state.PivotX + (point.X - anchorX) / pixelScale;
        float y = state.PivotY + (point.Y - anchorY) / pixelScale;

        for (int batchIndex = state.LastBatches.Count - 1;
             batchIndex >= 0;
             batchIndex--)
        {
            NativeSpineDrawBatch batch = state.LastBatches[batchIndex];
            for (int triangle = batch.IndexCount - 3;
                 triangle >= 0;
                 triangle -= 3)
            {
                NativeSpineVertex first =
                    batch.Vertices[batch.Indices[triangle]];
                NativeSpineVertex second =
                    batch.Vertices[batch.Indices[triangle + 1]];
                NativeSpineVertex third =
                    batch.Vertices[batch.Indices[triangle + 2]];
                if (!TryGetBarycentric(
                        x,
                        y,
                        first.Position,
                        second.Position,
                        third.Position,
                        out float firstWeight,
                        out float secondWeight,
                        out float thirdWeight))
                {
                    continue;
                }

                float u =
                    first.TextureCoordinate.X * firstWeight +
                    second.TextureCoordinate.X * secondWeight +
                    third.TextureCoordinate.X * thirdWeight;
                float v =
                    first.TextureCoordinate.Y * firstWeight +
                    second.TextureCoordinate.Y * secondWeight +
                    third.TextureCoordinate.Y * thirdWeight;
                float opacity =
                    first.LightColor.W * firstWeight +
                    second.LightColor.W * secondWeight +
                    third.LightColor.W * thirdWeight;
                if (batch.Texture.IsVisiblePixel(u, v, opacity))
                    return true;
            }
        }

        return false;
    }

    private static bool TryGetBarycentric(
        float x,
        float y,
        System.Numerics.Vector2 first,
        System.Numerics.Vector2 second,
        System.Numerics.Vector2 third,
        out float firstWeight,
        out float secondWeight,
        out float thirdWeight)
    {
        float denominator =
            (second.Y - third.Y) * (first.X - third.X) +
            (third.X - second.X) * (first.Y - third.Y);
        if (MathF.Abs(denominator) < 0.00001f)
        {
            firstWeight = 0;
            secondWeight = 0;
            thirdWeight = 0;
            return false;
        }

        firstWeight =
            ((second.Y - third.Y) * (x - third.X) +
             (third.X - second.X) * (y - third.Y)) /
            denominator;
        secondWeight =
            ((third.Y - first.Y) * (x - third.X) +
             (first.X - third.X) * (y - third.Y)) /
            denominator;
        thirdWeight = 1 - firstWeight - secondWeight;
        const float tolerance = -0.0001f;
        return firstWeight >= tolerance &&
               secondWeight >= tolerance &&
               thirdWeight >= tolerance;
    }

    private void PurgeUnusedTextures()
    {
        if (_graphics == null)
            return;

        HashSet<string> activePaths = new(
            _states.Values
                .Where(state => state.Resource != null)
                .SelectMany(state =>
                    state.Resource!.TextureLoader.Textures)
                .Select(texture => texture.Path),
            StringComparer.OrdinalIgnoreCase);
        _graphics.PurgeTextures(activePaths);
    }

    private void FlushPendingPointerMove()
    {
        if (!_pointerMovePending ||
            !_pointerDragging ||
            _pointerCharacterId == null ||
            _window == null)
        {
            return;
        }

        _pointerMovePending = false;
        MoveCharacter(
            _pointerCharacterId,
            _pointerStartX +
            (_pointerLatest.X - _pointerStart.X) / _window.DpiScale,
            _pointerStartY +
            (_pointerLatest.Y - _pointerStart.Y) / _window.DpiScale);
    }

    private void CancelPointerIfCharacter(string characterId)
    {
        if (string.Equals(
                _pointerCharacterId,
                characterId,
                StringComparison.Ordinal))
        {
            CancelPointerInteraction(commitPosition: false);
        }
    }

    private void CancelPointerInteraction(bool commitPosition)
    {
        if (_pointerCharacterId != null &&
            _states.TryGetValue(
                _pointerCharacterId,
                out NativeCharacterState? state))
        {
            FlushPendingPointerMove();
            if (_pointerDragging)
            {
                EndCharacterMove();
                SelectModeAnimation(state);
                if (commitPosition)
                {
                    CharacterPositionCommitted?.Invoke(
                        state.Config.Id,
                        state.Config.PositionX,
                        state.Config.PositionY);
                }
            }
        }

        ResetPointerState();
    }

    private void ResetPointerState()
    {
        _pointerCharacterId = null;
        _pointerDragging = false;
        _pointerMovePending = false;
    }

    private readonly record struct PendingFrame(
        NativeCharacterState State,
        IReadOnlyList<NativeSpineDrawBatch> Batches,
        float PixelScale);

    private struct NativePoint
    {
        public int X;
        public int Y;
    }
}
