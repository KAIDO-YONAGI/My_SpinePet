using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Windows;
using SpinePet.Infrastructure;
using SpinePet.Infrastructure.Import;
using SpinePet.Models;
using SpinePet.Rendering;
using SpinePet.Rendering.Native;

namespace SpinePet.Services;

public sealed class CharacterManager
{
    private readonly ConfigService _configService;
    private readonly CharacterIdentityService _identityService;
    [SuppressMessage(
        "Performance",
        "CA1859:Use concrete types when possible for improved performance",
        Justification = "The renderer backend is intentionally isolated behind this contract.")]
    private readonly ICharacterRenderHost _renderHost;
    private readonly CharacterResourceCoordinator _resourceCoordinator;
    private readonly Rect? _workArea;
    private readonly AppConfig _config;
    private readonly object _showSync = new();
    private readonly Dictionary<string, ShowFlight> _showFlights =
        new(StringComparer.Ordinal);
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
        _configService = configService;
        _identityService = identityService ?? new CharacterIdentityService();
        _resourceCoordinator = new CharacterResourceCoordinator(
            _identityService);
        _workArea = workArea;
        _config = configService.Load();
        _renderHost = renderHost ?? new NativeCharacterRenderHost();
        _renderHost.SetRenderDragEnabled(_config.Global.AllowRenderDrag);
        _renderHost.SetTargetFrameRate(_config.Global.TargetFrameRate);
        _renderHost.CharacterScaleChanged += OnCharacterScaleChanged;
        _renderHost.CharacterAnimationsLoaded += OnCharacterAnimationsLoaded;
        _renderHost.CharacterLoadFailed += OnCharacterLoadFailed;
        _renderHost.CharactersStateChanged += OnCharactersStateChanged;
        _renderHost.CharacterPositionCommitted += OnCharacterPositionCommitted;
        _renderHost.CharacterRightClicked += OnCharacterRightClicked;
    }

    public IReadOnlyList<CharacterConfig> Characters => _config.Characters;

    public bool AllowRenderDrag => _config.Global.AllowRenderDrag;

    public int TargetFrameRate => _config.Global.TargetFrameRate;

    public int LibraryThumbnailScalePercent =>
        _config.Global.LibraryThumbnailScalePercent;

    public event Action? CharactersChanged;

    public event Action<string, double, double>? CharacterScaleChanged;

    public event Action<string, double, double>? CharacterPositionChanged;

    public event Action<string>? CharacterRightClicked;

    public ICharacterRenderHost RenderHost => _renderHost;

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

    public void SetAllowRenderDrag(bool allow)
    {
        if (_config.Global.AllowRenderDrag == allow)
        {
            return;
        }

        _config.Global.AllowRenderDrag = allow;
        _renderHost.SetRenderDragEnabled(allow);
        _configService.Save(_config);
    }

    public void SetTargetFrameRate(int frameRate)
    {
        int normalized = GlobalConfig.NormalizeTargetFrameRate(frameRate);
        if (_config.Global.TargetFrameRate == normalized)
        {
            return;
        }

        _config.Global.TargetFrameRate = normalized;
        _renderHost.SetTargetFrameRate(normalized);
        _configService.Save(_config);
    }

    public void SetLibraryThumbnailScale(int percent)
    {
        int normalized =
            GlobalConfig.NormalizeLibraryThumbnailScale(percent);
        if (_config.Global.LibraryThumbnailScalePercent == normalized)
        {
            return;
        }

        _config.Global.LibraryThumbnailScalePercent = normalized;
        _configService.Save(_config);
    }

    public bool AddCharacter(CharacterResourceFiles resources)
    {
        EnsureStandingResources(resources);

        CharacterConfig? character = _config.Characters.FirstOrDefault(character =>
            string.Equals(
                character.SkeletonPath,
                resources.SkeletonPath,
                StringComparison.OrdinalIgnoreCase));
        character ??= _resourceCoordinator.FindPreferredCharacter(
            _config.Characters,
            resources);

        if (character != null)
        {
            CharacterIdentity currentIdentity = GetCharacterIdentity(character);
            bool shouldUpdatePaths =
                !CharacterResourceCoordinator.ResourcesExist(character) ||
                IsSameSkin(
                    currentIdentity,
                    resources.Identity);
            bool changed = SetIfDifferent(
                character.Name,
                resources.Identity.DisplayName,
                value => character.Name = value);
            if (shouldUpdatePaths)
            {
                changed |= UpdateCharacterResources(character, resources);
            }

            if (changed)
            {
                _configService.Save(_config);
                CharactersChanged?.Invoke();
            }

            return false;
        }

        character = CreateCharacter(resources);

        _config.Characters.Add(character);
        _configService.Save(_config);
        CharactersChanged?.Invoke();
        return true;
    }

    public CharacterIdentity GetCharacterIdentity(CharacterConfig character)
    {
        return _identityService.Resolve(
            character.SkeletonPath,
            character.Name);
    }

    public string GetCharacterThumbnailPath(CharacterConfig character)
    {
        CharacterIdentity identity = GetCharacterIdentity(character);
        return CharacterIconService.GetThumbnailPath(character, identity);
    }

    public Task ShowCharacterAsync(CharacterConfig character)
    {
        ArgumentNullException.ThrowIfNull(character);
        if (_closed ||
            (character.Visible &&
             _renderHost.IsCharacterVisible(character.Id) &&
             !_renderHost.IsCharacterLoading(character.Id)))
        {
            return Task.CompletedTask;
        }

        (Task<bool> task, bool ownsSideEffects) =
            GetOrStartShowFlight(character);
        return CompleteShowAsync(task, ownsSideEffects);
    }

    private async Task CompleteShowAsync(
        Task<bool> task,
        bool ownsSideEffects)
    {
        bool changed = await task;
        if (ownsSideEffects && changed && !_closed)
        {
            _configService.Save(_config);
            CharactersChanged?.Invoke();
        }
    }

    public async Task SwitchCharacterResourcesAsync(
        CharacterConfig character,
        CharacterResourceFiles resources)
    {
        EnsureStandingResources(resources);
        SpineSkeletonCompatibility.EnsureSupported(
            resources.SkeletonPath);

        if (!string.Equals(
            _resourceCoordinator.GetGroupKey(character),
            _resourceCoordinator.GetGroupKey(resources),
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The selected resources belong to a different character.");
        }

        if (CharacterResourceCoordinator.ResourcesMatch(
                character,
                resources))
        {
            if (SetIfDifferent(
                    character.Name,
                    resources.Identity.DisplayName,
                    value => character.Name = value))
            {
                _configService.Save(_config);
                CharactersChanged?.Invoke();
            }

            return;
        }

        bool wasVisible = character.Visible;
        string previousName = character.Name;
        string previousSkeletonPath = character.SkeletonPath;
        string previousAtlasPath = character.AtlasPath;
        string previousTexturePath = character.TexturePath;
        List<string> previousAdditionalTexturePaths =
            character.AdditionalTexturePaths.ToList();
        string previousAnimation = character.ConfiguredAnimation;
        bool previousRequiresStandingMigration =
            character.RequiresStandingMigration;
        if (wasVisible)
        {
            _renderHost.RemoveCharacter(character.Id);
        }

        character.ConfiguredAnimation = string.Empty;
        UpdateCharacterResources(character, resources);

        try
        {
            if (wasVisible)
            {
                await EnsureCharacterShownAsync(character);
            }
        }
        catch
        {
            _renderHost.RemoveCharacter(character.Id);
            character.Name = previousName;
            character.SkeletonPath = previousSkeletonPath;
            character.AtlasPath = previousAtlasPath;
            character.TexturePath = previousTexturePath;
            character.AdditionalTexturePaths =
                previousAdditionalTexturePaths;
            character.ConfiguredAnimation = previousAnimation;
            character.RequiresStandingMigration =
                previousRequiresStandingMigration;
            character.Visible = wasVisible;

            if (wasVisible)
            {
                try
                {
                    await EnsureCharacterShownAsync(character);
                }
                catch (Exception rollbackException)
                {
                    AppLogger.Write(
                        nameof(CharacterManager),
                        $"resource-switch-rollback-failed " +
                        $"id={character.Id} " +
                        $"message={rollbackException.Message}");
                }
            }

            throw;
        }
        finally
        {
            _configService.Save(_config);
            CharactersChanged?.Invoke();
        }
    }

    public CharacterResourceSynchronizationResult SynchronizeResources(
        IEnumerable<CharacterResourceFiles> resources,
        string managedRoot)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentException.ThrowIfNullOrWhiteSpace(managedRoot);

        CharacterResourceSynchronizationPlan plan =
            _resourceCoordinator.CalculateSynchronization(
                _config.Characters,
                resources,
                managedRoot);
        int addedCount = 0;
        int updatedCount = 0;
        int removedCount = 0;
        int mergedCount = 0;

        foreach (CharacterRemovalPlan removal in plan.Removals)
        {
            RemoveCharacterFromConfiguration(removal.Character);
            if (removal.IsMerge)
            {
                mergedCount++;
            }
            else
            {
                removedCount++;
            }
        }

        foreach (CharacterUpdatePlan update in plan.Updates)
        {
            UpdateCharacterResources(update.Character, update.Resources);
            if (update.ClearConfiguredAnimation)
            {
                update.Character.ConfiguredAnimation = string.Empty;
            }
            updatedCount++;
        }

        foreach (CharacterResourceFiles addition in plan.Additions)
        {
            _config.Characters.Add(CreateCharacter(addition));
            addedCount++;
        }

        CharacterResourceSynchronizationResult result = new(
            addedCount,
            updatedCount,
            removedCount,
            mergedCount);
        if (result.HasChanges || _config.RequiresRewrite)
        {
            _configService.Save(_config);
        }

        if (result.HasChanges)
        {
            CharactersChanged?.Invoke();
        }

        return result;
    }

    private (Task<bool> Task, bool Owner) GetOrStartShowFlight(
        CharacterConfig character)
    {
        string resourceKey = GetResourceKey(character);
        lock (_showSync)
        {
            if (_showFlights.TryGetValue(
                    character.Id,
                    out ShowFlight? existing) &&
                string.Equals(
                    existing.ResourceKey,
                    resourceKey,
                    StringComparison.OrdinalIgnoreCase))
            {
                return (existing.Task, false);
            }

            Task<bool> task = existing == null
                ? EnsureCharacterShownAsync(character)
                : ShowAfterAsync(existing.Task, character);
            _showFlights[character.Id] = new ShowFlight(resourceKey, task);
            _ = task.ContinueWith(
                _ => RemoveShowFlight(character.Id, task),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            return (task, true);
        }
    }

    private async Task<bool> ShowAfterAsync(
        Task<bool> previous,
        CharacterConfig character)
    {
        try
        {
            await previous;
        }
        catch
        {
            // A newer resource request is independent of the previous load.
        }

        return await EnsureCharacterShownAsync(character);
    }

    private async Task<bool> EnsureCharacterShownAsync(
        CharacterConfig character)
    {
        if (_closed ||
            (character.Visible &&
             _renderHost.IsCharacterVisible(character.Id) &&
             !_renderHost.IsCharacterLoading(character.Id)))
        {
            return false;
        }

        bool wasVisible = character.Visible;
        character.Visible = true;
        try
        {
            await _renderHost.ShowCharacterAsync(
                character,
                _isConfigMode,
                character.AnimationSpeed);
            if (_closed)
            {
                return false;
            }

            if (!_config.Characters.Contains(character))
            {
                _renderHost.RemoveCharacter(character.Id);
                return false;
            }

            if (!character.Visible)
            {
                if (_renderHost.IsCharacterVisible(character.Id) ||
                    _renderHost.IsCharacterLoading(character.Id))
                {
                    _renderHost.HideCharacter(character.Id);
                }

                return false;
            }

            return true;
        }
        catch
        {
            if (!_closed && character.Visible)
            {
                character.Visible = wasVisible;
                _renderHost.HideCharacter(character.Id);
            }

            throw;
        }
    }

    private void RemoveShowFlight(string characterId, Task<bool> task)
    {
        lock (_showSync)
        {
            if (_showFlights.TryGetValue(
                    characterId,
                    out ShowFlight? current) &&
                ReferenceEquals(current.Task, task))
            {
                _showFlights.Remove(characterId);
            }
        }
    }

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
        _renderHost.HideCharacter(character.Id);
        _configService.Save(_config);
        CharactersChanged?.Invoke();
    }

    public void UnloadCharacter(CharacterConfig character)
    {
        ArgumentNullException.ThrowIfNull(character);
        if (_closed)
        {
            return;
        }
        _renderHost.RemoveCharacter(character.Id);
    }

    public void RemoveCharacter(CharacterConfig character)
    {
        ArgumentNullException.ThrowIfNull(character);
        if (_closed || !_config.Characters.Contains(character))
        {
            return;
        }

        character.Visible = false;
        _renderHost.RemoveCharacter(character.Id);
        _config.Characters.Remove(character);
        _configService.Save(_config);
        CharactersChanged?.Invoke();
    }

    public void HideAll()
    {
        if (_closed ||
            !_config.Characters.Any(character =>
                character.Visible ||
                _renderHost.IsCharacterVisible(character.Id) ||
                _renderHost.IsCharacterLoading(character.Id)))
        {
            return;
        }

        foreach (CharacterConfig character in _config.Characters)
        {
            character.Visible = false;
        }

        _renderHost.HideAll();
        _configService.Save(_config);
        CharactersChanged?.Invoke();
    }

    public async Task ShowAllAsync()
    {
        if (_closed)
        {
            return;
        }

        List<Task<bool>> tasks = [];
        foreach (CharacterConfig character in _config.Characters)
        {
            if (character.Visible &&
                _renderHost.IsCharacterVisible(character.Id) &&
                !_renderHost.IsCharacterLoading(character.Id))
            {
                continue;
            }

            (Task<bool> task, bool ownsSideEffects) =
                GetOrStartShowFlight(character);
            if (ownsSideEffects)
            {
                tasks.Add(task);
            }
        }

        if (tasks.Count == 0)
        {
            return;
        }

        bool[] changes = await Task.WhenAll(tasks);
        if (changes.Any(changed => changed) && !_closed)
        {
            _configService.Save(_config);
            CharactersChanged?.Invoke();
        }
    }

    public void ResetAllSettings()
    {
        Rect workArea = _workArea ?? SystemParameters.WorkArea;
        double defaultPositionX = workArea.Left + workArea.Width / 2;
        double defaultPositionY = workArea.Bottom - 24;

        foreach (CharacterConfig character in _config.Characters)
        {
            IReadOnlyList<string> animationNames =
                _renderHost.GetAnimationNames(character.Id);
            string? idleAnimation =
                NativeAnimationController.SelectIdleAnimationName(
                    animationNames);

            character.Scale = CharacterConfig.DefaultScale;
            character.ScaleBasePercent =
                CharacterConfig.DefaultScaleBasePercent;
            character.ScaleMultiplier =
                CharacterConfig.DefaultScaleMultiplier;
            character.AnimationSpeed =
                CharacterConfig.DefaultAnimationSpeed;
            character.ConfiguredAnimation = idleAnimation ?? string.Empty;
            character.PositionX = defaultPositionX;
            character.PositionY = defaultPositionY;

            _renderHost.SetCharacterScale(
                character.Id,
                CharacterConfig.DefaultScale);
            _renderHost.SetCharacterSpeed(
                character.Id,
                CharacterConfig.DefaultAnimationSpeed);
            if (idleAnimation != null)
            {
                _renderHost.PlayCharacterAnimation(
                    character.Id,
                    idleAnimation,
                    repeat: true);
            }

            _renderHost.ResetCharacterPosition(character.Id);
        }

        _configService.Save(_config);
        CharactersChanged?.Invoke();
    }

    public void SaveAllState()
    {
        _configService.Save(_config);
    }

    public async Task RestoreAllAsync(bool configMode)
    {
        int modeVersion = _configModeVersion;
        if (modeVersion == 0)
        {
            _isConfigMode = configMode;
        }

        await _renderHost.RestoreVisibleCharactersAsync(_config.Characters, configMode);
        _renderHost.SetConfigMode(
            modeVersion == _configModeVersion ? configMode : _isConfigMode);
        CharactersChanged?.Invoke();
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
        _renderHost.CharactersStateChanged -= OnCharactersStateChanged;
        _renderHost.CharacterPositionCommitted -= OnCharacterPositionCommitted;
        _renderHost.CharacterRightClicked -= OnCharacterRightClicked;
        _renderHost.Close();
    }

    private void OnCharacterScaleChanged(
        string characterId,
        double maximumScale,
        double currentScale)
    {
        if (_closed)
        {
            return;
        }

        CharacterConfig? character =
            _config.Characters.FirstOrDefault(item => item.Id == characterId);
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

        CharacterConfig? character =
            _config.Characters.FirstOrDefault(item => item.Id == characterId);
        if (character != null &&
            string.IsNullOrWhiteSpace(character.ConfiguredAnimation) &&
            animations.Count > 0)
        {
            character.ConfiguredAnimation =
                NativeAnimationController.SelectIdleAnimationName(animations)
                ?? animations[0];
        }

        CharactersChanged?.Invoke();
    }

    private void OnCharacterLoadFailed(string characterId)
    {
        if (_closed)
        {
            return;
        }

        CharacterConfig? character =
            _config.Characters.FirstOrDefault(item => item.Id == characterId);
        if (character != null)
        {
            character.Visible = false;
        }

        CharactersChanged?.Invoke();
    }

    private void OnCharactersStateChanged()
    {
        if (_closed)
        {
            return;
        }

        CharactersChanged?.Invoke();
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

        CharacterConfig? character =
            _config.Characters.FirstOrDefault(item => item.Id == characterId);
        if (character == null)
        {
            return;
        }

        character.PositionX = left;
        character.PositionY = top;
        _ = _configService.SaveAsync(_config);
        CharacterPositionChanged?.Invoke(characterId, left, top);
    }

    private void OnCharacterRightClicked(string characterId)
    {
        if (_closed)
        {
            return;
        }

        CharacterRightClicked?.Invoke(characterId);
    }

    private void RemoveCharacterFromConfiguration(CharacterConfig character)
    {
        _renderHost.RemoveCharacter(character.Id);
        _config.Characters.Remove(character);
    }

    private CharacterConfig CreateCharacter(
        CharacterResourceFiles resources)
    {
        Rect workArea = _workArea ?? SystemParameters.WorkArea;
        return new CharacterConfig
        {
            Name = resources.Identity.DisplayName,
            SkeletonPath = resources.SkeletonPath,
            AtlasPath = resources.AtlasPath,
            TexturePath = resources.PrimaryTexturePath,
            AdditionalTexturePaths = resources.AdditionalTexturePaths.ToList(),
            PositionX = workArea.Left + workArea.Width / 2,
            PositionY = workArea.Bottom - 24
        };
    }

    private static bool IsSameSkin(
        CharacterIdentity leftIdentity,
        CharacterIdentity rightIdentity)
    {
        return string.Equals(
            leftIdentity.SkinCode,
            rightIdentity.SkinCode,
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool UpdateCharacterResources(
        CharacterConfig character,
        CharacterResourceFiles resources)
    {
        bool changed = false;
        changed |= SetIfDifferent(
            character.Name,
            resources.Identity.DisplayName,
            value => character.Name = value);
        changed |= SetIfDifferent(
            character.SkeletonPath,
            resources.SkeletonPath,
            value => character.SkeletonPath = value);
        changed |= SetIfDifferent(
            character.AtlasPath,
            resources.AtlasPath,
            value => character.AtlasPath = value);
        changed |= SetIfDifferent(
            character.TexturePath,
            resources.PrimaryTexturePath,
            value => character.TexturePath = value);
        if (!character.AdditionalTexturePaths.SequenceEqual(
            resources.AdditionalTexturePaths,
            StringComparer.OrdinalIgnoreCase))
        {
            character.AdditionalTexturePaths =
                resources.AdditionalTexturePaths.ToList();
            changed = true;
        }

        if (character.RequiresStandingMigration)
        {
            character.RequiresStandingMigration = false;
            changed = true;
        }

        return changed;
    }

    private static void EnsureStandingResources(
        CharacterResourceFiles resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        if (!string.Equals(
                resources.ResourceType,
                CharacterResourceTypes.Standing,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Only standing character resources are supported.");
        }
    }

    private static bool SetIfDifferent(
        string currentValue,
        string newValue,
        Action<string> setter)
    {
        if (string.Equals(
            currentValue,
            newValue,
            StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        setter(newValue);
        return true;
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

    private sealed record ShowFlight(string ResourceKey, Task<bool> Task);
}
