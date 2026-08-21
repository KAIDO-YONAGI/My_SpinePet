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
    private readonly Rect? _workArea;
    private readonly AppConfig _config;
    private bool _isConfigMode;
    private int _configModeVersion;

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

    public event Action<string>? CharacterRightClicked;

    public ICharacterRenderHost RenderHost => _renderHost;

    public void SetConfigMode(bool configMode)
    {
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
        character ??= FindPreferredCharacter(resources);

        if (character != null)
        {
            CharacterIdentity currentIdentity = GetCharacterIdentity(character);
            bool shouldUpdatePaths =
                !CharacterResourcesExist(character) ||
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

    public async Task ShowCharacterAsync(CharacterConfig character)
    {
        try
        {
            await ShowCharacterCoreAsync(character);
        }
        finally
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
            GetCharacterGroupKey(character),
            GetCharacterGroupKey(resources),
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The selected resources belong to a different character.");
        }

        if (CharacterResourcesMatch(character, resources))
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
                await ShowCharacterCoreAsync(character);
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
                    await ShowCharacterCoreAsync(character);
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

        string fullManagedRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(managedRoot));
        CharacterResourceFiles[] supportedResources = resources
            .Where(resource =>
                resource != null &&
                string.Equals(
                    resource.ResourceType,
                    CharacterResourceTypes.Standing,
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Dictionary<string, List<CharacterResourceFiles>> catalog =
            supportedResources
                .GroupBy(
                    GetCharacterGroupKey,
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.ToList(),
                    StringComparer.OrdinalIgnoreCase);

        var existingGroups = _config.Characters
            .Select((character, index) => new
            {
                Character = character,
                Index = index,
                Key = GetCharacterGroupKey(character)
            })
            .GroupBy(
                item => item.Key,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
        HashSet<string> existingKeys = existingGroups
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        int addedCount = 0;
        int updatedCount = 0;
        int removedCount = 0;
        int mergedCount = 0;
        bool wasEmptyConfiguration = _config.Characters.Count == 0;

        foreach (var group in existingGroups)
        {
            CharacterConfig[] existing = group
                .OrderBy(item => item.Index)
                .Select(item => item.Character)
                .ToArray();
            CharacterResourceFiles[] standingResources = catalog
                .GetValueOrDefault(group.Key, [])
                .Where(resource => string.Equals(
                    resource.ResourceType,
                    CharacterResourceTypes.Standing,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (standingResources.Length == 0)
            {
                CharacterConfig[] staleCharacters = existing
                    .Where(character =>
                        IsPathWithinRoot(
                            character.SkeletonPath,
                            fullManagedRoot) ||
                        character.RequiresStandingMigration ||
                        !CharacterResourcesExist(character))
                    .ToArray();
                foreach (CharacterConfig staleCharacter in staleCharacters)
                {
                    RemoveCharacterFromConfiguration(staleCharacter);
                    removedCount++;
                }

                CharacterConfig[] externalCharacters = existing
                    .Except(staleCharacters)
                    .ToArray();
                if (externalCharacters.Length > 1)
                {
                    CharacterConfig retained =
                        SelectPreferredCharacter(externalCharacters);
                    foreach (CharacterConfig duplicate in
                             externalCharacters.Where(character =>
                                 !ReferenceEquals(character, retained)))
                    {
                        RemoveCharacterFromConfiguration(duplicate);
                        mergedCount++;
                    }
                }

                continue;
            }

            CharacterConfig retainedCharacter =
                SelectPreferredCharacter(existing);
            foreach (CharacterConfig duplicate in existing.Where(character =>
                         !ReferenceEquals(character, retainedCharacter)))
            {
                RemoveCharacterFromConfiguration(duplicate);
                mergedCount++;
            }

            CharacterIdentity currentIdentity =
                GetCharacterIdentity(retainedCharacter);
            CharacterResourceFiles selectedResources = SelectResources(
                retainedCharacter,
                currentIdentity,
                catalog[group.Key]);
            bool selectionChanged =
                !CharacterResourcesMatch(
                    retainedCharacter,
                    selectedResources);
            bool updated = UpdateCharacterResources(
                retainedCharacter,
                selectedResources);
            if (selectionChanged &&
                !string.IsNullOrEmpty(retainedCharacter.ConfiguredAnimation))
            {
                retainedCharacter.ConfiguredAnimation = string.Empty;
                updated = true;
            }
            if (retainedCharacter.RequiresStandingMigration)
            {
                retainedCharacter.RequiresStandingMigration = false;
                updated = true;
            }

            if (updated)
            {
                updatedCount++;
            }
        }

        foreach ((string key, List<CharacterResourceFiles> variants) in catalog
                     .OrderBy(
                         item => item.Key,
                         StringComparer.OrdinalIgnoreCase))
        {
            if (existingKeys.Contains(key))
            {
                continue;
            }

            CharacterResourceFiles? standingResources = variants
                .Where(resource => string.Equals(
                    resource.ResourceType,
                    CharacterResourceTypes.Standing,
                    StringComparison.OrdinalIgnoreCase))
                .OrderBy(
                    resource => resource.Identity.SkinCode,
                    StringComparer.OrdinalIgnoreCase)
                .ThenBy(
                    resource => resource.Identity.ResourceName,
                    StringComparer.OrdinalIgnoreCase)
                .ThenBy(
                    resource => resource.SkeletonPath,
                    StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (standingResources == null)
            {
                continue;
            }

            _config.Characters.Add(CreateCharacter(standingResources));
            addedCount++;
        }

        // A freshly seeded configuration (e.g. the shipped portable package)
        // starts with exactly one visible character instead of showing every
        // discovered resource at once.
        if (wasEmptyConfiguration && _config.Characters.Count > 0)
        {
            for (int index = 0; index < _config.Characters.Count; index++)
            {
                _config.Characters[index].Visible = index == 0;
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

    private async Task ShowCharacterCoreAsync(CharacterConfig character)
    {
        character.Visible = true;
        try
        {
            await _renderHost.ShowCharacterAsync(
                character,
                _isConfigMode,
                character.AnimationSpeed);
        }
        catch
        {
            character.Visible = false;
            _renderHost.HideCharacter(character.Id);
            throw;
        }
    }

    public void HideCharacter(CharacterConfig character)
    {
        character.Visible = false;
        _renderHost.HideCharacter(character.Id);
        _configService.Save(_config);
        CharactersChanged?.Invoke();
    }

    public void UnloadCharacter(CharacterConfig character)
    {
        ArgumentNullException.ThrowIfNull(character);
        _renderHost.RemoveCharacter(character.Id);
    }

    public void RemoveCharacter(CharacterConfig character)
    {
        _renderHost.RemoveCharacter(character.Id);
        _config.Characters.Remove(character);
        _configService.Save(_config);
        CharactersChanged?.Invoke();
    }

    public void HideAll()
    {
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
        try
        {
            await Task.WhenAll(
                _config.Characters.Select(ShowCharacterCoreAsync));
        }
        finally
        {
            _configService.Save(_config);
            CharactersChanged?.Invoke();
        }
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
        _renderHost.Close();
    }

    private void OnCharacterScaleChanged(
        string characterId,
        double maximumScale,
        double currentScale)
    {
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
        CharacterConfig? character =
            _config.Characters.FirstOrDefault(item => item.Id == characterId);
        if (character != null &&
            string.IsNullOrWhiteSpace(character.ConfiguredAnimation) &&
            animations.Count > 0)
        {
            character.ConfiguredAnimation =
                NativeCharacterRenderHost.SelectIdleAnimationName(animations)
                ?? animations[0];
        }

        CharactersChanged?.Invoke();
    }

    private void OnCharacterLoadFailed(string characterId)
    {
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
        CharactersChanged?.Invoke();
    }

    private void OnCharacterPositionCommitted(
        string characterId,
        double left,
        double top)
    {
        CharacterConfig? character =
            _config.Characters.FirstOrDefault(item => item.Id == characterId);
        if (character == null)
        {
            return;
        }

        character.PositionX = left;
        character.PositionY = top;
        _configService.Save(_config);
        CharactersChanged?.Invoke();
    }

    private void OnCharacterRightClicked(string characterId)
    {
        CharacterRightClicked?.Invoke(characterId);
    }

    private CharacterConfig? FindPreferredCharacter(
        CharacterResourceFiles resources)
    {
        string characterKey = GetCharacterGroupKey(resources);
        CharacterConfig[] matches = _config.Characters
            .Where(character => string.Equals(
                GetCharacterGroupKey(character),
                characterKey,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return matches.Length == 0
            ? null
            : SelectPreferredCharacter(matches);
    }

    private string GetCharacterGroupKey(CharacterConfig character)
    {
        return GetCharacterGroupKey(
            GetCharacterIdentity(character),
            character.SkeletonPath);
    }

    private static string GetCharacterGroupKey(
        CharacterResourceFiles resources)
    {
        return GetCharacterGroupKey(
            resources.Identity,
            resources.SkeletonPath);
    }

    private static string GetCharacterGroupKey(
        CharacterIdentity identity,
        string fallbackPath)
    {
        if (!string.IsNullOrWhiteSpace(identity.CharacterCode))
        {
            return $"code:{identity.CharacterCode.Trim()}";
        }

        if (!string.IsNullOrWhiteSpace(identity.ResourceName))
        {
            return $"resource:{identity.ResourceName.Trim()}";
        }

        return $"path:{fallbackPath}";
    }

    private static CharacterConfig SelectPreferredCharacter(
        CharacterConfig[] characters)
    {
        return characters.FirstOrDefault(character => character.Visible) ??
            characters[0];
    }

    private static CharacterResourceFiles SelectResources(
        CharacterConfig character,
        CharacterIdentity currentIdentity,
        IReadOnlyList<CharacterResourceFiles> resources)
    {
        CharacterResourceFiles? selected = OrderResources(
                resources.Where(resource => IsSameSkin(
                    currentIdentity,
                    resource.Identity)),
                character.SkeletonPath)
            .FirstOrDefault();
        selected ??= OrderResources(
                resources.Where(resource => string.Equals(
                    resource.ResourceType,
                    CharacterResourceTypes.Standing,
                    StringComparison.OrdinalIgnoreCase)),
                character.SkeletonPath)
            .First();
        return selected;
    }

    private static IOrderedEnumerable<CharacterResourceFiles> OrderResources(
        IEnumerable<CharacterResourceFiles> resources,
        string preferredSkeletonPath)
    {
        return resources
            .OrderBy(resource => !string.Equals(
                resource.SkeletonPath,
                preferredSkeletonPath,
                StringComparison.OrdinalIgnoreCase))
            .ThenBy(
                resource => resource.Identity.SkinCode,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(
                resource => resource.Identity.ResourceName,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(
                resource => resource.SkeletonPath,
                StringComparer.OrdinalIgnoreCase);
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

    private static bool CharacterResourcesExist(CharacterConfig character)
    {
        return File.Exists(character.SkeletonPath) &&
            File.Exists(character.AtlasPath) &&
            File.Exists(character.TexturePath) &&
            character.AdditionalTexturePaths.All(File.Exists);
    }

    private static bool CharacterResourcesMatch(
        CharacterConfig character,
        CharacterResourceFiles resources)
    {
        return string.Equals(
                character.SkeletonPath,
                resources.SkeletonPath,
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                character.AtlasPath,
                resources.AtlasPath,
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                character.TexturePath,
                resources.PrimaryTexturePath,
                StringComparison.OrdinalIgnoreCase) &&
            character.AdditionalTexturePaths.SequenceEqual(
                resources.AdditionalTexturePaths,
                StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsPathWithinRoot(string path, string root)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            string relativePath = Path.GetRelativePath(
                root,
                Path.GetFullPath(path));
            return !Path.IsPathRooted(relativePath) &&
                !string.Equals(
                    relativePath,
                    "..",
                    StringComparison.Ordinal) &&
                !relativePath.StartsWith(
                    $"..{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal) &&
                !relativePath.StartsWith(
                    $"..{Path.AltDirectorySeparatorChar}",
                    StringComparison.Ordinal);
        }
        catch (Exception exception) when (
            exception is ArgumentException or
                NotSupportedException or
                PathTooLongException)
        {
            return false;
        }
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
}
