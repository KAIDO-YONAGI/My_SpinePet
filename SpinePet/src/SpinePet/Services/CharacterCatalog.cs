using System.Windows;
using SpinePet.Infrastructure;
using SpinePet.Infrastructure.Import;
using SpinePet.Models;
using SpinePet.Rendering;
using SpinePet.Rendering.Native;

namespace SpinePet.Services;

/// <summary>
/// Owns the persisted character configuration: catalog additions, resource
/// switches with rollback, resource synchronization, global settings and
/// persistence. Render-facing calls are limited to the operations the
/// original manager performed inline.
/// </summary>
internal sealed class CharacterCatalog
{
    private readonly ConfigService _configService;
    private readonly CharacterIdentityService _identityService;
    private readonly CharacterResourceCoordinator _resourceCoordinator;
    private readonly ICharacterRenderHost _renderHost;
    private readonly Func<CharacterConfig, Task> _ensureShownAsync;
    private readonly Func<CharacterConfig, IReadOnlyList<string>>
        _readAnimationNames;
    private readonly Rect? _workArea;
    private readonly AppConfig _config;

    public CharacterCatalog(
        ConfigService configService,
        CharacterIdentityService identityService,
        ICharacterRenderHost renderHost,
        Rect? workArea,
        Func<CharacterConfig, Task> ensureShownAsync,
        Func<CharacterConfig, IReadOnlyList<string>>? readAnimationNames = null)
    {
        _configService = configService;
        _identityService = identityService;
        _renderHost = renderHost;
        _workArea = workArea;
        _ensureShownAsync = ensureShownAsync;
        _readAnimationNames =
            readAnimationNames ?? CharacterAnimationNameReader.Read;
        _resourceCoordinator = new CharacterResourceCoordinator(
            _identityService);
        _config = configService.Load();
        _renderHost.SetRenderDragEnabled(_config.Global.AllowRenderDrag);
        _renderHost.SetTargetFrameRate(_config.Global.TargetFrameRate);
    }

    public event Action? CharactersChanged;

    public IReadOnlyList<CharacterConfig> Characters => _config.Characters;

    public BattleRulesConfig BattleRules => _config.Global.BattleRules;

    public bool AllowRenderDrag => _config.Global.AllowRenderDrag;

    public int TargetFrameRate => _config.Global.TargetFrameRate;

    public int LibraryThumbnailScalePercent =>
        _config.Global.LibraryThumbnailScalePercent;

    public bool Contains(CharacterConfig character) =>
        _config.Characters.Contains(character);

    public CharacterConfig? FindById(string characterId) =>
        _config.Characters.FirstOrDefault(item => item.Id == characterId);

    public IReadOnlyList<string> GetAnimationNames(
        CharacterConfig character)
    {
        IReadOnlyList<string> loaded =
            _renderHost.GetAnimationNames(character.Id);
        return loaded.Count > 0 ? loaded : _readAnimationNames(character);
    }

    public void Save() => _configService.Save(_config);

