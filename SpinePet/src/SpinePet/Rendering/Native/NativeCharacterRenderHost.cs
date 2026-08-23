using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Threading;
using SpinePet.Models;

namespace SpinePet.Rendering.Native;

public sealed class NativeCharacterRenderHost :
    ICharacterRenderHost,
    IDisposable
{
    private const double DefaultScale = 0.2;
    private const double MaximumScale = 2.0;

    private readonly Dispatcher _uiDispatcher;
    private readonly ConcurrentDictionary<string, CharacterRenderSnapshot>
        _snapshots = new(StringComparer.Ordinal);
    private readonly object _stateEventSync = new();
    private readonly Dictionary<string, CharacterRenderSnapshot>
        _pendingStateEvents = new(StringComparer.Ordinal);
    private readonly object _latestCommandSync = new();
    private readonly Dictionary<string, Action<NativeCharacterRenderEngine>>
        _latestCommands = new(StringComparer.Ordinal);
    private readonly NativeRenderThread _renderThread;
    private bool _stateDrainScheduled;
    private bool _latestDrainScheduled;
    private int _closed;
    private int _targetFrameRate = GlobalConfig.DefaultTargetFrameRate;

    public NativeCharacterRenderHost()
    {
        _uiDispatcher = System.Windows.Application.Current?.Dispatcher ??
            Dispatcher.CurrentDispatcher;
        _renderThread = new NativeRenderThread(AttachEngineEvents);
    }

    public event Action<string, double, double>? CharacterScaleChanged;
    public event Action<string, IReadOnlyList<string>>?
        CharacterAnimationsLoaded;
    public event Action<string>? CharacterLoadFailed;
    public event Action<CharacterRenderSnapshot>? CharacterStateChanged;
    public event Action? CharactersStateChanged;
    public event Action<string, double, double>? CharacterPositionCommitted;
    public event Action<string>? CharacterRightPressed;
    public event Action<string>? CharacterRightReleased;

    public bool IsCharacterLoading(string characterId) =>
        TryGetSnapshot(characterId).IsLoading;

    public bool IsCharacterVisible(string characterId) =>
        TryGetSnapshot(characterId).IsVisible;

    public IReadOnlyList<string> GetAnimationNames(string characterId) =>
        TryGetSnapshot(characterId).AnimationNames;

    public double GetMaxScale(string characterId) =>
        TryGetSnapshot(characterId).MaximumScale;

    public double GetCurrentScale(string characterId) =>
        TryGetSnapshot(characterId).CurrentScale;

    internal int RenderThreadId => _renderThread.ManagedThreadId;

    internal bool IsFrameLoopRunning =>
        _snapshots.Values.Any(snapshot =>
            snapshot.IsVisible && !snapshot.IsLoading);

    internal int TargetFrameRate => Volatile.Read(ref _targetFrameRate);

    internal TimeSpan FrameInterval =>
        TimeSpan.FromSeconds(1.0 / TargetFrameRate);

    public Task InitializeAsync() =>
        InvokeAsync(engine => engine.InitializeAsync());

    public Task ShowCharacterAsync(
        CharacterConfig character,
        bool configMode,
        double speed) =>
        InvokeAsync(engine =>
            engine.ShowCharacterAsync(character, configMode, speed));

    public void HideCharacter(string characterId) =>
        Post(engine => engine.HideCharacter(characterId));

    public void RemoveCharacter(string characterId)
    {
        _snapshots.TryRemove(characterId, out _);
        Post(engine => engine.RemoveCharacter(characterId));
    }

    public void SetCharacterScale(string characterId, double scale) =>
        QueueLatest(
            $"scale:{characterId}",
            engine => engine.SetCharacterScale(characterId, scale));

    public void SetCharacterSpeed(string characterId, double speed) =>
        QueueLatest(
            $"speed:{characterId}",
            engine => engine.SetCharacterSpeed(characterId, speed));

    public void PlayCharacterAnimation(
        string characterId,
        string animation,
        bool repeat) =>
        Post(engine =>
            engine.PlayCharacterAnimation(characterId, animation, repeat));

    public void PlayCharacterAnimationSequence(
        string characterId,
        IReadOnlyList<string> animations,
        string? restoreAnimation,
        bool loopLast,
        IReadOnlyList<CharacterBattleEffectConfig>? battleEffects = null)
    {
        string[] sequence = animations.ToArray();
        CharacterBattleEffectConfig[]? effects = battleEffects?.ToArray();
        Post(engine => engine.PlayCharacterAnimationSequence(
            characterId,
            sequence,
            restoreAnimation,
            loopLast,
            effects));
    }

    public Task PreloadBattleResourcesAsync(CharacterConfig character) =>
        InvokeAsync(engine => engine.PreloadBattleResourcesAsync(character));

    public Task<bool> SetCharacterResourceStateAsync(
        string characterId,
        string resourceState,
        string? idleAnimation) =>
        InvokeAsync(engine => Task.FromResult(
            engine.SetCharacterResourceState(
                characterId,
                resourceState,
                idleAnimation)));

    public void SetConfigMode(bool configMode) =>
        Post(engine => engine.SetConfigMode(configMode));

    public void SetRenderDragEnabled(bool enabled) =>
        Post(engine => engine.SetRenderDragEnabled(enabled));

    public void SetTargetFrameRate(int frameRate)
    {
        int normalized = GlobalConfig.NormalizeTargetFrameRate(frameRate);
        Volatile.Write(ref _targetFrameRate, normalized);
        QueueLatest(
            "frame-rate",
            engine => engine.SetTargetFrameRate(normalized));
    }

    public void MoveCharacter(
        string characterId,
        double left,
        double top) =>
        QueueLatest(
            $"position:{characterId}",
            engine => engine.MoveCharacter(characterId, left, top));

    public void BeginCharacterMove() =>
        Post(engine => engine.BeginCharacterMove());

    public void EndCharacterMove() =>
        Post(engine => engine.EndCharacterMove());

    public void ResetCharacterPosition(string characterId) =>
        Post(engine => engine.ResetCharacterPosition(characterId));

    public void HideAll() =>
        Post(engine => engine.HideAll());

    public Task RestoreVisibleCharactersAsync(
        IEnumerable<CharacterConfig> characters,
        bool configMode)
    {
        CharacterConfig[] visible = characters
            .Where(character => character.Visible)
            .ToArray();
        return InvokeAsync(engine =>
            engine.RestoreVisibleCharactersAsync(visible, configMode));
    }

    public void Close()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0)
            return;

        lock (_latestCommandSync)
            _latestCommands.Clear();
        lock (_stateEventSync)
            _pendingStateEvents.Clear();
        _renderThread.Close();
        _snapshots.Clear();
        CharacterScaleChanged = null;
        CharacterAnimationsLoaded = null;
        CharacterLoadFailed = null;
        CharacterStateChanged = null;
        CharactersStateChanged = null;
        CharacterPositionCommitted = null;
        CharacterRightPressed = null;
        CharacterRightReleased = null;
    }

    public void Dispose() => Close();

    internal Task ExecuteOnRenderThreadAsync(Action action) =>
        InvokeAsync(_ =>
        {
            action();
            return Task.CompletedTask;
        });

    private CharacterRenderSnapshot TryGetSnapshot(string characterId) =>
        _snapshots.TryGetValue(
            characterId,
            out CharacterRenderSnapshot? snapshot)
            ? snapshot
            : new CharacterRenderSnapshot(
                characterId,
                false,
                false,
                Array.Empty<string>(),
                MaximumScale,
                DefaultScale);

    private void AttachEngineEvents(NativeCharacterRenderEngine engine)
    {
        engine.CharacterStateChanged += QueueCharacterState;
        engine.CharacterScaleChanged += (id, maximum, current) =>
            PostToUi(() =>
                CharacterScaleChanged?.Invoke(id, maximum, current));
        engine.CharacterAnimationsLoaded += (id, animations) =>
        {
            string[] copy = animations.ToArray();
            PostToUi(() =>
                CharacterAnimationsLoaded?.Invoke(id, copy));
        };
        engine.CharacterLoadFailed += id =>
            PostToUi(() => CharacterLoadFailed?.Invoke(id));
        engine.CharactersStateChanged += () =>
            PostToUi(() => CharactersStateChanged?.Invoke());
        engine.CharacterPositionCommitted += (id, left, top) =>
            PostToUi(() =>
                CharacterPositionCommitted?.Invoke(id, left, top));
        engine.CharacterRightPressed += id =>
            PostToUi(() => CharacterRightPressed?.Invoke(id));
        engine.CharacterRightReleased += id =>
            PostToUi(() => CharacterRightReleased?.Invoke(id));
    }

    private void QueueCharacterState(CharacterRenderSnapshot snapshot)
    {
        _snapshots[snapshot.CharacterId] = snapshot;
        lock (_stateEventSync)
        {
            _pendingStateEvents[snapshot.CharacterId] = snapshot;
            if (_stateDrainScheduled)
                return;

            _stateDrainScheduled = true;
        }

        PostToUi(DrainCharacterStateEvents);
    }

    private void DrainCharacterStateEvents()
    {
        CharacterRenderSnapshot[] pending;
        lock (_stateEventSync)
        {
            pending = _pendingStateEvents.Values.ToArray();
            _pendingStateEvents.Clear();
            _stateDrainScheduled = false;
        }

        foreach (CharacterRenderSnapshot snapshot in pending)
            CharacterStateChanged?.Invoke(snapshot);
    }

    private void QueueLatest(
        string key,
        Action<NativeCharacterRenderEngine> command)
    {
        if (Volatile.Read(ref _closed) != 0)
            return;

        lock (_latestCommandSync)
        {
            _latestCommands[key] = command;
            if (_latestDrainScheduled)
                return;

            _latestDrainScheduled = true;
        }

        _renderThread.Post(DrainLatestCommands);
    }

    private void DrainLatestCommands(NativeCharacterRenderEngine engine)
    {
        Action<NativeCharacterRenderEngine>[] pending;
        lock (_latestCommandSync)
        {
            pending = _latestCommands.Values.ToArray();
            _latestCommands.Clear();
            _latestDrainScheduled = false;
        }

        foreach (Action<NativeCharacterRenderEngine> command in pending)
            command(engine);
    }

    private void Post(Action<NativeCharacterRenderEngine> command)
    {
        if (Volatile.Read(ref _closed) == 0)
            _renderThread.Post(command);
    }

    private Task InvokeAsync(
        Func<NativeCharacterRenderEngine, Task> command) =>
        Volatile.Read(ref _closed) == 0
            ? _renderThread.InvokeAsync(command)
            : Task.CompletedTask;

    private Task<T> InvokeAsync<T>(
        Func<NativeCharacterRenderEngine, Task<T>> command) =>
        Volatile.Read(ref _closed) == 0
            ? _renderThread.InvokeAsync(command)
            : Task.FromResult(default(T)!);

    private void PostToUi(Action action)
    {
        if (Volatile.Read(ref _closed) != 0 ||
            _uiDispatcher.HasShutdownStarted)
        {
            return;
        }

        try
        {
            _ = _uiDispatcher.BeginInvoke(
                DispatcherPriority.DataBind,
                new Action(() =>
                {
                    if (Volatile.Read(ref _closed) == 0)
                        action();
                }));
        }
        catch (InvalidOperationException)
        {
            // UI shutdown won the race with this event.
        }
    }
}
