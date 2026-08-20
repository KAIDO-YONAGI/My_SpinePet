using SpinePet.Models;
using SpinePet.Rendering;

namespace SpinePet.Tests.TestDoubles;

internal sealed class FakeCharacterRenderHost : ICharacterRenderHost
{
    public List<string> RemovedCharacterIds { get; } = [];
    public List<string> ShownSkeletonPaths { get; } = [];

    public Func<CharacterConfig, Task>? ShowCharacterHandler { get; set; }

    public int TargetFrameRate { get; private set; } =
        GlobalConfig.DefaultTargetFrameRate;
    public int TargetFrameRateSetCount { get; private set; }

    public event Action<string, double, double>? CharacterScaleChanged;
    public event Action<string, IReadOnlyList<string>>? CharacterAnimationsLoaded;
    public event Action<string>? CharacterLoadFailed;
    public event Action? CharactersStateChanged;
    public event Action<string, double, double>? CharacterPositionCommitted;

    public bool IsCharacterLoading(string characterId) => false;
    public bool IsCharacterVisible(string characterId) => false;
    public IReadOnlyList<string> GetAnimationNames(string characterId) => [];
    public double GetMaxScale(string characterId) => 2;
    public double GetCurrentScale(string characterId) => 0.2;
    public Task InitializeAsync() => Task.CompletedTask;
    public Task ShowCharacterAsync(
        CharacterConfig character,
        bool configMode,
        double speed)
    {
        ShownSkeletonPaths.Add(character.SkeletonPath);
        return ShowCharacterHandler?.Invoke(character) ?? Task.CompletedTask;
    }
    public void HideCharacter(string characterId) { }
    public void RemoveCharacter(string characterId) =>
        RemovedCharacterIds.Add(characterId);
    public void SetCharacterScale(string characterId, double scale) { }
    public void SetCharacterSpeed(string characterId, double speed) { }
    public void PlayCharacterAnimation(
        string characterId,
        string animation,
        bool repeat)
    { }
    public void SetConfigMode(bool configMode) { }
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
    public void HideAll() { }
    public Task RestoreVisibleCharactersAsync(
        IEnumerable<CharacterConfig> characters,
        bool configMode) => Task.CompletedTask;
    public void Close() { }

    public void RaiseScaleChanged(
        string characterId,
        double maximumScale,
        double currentScale) =>
        CharacterScaleChanged?.Invoke(
            characterId,
            maximumScale,
            currentScale);

    public void RaiseAnimationsLoaded(
        string characterId,
        IReadOnlyList<string> animations) =>
        CharacterAnimationsLoaded?.Invoke(characterId, animations);

    public void RaiseLoadFailed(string characterId) =>
        CharacterLoadFailed?.Invoke(characterId);

    public void RaiseStateChanged() => CharactersStateChanged?.Invoke();

    public void RaisePositionCommitted(
        string characterId,
        double left,
        double top) =>
        CharacterPositionCommitted?.Invoke(characterId, left, top);
}
