using System.Diagnostics.CodeAnalysis;
using System.Windows;
using SpinePet.Models;
using SpinePet.Rendering;
using SpinePet.Rendering.Native;

namespace SpinePet.Services;

/// <summary>
/// Composition facade over the catalog, show coordinator and battle
/// interaction controller. Public members must keep their exact signatures
/// and observable behavior; the manager itself only orchestrates
/// cross-component operations and relays renderer events.
/// </summary>
public sealed class CharacterManager
{
    [SuppressMessage(
        "Performance",
        "CA1859:Use concrete types when possible for improved performance",
        Justification = "The renderer backend is intentionally isolated behind this contract.")]
    private readonly ICharacterRenderHost _renderHost;
    private readonly CharacterCatalog _catalog;
    private readonly CharacterShowCoordinator _shows;
    private readonly BattleInteractionController _battle;
    private bool _isConfigMode;
    private int _configModeVersion;
    private bool _closed;

    public CharacterManager(
        ConfigService configService,
        CharacterIdentityService? identityService = null,
        ICharacterRenderHost? renderHost = null)
        : this(configService, identityService, renderHost, null)
    {
    }

    internal CharacterManager(
        ConfigService configService,
        CharacterIdentityService? identityService,
        ICharacterRenderHost? renderHost,
        Rect? workArea)
    {
        _renderHost = renderHost ?? new NativeCharacterRenderHost();
        CharacterCatalog? catalog = null;
        CharacterShowCoordinator? shows = null;
        _catalog = catalog = new CharacterCatalog(
            configService,
            identityService ?? new CharacterIdentityService(),
            _renderHost,
            workArea,
            character => shows!.EnsureShownAsync(character));
        _shows = shows = new CharacterShowCoordinator(
            _renderHost,
            () => _closed,
            () => _isConfigMode,
            character => catalog!.Contains(character),
            () => catalog!.Save());
        _battle = new BattleInteractionController(
            _renderHost,
            EnsureVisibleAsync,
            () => catalog!.BattleRules,
            id => catalog!.FindById(id));
        _catalog.CharactersChanged += () => CharactersChanged?.Invoke();
        _battle.CharacterBattleStateChanged += OnBattleStateChanged;
        _battle.CharacterRightClicked += OnBattleRightClicked;
        _renderHost.CharacterScaleChanged += OnCharacterScaleChanged;
        _renderHost.CharacterAnimationsLoaded += OnCharacterAnimationsLoaded;
        _renderHost.CharacterLoadFailed += OnCharacterLoadFailed;
        _renderHost.CharacterStateChanged += OnCharacterStateChanged;
        _renderHost.CharacterPositionCommitted += OnCharacterPositionCommitted;
        _renderHost.CharacterRightPressed += OnCharacterRightPressed;
        _renderHost.CharacterRightReleased += OnCharacterRightReleased;
    }

    public IReadOnlyList<CharacterConfig> Characters => _catalog.Characters;

    public bool AllowRenderDrag => _catalog.AllowRenderDrag;

    public int TargetFrameRate => _catalog.TargetFrameRate;

    public int LibraryThumbnailScalePercent =>
        _catalog.LibraryThumbnailScalePercent;

    public BattleRulesConfig BattleRules => _catalog.BattleRules;

    public event Action? CharactersChanged;

    public event Action<string, double, double>? CharacterScaleChanged;

    public event Action<CharacterRenderSnapshot>? CharacterStateChanged;

    public event Action<string, double, double>? CharacterPositionChanged;

    public event Action<string>? CharacterRightClicked;

    public event Action<string, string, string>? CharacterBattleStateChanged;

    public ICharacterRenderHost RenderHost => _renderHost;

    public string GetCharacterDisplayMode(string characterId) =>
        _battle.GetDisplayMode(characterId);

    public string GetCharacterBattleState(string characterId) =>
        _battle.GetBattleState(characterId);

    public Task<bool> SetCharacterDisplayModeAsync(
        CharacterConfig character,
        string mode) =>
        _battle.SetDisplayModeAsync(character, mode);

    public Task<bool> SetCharacterBattleStateAsync(
        CharacterConfig character,
        string battleState) =>
        _battle.SetBattleStateAsync(character, battleState);

    public void SetConfigMode(bool configMode)
    {
        if (_closed || _isConfigMode == configMode)
        {
            return;
        }

        _isConfigMode = configMode;
        _configModeVersion++;
        _renderHost.SetConfigMode(configMode);
    }

    public void SetAllowRenderDrag(bool allow) =>
        _catalog.SetAllowRenderDrag(allow);

    public void SetTargetFrameRate(int frameRate) =>
        _catalog.SetTargetFrameRate(frameRate);

    public void SetLibraryThumbnailScale(int percent) =>
        _catalog.SetLibraryThumbnailScale(percent);

    public bool AddCharacter(CharacterResourceFiles resources) =>
        _catalog.AddCharacter(resources);

    public CharacterIdentity GetCharacterIdentity(CharacterConfig character) =>
        _catalog.GetCharacterIdentity(character);

    public string GetCharacterThumbnailPath(CharacterConfig character) =>
        _catalog.GetCharacterThumbnailPath(character);

    public Task ShowCharacterAsync(CharacterConfig character) =>
        _shows.ShowAsync(character);

    public Task SwitchCharacterResourcesAsync(
        CharacterConfig character,
        CharacterResourceFiles resources) =>
        _catalog.SwitchCharacterResourcesAsync(character, resources);

    public CharacterResourceSynchronizationResult SynchronizeResources(
        IEnumerable<CharacterResourceFiles> resources,
        string managedRoot) =>
        _catalog.SynchronizeResources(resources, managedRoot);