    public Task SaveAsync() => _configService.SaveAsync(_config);

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
                await _ensureShownAsync(character);
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
                    await _ensureShownAsync(character);
                }
                catch (Exception rollbackException)
                {
                    AppLogger.Write(
                        nameof(CharacterCatalog),
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
        CharacterResourceFiles[] resourceCatalog = resources.ToArray();

        CharacterResourceSynchronizationPlan plan =
            _resourceCoordinator.CalculateSynchronization(
                _config.Characters,
                resourceCatalog,
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

        foreach (CharacterConfig character in _config.Characters)
        {
            CharacterResourceFiles? standing = resourceCatalog
                .FirstOrDefault(resource =>
                    string.Equals(
                        resource.ResourceType,
                        CharacterResourceTypes.Standing,
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(
                        resource.SkeletonPath,
                        character.SkeletonPath,
                        StringComparison.OrdinalIgnoreCase));
            CharacterBattleConfig? battle = standing == null
                ? null
                : CharacterBattleConfigFactory.TryCreate(
                    standing,
                    resourceCatalog);
            if (!BattleProfilesMatch(character.Battle, battle))
            {
                character.Battle = battle;
                updatedCount++;
            }
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

    public void RemoveFromConfiguration(CharacterConfig character)
    {
        _renderHost.RemoveCharacter(character.Id);
        _config.Characters.Remove(character);
    }

    public void ResetAllSettings()
    {
        Rect workArea = _workArea ?? SystemParameters.WorkArea;
        double defaultPositionX = workArea.Left + workArea.Width / 2;
        double defaultPositionY = workArea.Bottom - 24;

        foreach (CharacterConfig character in _config.Characters)
        {
            IReadOnlyList<string> animationNames =
                GetAnimationNames(character);
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

    private void RemoveCharacterFromConfiguration(CharacterConfig character) =>
        RemoveFromConfiguration(character);

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

    private static bool BattleProfilesMatch(
        CharacterBattleConfig? left,
        CharacterBattleConfig? right)
    {
        if (left == null || right == null)
        {
            return left == right;
        }

        return ResourceProfilesMatch(left.Aim, right.Aim) &&
            ResourceProfilesMatch(left.Cover, right.Cover) &&
            string.Equals(
                left.Animations.AimIdle,
                right.Animations.AimIdle,
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                left.Animations.ToAim,
                right.Animations.ToAim,
                StringComparison.OrdinalIgnoreCase) &&
            BattleLayersMatch(
                left.Animations.AimFireLayers,
                right.Animations.AimFireLayers) &&
            string.Equals(
                left.Animations.CoverIdle,
                right.Animations.CoverIdle,
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                left.Animations.ToCover,
                right.Animations.ToCover,
                StringComparison.OrdinalIgnoreCase) &&
            left.Animations.ReloadSequence.SequenceEqual(
                right.Animations.ReloadSequence,
                StringComparer.OrdinalIgnoreCase);
    }

    private static bool BattleLayersMatch(
        IReadOnlyList<CharacterBattleLayerConfig>? left,
        IReadOnlyList<CharacterBattleLayerConfig>? right)
    {
        IReadOnlyList<CharacterBattleLayerConfig> normalizedLeft =
            left ?? [];
        IReadOnlyList<CharacterBattleLayerConfig> normalizedRight =
            right ?? [];
        if (normalizedLeft.Count != normalizedRight.Count)
        {
            return false;
        }

        for (int index = 0; index < normalizedLeft.Count; index++)
        {
            CharacterBattleLayerConfig leftEffect = normalizedLeft[index];
            CharacterBattleLayerConfig rightEffect =
                normalizedRight[index];
            if (!string.Equals(
                    leftEffect.Animation,
                    rightEffect.Animation,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    CharacterBattleEffectBlendModes.Normalize(
                        leftEffect.Blend),
                    CharacterBattleEffectBlendModes.Normalize(
                        rightEffect.Blend),
                    StringComparison.Ordinal) ||
                leftEffect.Alpha != rightEffect.Alpha ||
                leftEffect.Loop != rightEffect.Loop ||
                !TimelineKeysMatch(
                    leftEffect.IncludeTimelines,
                    rightEffect.IncludeTimelines) ||
                !TimelineKeysMatch(
                    leftEffect.ExcludeTimelines,
                    rightEffect.ExcludeTimelines))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TimelineKeysMatch(
        IReadOnlyList<string>? left,
        IReadOnlyList<string>? right) =>
        (left ?? []).SequenceEqual(
            right ?? [],
            StringComparer.Ordinal);

    private static bool ResourceProfilesMatch(
        CharacterBattleResourceConfig left,
        CharacterBattleResourceConfig right) =>
        string.Equals(
            left.SkeletonPath,
            right.SkeletonPath,
            StringComparison.OrdinalIgnoreCase) &&
        string.Equals(
            left.AtlasPath,
            right.AtlasPath,
            StringComparison.OrdinalIgnoreCase) &&
        string.Equals(
            left.TexturePath,
            right.TexturePath,
            StringComparison.OrdinalIgnoreCase) &&
        left.ExtraTexturePaths.SequenceEqual(
            right.ExtraTexturePaths,
            StringComparer.OrdinalIgnoreCase);

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
}
