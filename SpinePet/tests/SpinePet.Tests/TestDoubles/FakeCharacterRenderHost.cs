using SpinePet.Models;
using SpinePet.Rendering;

namespace SpinePet.Tests.TestDoubles;

internal sealed class FakeCharacterRenderHost : ICharacterRenderHost
{
    public List<string> RemovedCharacterIds { get; } = [];
    public List<string> ShownSkeletonPaths { get; } = [];
    public HashSet<string> VisibleCharacterIds { get; } =
        new(StringComparer.Ordinal);
    public HashSet<string> LoadingCharacterIds { get; } =
        new(StringComparer.Ordinal);

    public Func<CharacterConfig, Task>? ShowCharacterHandler { get; set; }
    public Action<string, double>? SetCharacterScaleHandler { get; set; }
    public int HideCharacterCount { get; private set; }
    public int HideAllCount { get; private set; }
    public int ConfigModeSetCount { get; private set; }
    public int CloseCount { get; private set; }
    public bool ConfigMode { get; private set; }

    public int TargetFrameRate { get; private set; } =
        GlobalConfig.DefaultTargetFrameRate;
    public int TargetFrameRateSetCount { get; private set; }

    private Action<string, double, double>? _characterScaleChanged;
    private Action<string, IReadOnlyList<string>>? _characterAnimationsLoaded;
    private Action<string>? _characterLoadFailed;
    private Action? _charactersStateChanged;
    private Action<string, double, double>? _characterPositionCommitted;
    private Action<string>? _characterRightClicked;

    public int EventSubscriptionCount =>
        GetSubscriberCount(_characterScaleChanged) +
        GetSubscriberCount(_characterAnimationsLoaded) +
        GetSubscriberCount(_characterLoadFailed) +
        GetSubscriberCount(_charactersStateChanged) +
        GetSubscriberCount(_characterPositionCommitted) +
        GetSubscriberCount(_characterRightClicked);

    public event Action<string, double, double>? CharacterScaleChanged
    {
        add => _characterScaleChanged += value;
        remove => _characterScaleChanged -= value;
    }
    public event Action<string, IReadOnlyList<string>>? CharacterAnimationsLoaded
    {
        add => _characterAnimationsLoaded += value;
        remove => _characterAnimationsLoaded -= value;
    }
    public event Action<string>? CharacterLoadFailed
    {
        add => _characterLoadFailed += value;
        remove => _characterLoadFailed -= value;
    }
    public event Action? CharactersStateChanged
    {
        add => _charactersStateChanged += value;
        remove => _charactersStateChanged -= value;
    }
    public event Action<string, double, double>? CharacterPositionCommitted
    {
        add => _characterPositionCommitted += value;
        remove => _characterPositionCommitted -= value;
    }
    public event Action<string>? CharacterRightClicked
    {
        add => _characterRightClicked += value;
        remove => _characterRightClicked -= value;
    }

    public bool IsCharacterLoading(string characterId) =>
        LoadingCharacterIds.Contains(characterId);
    public bool IsCharacterVisible(string characterId) =>
        VisibleCharacterIds.Contains(characterId);
    public IReadOnlyList<string> GetAnimationNames(string characterId) => [];
    public double GetMaxScale(string characterId) => 2;
    public double GetCurrentScale(string characterId) => 0.2;
    public Task InitializeAsync() => Task.CompletedTask;
    public async Task ShowCharacterAsync(
        CharacterConfig character,
        bool configMode,
        double speed)
    {
        ShownSkeletonPaths.Add(character.SkeletonPath);
        LoadingCharacterIds.Add(character.Id);
        try
        {
            if (ShowCharacterHandler != null)
            {
                await ShowCharacterHandler(character);
            }
            VisibleCharacterIds.Add(character.Id);
        }
        finally
        {
            LoadingCharacterIds.Remove(character.Id);
        }
    }
    public void HideCharacter(string characterId)
    {
        HideCharacterCount++;
        VisibleCharacterIds.Remove(characterId);
        LoadingCharacterIds.Remove(characterId);
    }
    public void RemoveCharacter(string characterId)
    {
        RemovedCharacterIds.Add(characterId);
        VisibleCharacterIds.Remove(characterId);
        LoadingCharacterIds.Remove(characterId);
    }
    public void SetCharacterScale(string characterId, double scale) =>
        SetCharacterScaleHandler?.Invoke(characterId, scale);
    public void SetCharacterSpeed(string characterId, double speed) { }
    public void PlayCharacterAnimation(
        string characterId,
        string animation,
        bool repeat)
    { }
    public void SetConfigMode(bool configMode)
    {
        ConfigMode = configMode;
        ConfigModeSetCount++;
    }
    public void SetRenderDragEnabled(bool enabled) { }
    public void SetTargetFrameRate(int frameRate)
    {
        TargetFrameRate = GlobalConfig.NormalizeTargetFrameRate(frameRate);
        TargetFrameRateSetCount++;
    }
    public void MoveCharacter(string characterId, double left, double top) { }
    public void BeginCharacterMove() { }
    public void EndCharacterMove() { }
    public void ResetCharacterPosition(string characterId) { }
    public void HideAll()
    {
        HideAllCount++;
        VisibleCharacterIds.Clear();
        LoadingCharacterIds.Clear();
    }
    public Task RestoreVisibleCharactersAsync(
        IEnumerable<CharacterConfig> characters,
        bool configMode) => Task.CompletedTask;
    public void Close() => CloseCount++;

    public void RaiseScaleChanged(
        string characterId,
        double maximumScale,
        double currentScale) =>
        _characterScaleChanged?.Invoke(
            characterId,
            maximumScale,
            currentScale);

    public void RaiseAnimationsLoaded(
        string characterId,
        IReadOnlyList<string> animations) =>
        _characterAnimationsLoaded?.Invoke(characterId, animations);

    public void RaiseLoadFailed(string characterId) =>
        _characterLoadFailed?.Invoke(characterId);

    public void RaiseStateChanged() => _charactersStateChanged?.Invoke();

    public void RaisePositionCommitted(
        string characterId,
        double left,
        double top) =>
        _characterPositionCommitted?.Invoke(characterId, left, top);

    public void RaiseRightClicked(string characterId) =>
        _characterRightClicked?.Invoke(characterId);

    private static int GetSubscriberCount(Delegate? handler) =>
        handler?.GetInvocationList().Length ?? 0;
}
