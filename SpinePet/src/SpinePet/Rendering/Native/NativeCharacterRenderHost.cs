using System.Diagnostics;
using System.Drawing;
using System.Windows;
using System.Windows.Threading;
using SpinePet.Infrastructure;
using SpinePet.Models;
using SpinePet.Rendering;

namespace SpinePet.Rendering.Native;

public sealed class NativeCharacterRenderHost :
    ICharacterRenderHost,
    IDisposable
{
    private const double DefaultScale = 0.2;
    private const double MaximumScale = 2.0;
    private const double MinimumScale = 0.05;
    private const double CharacterBottomMargin = 24;

    private readonly object _initializationSync = new();
    private readonly NativeCharacterScene _scene = new();
    private readonly NativeRenderSession _session = new();
    private readonly NativeInputRegionCoordinator _inputRegions = new();
    private readonly NativeFrameRenderer _frameRenderer;
    private readonly NativePointerController _pointer;
    private readonly Dispatcher _dispatcher;
    private readonly NativeFrameScheduler _frameScheduler;
    private readonly DispatcherTimer _scaleSettleTimer;
    private readonly Stopwatch _frameClock = new();
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly HashSet<string> _scaleShrinkPending =
        new(StringComparer.Ordinal);
    private Task? _initializationTask;
    private bool _configMode;
    private bool _renderDragEnabled = true;
    private bool _closed;
    private int _explicitMoveDepth;
    private int _targetFrameRate = GlobalConfig.DefaultTargetFrameRate;

    public NativeCharacterRenderHost()
    {
        _dispatcher = System.Windows.Application.Current?.Dispatcher ??
            Dispatcher.CurrentDispatcher;
        _frameRenderer = new NativeFrameRenderer(
            _scene,
            _session,
            _inputRegions);
        _pointer = new NativePointerController(
            _scene,
            () => _session.Window,
            MoveCharacter,
            BeginCharacterMove,
            EndCharacterMove,
            NativeAnimationController.PlayClickAnimation,
            QueueCharacterPositionCommitted,
            QueueRightPress,
            QueueRightRelease);
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
    public event Action<string>? CharacterRightPressed;
    public event Action<string>? CharacterRightReleased;

    public bool IsCharacterLoading(string characterId) =>
        !_closed &&
        _scene.TryGet(characterId, out NativeCharacterState? state) &&
        state.IsLoading;

    public bool IsCharacterVisible(string characterId) =>
        !_closed &&
        _scene.TryGet(characterId, out NativeCharacterState? state) &&
        state.IsVisible;

    public IReadOnlyList<string> GetAnimationNames(string characterId) =>
        !_closed &&
        _scene.TryGet(characterId, out NativeCharacterState? state)
            ? state.CachedAnimationNames
            : Array.Empty<string>();

    public double GetMaxScale(string characterId) =>
        !_closed &&
        _scene.TryGet(characterId, out NativeCharacterState? state)
            ? state.MaxScale
            : MaximumScale;

    public double GetCurrentScale(string characterId) =>
        !_closed &&
        _scene.TryGet(characterId, out NativeCharacterState? state)
            ? state.CurrentScale
            : DefaultScale;

    internal bool IsFrameLoopRunning => _frameScheduler.IsRunning;

    internal int TargetFrameRate => _targetFrameRate;

    internal TimeSpan FrameInterval => _frameScheduler.Interval;

    public Task InitializeAsync()
    {
        lock (_initializationSync)
        {
            if (_closed)
                return Task.CompletedTask;

            _initializationTask ??= _dispatcher.CheckAccess()
                ? InitializeOnDispatcher()
                : _dispatcher.InvokeAsync(
                    InitializeOnDispatcher).Task.Unwrap();
            return _initializationTask;
        }
    }

    public async Task ShowCharacterAsync(
        CharacterConfig character,
        bool configMode,
        double speed)
    {
        if (_closed)
            return;

        await InitializeAsync();
        if (_closed)
            return;

        SetConfigMode(configMode);

        NativeCharacterState state = GetOrCreateState(character);
        string resourceKey = GetResourceKey(character);
        double normalizedScale = Math.Clamp(
            character.Scale > 0 ? character.Scale : DefaultScale,
            MinimumScale,
            state.MaxScale);
        double normalizedSpeed = Math.Clamp(speed, 0.1, 2);
        bool sameResourceKey = string.Equals(
            state.ResourceKey,
            resourceKey,
            StringComparison.OrdinalIgnoreCase);
        bool scaleChanged = state.CurrentScale != normalizedScale;
        bool speedChanged =
            state.Config.AnimationSpeed != normalizedSpeed;

        if ((state.Resource != null || state.IsLoading) &&
            !sameResourceKey)
        {
            _pointer.CancelIfCharacter(character.Id);
            ReleaseCharacterResources(state);
            RefreshInputRegions();
            sameResourceKey = false;
        }

        if (state.IsLoading &&
            state.LoadTask != null &&
            sameResourceKey)
        {
            state.Config = character;
            state.CurrentScale = normalizedScale;
            state.Config.Scale = normalizedScale;
            state.Config.AnimationSpeed = normalizedSpeed;
            state.IsVisible = true;
            state.Config.Visible = true;
            await state.LoadTask;
            return;
        }

        bool sameLoadedState =
            state.Resource != null &&
            sameResourceKey &&
            state.IsVisible &&
            state.Surface != null &&
            state.CurrentScale == normalizedScale &&
            state.Config.AnimationSpeed == normalizedSpeed;
        if (sameLoadedState)
        {
            character.Visible = true;
            return;
        }

        bool wasVisible = state.IsVisible;
        state.Config = character;
        state.ResourceKey = resourceKey;
        state.CurrentScale = normalizedScale;
        state.Config.Scale = normalizedScale;
        state.Config.AnimationSpeed = normalizedSpeed;
        state.IsVisible = true;
        state.Config.Visible = true;

        if (state.Resource != null)
        {
            if (state.Surface?.SetVisible(true) == true)
                _scene.MoveToTop(state.Config.Id);

            NativeAnimationController.SelectModeAnimation(state);
            _frameRenderer.UpdateSurfacePosition(state);
            _frameRenderer.MarkCompositionDirty();
            RenderFrame(0);
            UpdateFrameTimerState();
            if (!wasVisible || scaleChanged || speedChanged)
                CharactersStateChanged?.Invoke();
            return;
        }

        Task loadTask = LoadCharacterAsync(character, state);
        state.LoadTask = loadTask;
        try
        {
            await loadTask;
        }
        finally
        {
            if (ReferenceEquals(state.LoadTask, loadTask))
                state.LoadTask = null;
        }
    }

    public void HideCharacter(string characterId)
    {
        if (_closed ||
            !_scene.TryGet(
                characterId,
                out NativeCharacterState? state) ||
            (!state.IsVisible &&
             !state.IsLoading &&
             state.Resource == null &&
             state.Surface == null))
        {
            return;
        }

        _pointer.CancelIfCharacter(characterId);
        state.IsVisible = false;
        state.Config.Visible = false;
        ReleaseCharacterResources(state);
        RefreshInputRegions();
        UpdateFrameTimerState();
        _frameRenderer.Commit();
        CharactersStateChanged?.Invoke();
    }

    public void RemoveCharacter(string characterId)
    {
        if (_closed ||
            !_scene.TryGet(characterId, out _))
        {
            return;
        }

        _pointer.CancelIfCharacter(characterId);
        if (!_scene.Remove(
                characterId,
                out NativeCharacterState? state))
        {
            return;
        }

        _scaleShrinkPending.Remove(characterId);
        ReleaseCharacterResources(state);
        RefreshInputRegions();
        UpdateFrameTimerState();
        _frameRenderer.Commit();
        CharactersStateChanged?.Invoke();
    }

    public void SetCharacterScale(string characterId, double scale)
    {
        if (_closed ||
            !_scene.TryGet(
                characterId,
                out NativeCharacterState? state))
        {
            return;
        }

        double normalized = Math.Clamp(
            scale,
            MinimumScale,
            state.MaxScale);
        if (state.CurrentScale == normalized &&
            state.Config.Scale == normalized)
        {
            return;
        }

        state.CurrentScale = normalized;
        state.Config.Scale = normalized;
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
        if (_closed ||
            !_scene.TryGet(
                characterId,
                out NativeCharacterState? state))
        {
            return;
        }

        double normalized = Math.Clamp(speed, 0.1, 2);
        if (state.Config.AnimationSpeed == normalized)
            return;

        state.Config.AnimationSpeed = normalized;
        if (state.Resource != null)
        {
            state.Resource.AnimationState.TimeScale =
                (float)normalized;
        }
    }

    public void PlayCharacterAnimation(
        string characterId,
        string animation,
        bool repeat)
    {
        if (_closed ||
            !_scene.TryGet(
                characterId,
                out NativeCharacterState? state))
        {
            return;
        }

        NativeAnimationController.SetPersistent(
            state,
            animation,
            repeat);
    }

    public void PlayCharacterAnimationSequence(
        string characterId,
        IReadOnlyList<string> animations,
        string? restoreAnimation,
        bool loopLast,
        IReadOnlyList<string>? parallelAnimations = null)
    {
        if (_closed ||
            !_scene.TryGet(characterId, out NativeCharacterState? state) ||
            state.Resource == null)
        {
            return;
        }

        state.TemporaryAnimationPlayback.Clear();
        state.Resource.SetAnimationSequence(
            animations,
            restoreAnimation,
            loopLast,
            parallelAnimations);
    }

    public async Task PreloadBattleResourcesAsync(CharacterConfig character)
    {
        if (_closed || character.Battle == null)
            return;

        await InitializeAsync();
        if (!_scene.TryGet(character.Id, out NativeCharacterState? state))
            return;

        await PreloadResourceSlotAsync(
            state,
            CharacterBattleStates.Aim,
            character.Battle.Aim.CreateRenderConfig(character));
        await PreloadResourceSlotAsync(
            state,
            CharacterBattleStates.Cover,
            character.Battle.Cover.CreateRenderConfig(character));
    }

    public bool SetCharacterResourceState(
        string characterId,
        string resourceState,
        string? idleAnimation)
    {
        if (_closed ||
            !_scene.TryGet(characterId, out NativeCharacterState? state) ||
            state.Resource == null)
        {
            return false;
        }

        if (state.ActiveResourceState.Equals(
                resourceState,
                StringComparison.OrdinalIgnoreCase))
        {
            ApplyResourceIdleAnimation(
                state,
                idleAnimation,
                restart: false);
            return true;
        }

        if (!state.ResourceSlots.Remove(
                resourceState,
                out NativeCharacterLoadResult target))
        {
            AppLogger.Write(
                nameof(NativeCharacterRenderHost),
                $"resource-state-switch-missed id={characterId} " +
                $"active={state.ActiveResourceState} target={resourceState}");
            return false;
        }

        state.ResourceSlots[state.ActiveResourceState] =
            new NativeCharacterLoadResult(
                state.Resource,
                state.SetupBounds,
                state.Envelope);
        state.Resource = target.Resource;
        state.SetupBounds = target.Setup;
        state.Envelope = target.Envelope;
        state.ActiveResourceState = resourceState;
        state.Resource.AnimationState.TimeScale =
            (float)state.Config.AnimationSpeed;
        ApplyResourceIdleAnimation(
            state,
            idleAnimation,
            restart: true);
        state.HasCachedSilhouette = false;
        state.CachedSilhouetteRuns.Clear();
        _frameRenderer.UpdateSurfacePosition(state);
        _frameRenderer.MarkCompositionDirty();
        RenderFrame(0);
        RefreshInputRegions();
        return true;
    }

    public void SetConfigMode(bool configMode)
    {
        if (_closed || _configMode == configMode)
            return;

        _configMode = configMode;
        _pointer.Cancel(commitPosition: false);
    }

    private static void ApplyResourceIdleAnimation(
        NativeCharacterState state,
        string? configuredAnimation,
        bool restart)
    {
        NativeSpineResource resource = state.Resource!;
        string? animation =
            NativeAnimationController.SelectConfiguredOrIdleAnimationName(
                configuredAnimation,
                resource.AnimationNames);
        if (!restart &&
            string.Equals(
                state.PersistentAnimationName,
                animation,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        state.TemporaryAnimationPlayback.Clear();
        resource.SetAnimation(animation, true);
        state.PersistentAnimationName = animation;
    }

    public void SetRenderDragEnabled(bool enabled)
    {
        if (_closed || _renderDragEnabled == enabled)
            return;

        _renderDragEnabled = enabled;
        _pointer.SetDragEnabled(enabled);
    }

    public void SetTargetFrameRate(int frameRate)
    {
        int normalized = GlobalConfig.NormalizeTargetFrameRate(frameRate);
        if (_closed || _targetFrameRate == normalized)
            return;

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
        if (_closed ||
            !_scene.TryGet(
                characterId,
                out NativeCharacterState? state) ||
            (state.Config.PositionX == left &&
             state.Config.PositionY == top))
        {
            return;
        }

        state.Config.PositionX = left;
        state.Config.PositionY = top;
        _frameRenderer.UpdateSurfacePosition(state);
        _frameRenderer.MarkCompositionDirty();
    }

    public void BeginCharacterMove()
    {
        if (!_closed)
            _explicitMoveDepth++;
    }

    public void EndCharacterMove()
    {
        if (!_closed && _explicitMoveDepth > 0)
            _explicitMoveDepth--;
    }

    public void ResetCharacterPosition(string characterId)
    {
        if (_closed ||
            !_scene.TryGet(
                characterId,
                out NativeCharacterState? state))
        {
            return;
        }

        Rect area = SystemParameters.WorkArea;
        double left = area.Left + area.Width / 2;
        double top = area.Bottom - CharacterBottomMargin;
        if (state.Config.PositionX == left &&
            state.Config.PositionY == top)
        {
            return;
        }

        MoveCharacter(characterId, left, top);
        _frameRenderer.Commit();
        CharacterPositionCommitted?.Invoke(
            characterId,
            state.Config.PositionX,
            state.Config.PositionY);
    }

    public void HideAll()
    {
        if (_closed ||
            !_scene.States.Any(state =>
                state.IsVisible ||
                state.IsLoading ||
                state.Resource != null ||
                state.Surface != null))
        {
            return;
        }

        _pointer.Cancel(commitPosition: false);
        foreach (NativeCharacterState state in _scene.States)
        {
            state.IsVisible = false;
            state.Config.Visible = false;
            ReleaseCharacterResources(state);
        }

        RefreshInputRegions();
        UpdateFrameTimerState();
        _frameRenderer.Commit();
        CharactersStateChanged?.Invoke();
    }

    public async Task RestoreVisibleCharactersAsync(
        IEnumerable<CharacterConfig> characters,
        bool configMode)
    {
        if (_closed)
            return;

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
        if (_session.InputWindow != null)
            _session.InputWindow.MouseInput = null;

        _frameScheduler.Dispose();
        _scaleSettleTimer.Stop();
        _scaleSettleTimer.Tick -= OnScaleSettle;
        _pointer.Cancel(commitPosition: false);
        _scaleShrinkPending.Clear();
        _scene.Clear();
        _session.Dispose();
        _lifetimeCancellation.Dispose();

        CharacterScaleChanged = null;
        CharacterAnimationsLoaded = null;
        CharacterLoadFailed = null;
        CharactersStateChanged = null;
        CharacterPositionCommitted = null;
        CharacterRightPressed = null;
        CharacterRightReleased = null;
    }

    public void Dispose()
    {
        Close();
    }

    private async Task LoadCharacterAsync(
        CharacterConfig character,
        NativeCharacterState state)
    {
        state.IsLoading = true;
        int loadVersion = ++state.LoadVersion;
        CharactersStateChanged?.Invoke();

        try
        {
            NativeCharacterLoadResult loaded =
                await NativeCharacterLoader.LoadAsync(
                    character,
                    _lifetimeCancellation.Token);
            await _dispatcher.InvokeAsync(() =>
            {
                if (_closed ||
                    !_scene.TryGet(
                        character.Id,
                        out NativeCharacterState? current) ||
                    !ReferenceEquals(current, state) ||
                    current.LoadVersion != loadVersion ||
                    _session.Graphics is not { } graphics)
                {
                    loaded.Resource.Dispose();
                    return;
                }

                current.Resource = loaded.Resource;
                current.ActiveResourceState = CharacterDisplayModes.Normal;
                current.CachedAnimationNames =
                    loaded.Resource.AnimationNames;
                current.SetupBounds = loaded.Setup;
                current.Envelope = loaded.Envelope;
                current.Surface = graphics.CreateSurface();
                if (current.Surface.SetVisible(current.IsVisible) &&
                    current.IsVisible)
                {
                    _scene.MoveToTop(current.Config.Id);
                }

                current.IsLoading = false;
                NativeAnimationController.SelectModeAnimation(current);
                _frameRenderer.UpdateSurfacePosition(current);
                _frameRenderer.MarkCompositionDirty();
                RenderFrame(0);
                UpdateFrameTimerState();

                if (string.IsNullOrWhiteSpace(
                        current.Config.ConfiguredAnimation) &&
                    current.Resource.AnimationNames.Count > 0)
                {
                    current.Config.ConfiguredAnimation =
                        NativeAnimationController.SelectIdleAnimationName(
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
            // Closing or unloading invalidates this load version.
        }
        catch (Exception exception)
        {
            await _dispatcher.InvokeAsync(() =>
            {
                if (_closed ||
                    !_scene.TryGet(
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
                RefreshInputRegions();
                UpdateFrameTimerState();
                AppLogger.Write(
                    nameof(NativeCharacterRenderHost),
                    $"character-load-failed id={character.Id} " +
                    $"message={exception.Message}");
                CharacterLoadFailed?.Invoke(character.Id);
                CharactersStateChanged?.Invoke();
            });
            throw;
        }
    }

    private Task InitializeOnDispatcher()
    {
        if (_closed)
            return Task.CompletedTask;

        return _session.Initialize(OnNativeMouseInput);
    }

    private static string GetResourceKey(CharacterConfig character) =>
        string.Join(
            "\n",
            new[]
            {
                character.SkeletonPath,
                character.AtlasPath,
                character.TexturePath
            }.Concat(character.AdditionalTexturePaths));

    private NativeCharacterState GetOrCreateState(CharacterConfig character) =>
        _scene.GetOrCreate(
            character,
            DefaultScale,
            MinimumScale,
            MaximumScale);

    private void UpdateFrameTimerState()
    {
        bool shouldRun = !_closed && _scene.States.Any(state =>
            state.IsVisible &&
            state.Resource != null &&
            state.Surface != null);
        if (shouldRun == _frameScheduler.IsRunning)
            return;

        if (shouldRun)
        {
            _frameClock.Restart();
            _frameScheduler.Start();
            return;
        }

        _frameScheduler.Stop();
        _frameClock.Reset();
    }

    private void OnFrame()
    {
        if (_closed)
            return;

        double elapsed = _frameClock.Elapsed.TotalSeconds;
        _frameClock.Restart();
        RenderFrame(Math.Min(elapsed, 0.1));
    }

    private void OnScaleSettle(object? sender, EventArgs eventArgs)
    {
        _scaleSettleTimer.Stop();
        if (_closed || _session.Window == null)
            return;

        foreach (string characterId in _scaleShrinkPending)
        {
            if (_scene.TryGet(
                    characterId,
                    out NativeCharacterState? state) &&
                state.IsVisible &&
                state.Surface != null)
            {
                _frameRenderer.ShrinkSurface(state);
            }
        }

        _scaleShrinkPending.Clear();
        RenderFrame(0);
    }

    private void RenderFrame(double elapsedSeconds)
    {
        if (_closed)
            return;

        _frameRenderer.RenderFrame(
            elapsedSeconds,
            _pointer.IsDragging,
            _pointer.FlushPendingMove,
            _targetFrameRate);
    }

    private void RefreshInputRegions()
    {
        if (!_closed)
        {
            _inputRegions.Update(
                _session,
                _scene.States,
                pointerDragging: false);
        }
    }

    private void ReleaseCharacterResources(NativeCharacterState state)
    {
        state.LoadVersion++;
        state.IsLoading = false;
        if (state.Resource == null &&
            state.Surface == null &&
            state.LastBatches.Count == 0)
        {
            return;
        }

        state.HasCachedSilhouette = false;
        state.CachedSilhouetteRuns.Clear();
        state.LastBatches = Array.Empty<NativeSpineDrawBatch>();
        state.ScreenBounds = RectangleF.Empty;
        state.RenderRegionBounds = RectangleF.Empty;
        state.PreviousRenderRegionBounds = RectangleF.Empty;
        state.Surface?.Dispose();
        state.Surface = null;
        state.Resource?.Dispose();
        state.Resource = null;
        foreach (NativeCharacterLoadResult slot in state.ResourceSlots.Values)
        {
            slot.Resource.Dispose();
        }
        state.ResourceSlots.Clear();
        state.ActiveResourceState = CharacterDisplayModes.Normal;
        state.PersistentAnimationName = null;
        state.ResourceKey = string.Empty;
        state.TemporaryAnimationPlayback.Clear();
        _frameRenderer.PurgeUnusedTextures();
        CollectIfIdleAfterUnload();
    }

    private void CollectIfIdleAfterUnload()
    {
        if (_scene.States.Any(state => state.IsVisible))
            return;

        GC.Collect(
            GC.MaxGeneration,
            GCCollectionMode.Forced,
            blocking: false,
            compacting: true);
    }

    private void OnNativeMouseInput(uint message, int x, int y)
    {
        if (!_closed)
            _pointer.Handle(message, x, y);
    }

    private void QueueCharacterPositionCommitted(
        string characterId,
        double left,
        double top)
    {
        try
        {
            _dispatcher.BeginInvoke(
                DispatcherPriority.Input,
                new Action(() =>
                {
                    if (!_closed)
                    {
                        CharacterPositionCommitted?.Invoke(
                            characterId,
                            left,
                            top);
                    }
                }));
        }
        catch (InvalidOperationException)
        {
            // Ignore input queued during dispatcher shutdown.
        }
    }

    private async Task PreloadResourceSlotAsync(
        NativeCharacterState state,
        string resourceState,
        CharacterConfig resourceConfig)
    {
        if (state.ActiveResourceState.Equals(
                resourceState,
                StringComparison.OrdinalIgnoreCase) ||
            state.ResourceSlots.ContainsKey(resourceState))
        {
            return;
        }

        NativeCharacterLoadResult loaded =
            await NativeCharacterLoader.LoadAsync(
                resourceConfig,
                _lifetimeCancellation.Token);
        await _dispatcher.InvokeAsync(() =>
        {
            if (_closed ||
                !_scene.TryGet(state.Config.Id, out NativeCharacterState? current) ||
                !ReferenceEquals(current, state) ||
                current.ResourceSlots.ContainsKey(resourceState) ||
                current.ActiveResourceState.Equals(
                    resourceState,
                    StringComparison.OrdinalIgnoreCase))
            {
                loaded.Resource.Dispose();
                return;
            }
            loaded.Resource.AnimationState.TimeScale =
                (float)current.Config.AnimationSpeed;
            current.ResourceSlots[resourceState] = loaded;
        });
    }

    private void QueueRightPress(string characterId) =>
        QueueInputEvent(CharacterRightPressed, characterId);

    private void QueueRightRelease(string characterId) =>
        QueueInputEvent(CharacterRightReleased, characterId);

    private void QueueInputEvent(
        Action<string>? handler,
        string characterId)
    {
        try
        {
            _dispatcher.BeginInvoke(
                DispatcherPriority.Input,
                new Action(() =>
                {
                    if (!_closed)
                        handler?.Invoke(characterId);
                }));
        }
        catch (InvalidOperationException)
        {
            // Ignore input queued during dispatcher shutdown.
        }
    }
}
