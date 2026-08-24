using SpinePet.Models;

namespace SpinePet.Rendering;

public interface ICharacterRenderHost
{
    event Action<string, double, double>? CharacterScaleChanged;
    event Action<string, IReadOnlyList<string>>? CharacterAnimationsLoaded;
    event Action<string>? CharacterLoadFailed;
    event Action<CharacterRenderSnapshot>? CharacterStateChanged;
    event Action? CharactersStateChanged;
    event Action<string, double, double>? CharacterPositionCommitted;
    event Action<string>? CharacterRightPressed;
    event Action<string>? CharacterRightReleased;

    bool IsCharacterLoading(string characterId);
    bool IsCharacterVisible(string characterId);
    IReadOnlyList<string> GetAnimationNames(string characterId);
    double GetMaxScale(string characterId);
    double GetCurrentScale(string characterId);
    Task InitializeAsync();
    Task ShowCharacterAsync(CharacterConfig character, bool configMode, double speed);
    void HideCharacter(string characterId);
    void RemoveCharacter(string characterId);
    void SetCharacterScale(string characterId, double scale);
    void SetCharacterSpeed(string characterId, double speed);
    void PlayCharacterAnimation(string characterId, string animation, bool repeat);
    void PlayCharacterAnimationSequence(
        string characterId,
        IReadOnlyList<string> animations,
        string? restoreAnimation,
        bool loopLast,
        IReadOnlyList<CharacterBattleLayerConfig>? battleLayers = null);
    Task PreloadBattleResourcesAsync(CharacterConfig character);
    Task<bool> SetCharacterResourceStateAsync(
        string characterId,
        string resourceState,
        string? idleAnimation);
    void SetConfigMode(bool configMode);
    void SetRenderDragEnabled(bool enabled);
    void SetTargetFrameRate(int frameRate);
    void MoveCharacter(string characterId, double left, double top);
    void BeginCharacterMove();
    void EndCharacterMove();
    void ResetCharacterPosition(string characterId);
    void HideAll();
    Task RestoreVisibleCharactersAsync(
        IEnumerable<CharacterConfig> characters,
        bool configMode);
    void Close();
}