    public void HideCharacter(CharacterConfig character)
    {
        ArgumentNullException.ThrowIfNull(character);
        if (_closed ||
            (!character.Visible &&
             !_renderHost.IsCharacterVisible(character.Id) &&
             !_renderHost.IsCharacterLoading(character.Id)))
        {
            return;
        }

        character.Visible = false;
        _battle.ResetRuntime(character.Id);
        _renderHost.HideCharacter(character.Id);
        _catalog.Save();
    }

    public void UnloadCharacter(CharacterConfig character)
    {
        ArgumentNullException.ThrowIfNull(character);
        if (_closed)
        {
            return;
        }
        _battle.ResetRuntime(character.Id);
        _renderHost.RemoveCharacter(character.Id);
    }

    public void RemoveCharacter(CharacterConfig character)
    {
        ArgumentNullException.ThrowIfNull(character);
        if (_closed || !_catalog.Contains(character))
        {
            return;
        }

        character.Visible = false;
        _battle.ResetRuntime(character.Id);
        _catalog.RemoveFromConfiguration(character);
        _catalog.Save();
        CharactersChanged?.Invoke();
    }

    public void HideAll()
    {
        if (_closed ||
            !_catalog.Characters.Any(character =>
                character.Visible ||
                _renderHost.IsCharacterVisible(character.Id) ||
                _renderHost.IsCharacterLoading(character.Id)))
        {
            return;
        }

        foreach (CharacterConfig character in _catalog.Characters)
        {
            character.Visible = false;
            _battle.ResetRuntime(character.Id);
        }

        _renderHost.HideAll();
        _catalog.Save();
    }

    public Task ShowAllAsync() =>
        _shows.ShowAllAsync(_catalog.Characters);

    public void ResetAllSettings() =>
        _catalog.ResetAllSettings();

    public void SaveAllState() =>
        _catalog.Save();

    public async Task RestoreAllAsync(bool configMode)
    {
        int modeVersion = _configModeVersion;
        if (modeVersion == 0)
        {
            _isConfigMode = configMode;
        }

        await _renderHost.RestoreVisibleCharactersAsync(
            _catalog.Characters, configMode);
        _renderHost.SetConfigMode(
            modeVersion == _configModeVersion ? configMode : _isConfigMode);
    }

    public bool IsCharacterLoading(string characterId) =>
        _renderHost.IsCharacterLoading(characterId);

    public void Close()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        _renderHost.CharacterScaleChanged -= OnCharacterScaleChanged;
        _renderHost.CharacterAnimationsLoaded -= OnCharacterAnimationsLoaded;
        _renderHost.CharacterLoadFailed -= OnCharacterLoadFailed;
        _renderHost.CharacterStateChanged -= OnCharacterStateChanged;
        _renderHost.CharacterPositionCommitted -= OnCharacterPositionCommitted;
        _renderHost.CharacterRightPressed -= OnCharacterRightPressed;
        _renderHost.CharacterRightReleased -= OnCharacterRightReleased;
        _battle.Close();
        _renderHost.Close();
    }

    private Task EnsureVisibleAsync(CharacterConfig character) =>
        ShowCharacterAsync(character);

    private void OnBattleStateChanged(
        string characterId,
        string mode,
        string battleState) =>
        CharacterBattleStateChanged?.Invoke(characterId, mode, battleState);

    private void OnBattleRightClicked(string characterId) =>
        CharacterRightClicked?.Invoke(characterId);

    private void OnCharacterScaleChanged(
        string characterId,
        double maximumScale,
        double currentScale)
    {
        if (_closed)
        {
            return;
        }

        CharacterConfig? character = _catalog.FindById(characterId);
        if (character != null)
        {
            character.Scale = currentScale;
        }

        CharacterScaleChanged?.Invoke(characterId, maximumScale, currentScale);
    }

    private void OnCharacterAnimationsLoaded(
        string characterId,
        IReadOnlyList<string> animations)
    {
        if (_closed)
        {
            return;
        }

        CharacterConfig? character = _catalog.FindById(characterId);
        if (character != null &&
            string.IsNullOrWhiteSpace(character.ConfiguredAnimation) &&
            animations.Count > 0)
        {
            character.ConfiguredAnimation =
                NativeAnimationController.SelectIdleAnimationName(animations)
                ?? animations[0];
        }

    }

    private void OnCharacterLoadFailed(string characterId)
    {
        if (_closed)
        {
            return;
        }

        CharacterConfig? character = _catalog.FindById(characterId);
        if (character != null)
        {
            character.Visible = false;
        }

    }

    private void OnCharacterStateChanged(
        CharacterRenderSnapshot snapshot)
    {
        if (_closed)
        {
            return;
        }

        CharacterConfig? character = _catalog.FindById(snapshot.CharacterId);
        if (character != null)
        {
            character.Visible = snapshot.IsVisible;
            character.Scale = snapshot.CurrentScale;
        }

        CharacterStateChanged?.Invoke(snapshot);
    }

    private void OnCharacterPositionCommitted(
        string characterId,
        double left,
        double top)
    {
        if (_closed)
        {
            return;
        }

        CharacterConfig? character = _catalog.FindById(characterId);
        if (character == null)
        {
            return;
        }

        character.PositionX = left;
        character.PositionY = top;
        _ = _catalog.SaveAsync();
        CharacterPositionChanged?.Invoke(characterId, left, top);
    }

    private void OnCharacterRightPressed(string characterId)
    {
        if (_closed)
            return;
        _battle.HandleRightPressed(characterId);
    }

    private void OnCharacterRightReleased(string characterId)
    {
        if (_closed)
            return;
        _battle.HandleRightReleased(characterId);
    }
}
