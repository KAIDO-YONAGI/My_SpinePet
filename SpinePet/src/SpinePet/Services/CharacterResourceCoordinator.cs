using System.IO;
using SpinePet.Models;

namespace SpinePet.Services;

internal sealed class CharacterResourceCoordinator(
    CharacterIdentityService identityService)
{
    public CharacterResourceSynchronizationPlan CalculateSynchronization(
        IReadOnlyList<CharacterConfig> characters,
        IEnumerable<CharacterResourceFiles> resources,
        string managedRoot)
    {
        ArgumentNullException.ThrowIfNull(characters);
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentException.ThrowIfNullOrWhiteSpace(managedRoot);

        string fullManagedRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(managedRoot));
        Dictionary<string, List<CharacterResourceFiles>> catalog = resources
            .Where(resource =>
                resource != null &&
                string.Equals(
                    resource.ResourceType,
                    CharacterResourceTypes.Standing,
                    StringComparison.OrdinalIgnoreCase))
            .GroupBy(GetGroupKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.ToList(),
                StringComparer.OrdinalIgnoreCase);
        var existingGroups = characters
            .Select((character, index) => new
            {
                Character = character,
                Index = index,
                Key = GetGroupKey(character)
            })
            .GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        HashSet<string> existingKeys = existingGroups
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        List<CharacterRemovalPlan> removals = [];
        List<CharacterUpdatePlan> updates = [];
        List<CharacterResourceFiles> additions = [];

        foreach (var group in existingGroups)
        {
            CharacterConfig[] existing = group
                .OrderBy(item => item.Index)
                .Select(item => item.Character)
                .ToArray();
            CharacterResourceFiles[] standingResources = catalog
                .GetValueOrDefault(group.Key, [])
                .ToArray();

            if (standingResources.Length == 0)
            {
                CharacterConfig[] staleCharacters = existing
                    .Where(character =>
                        IsPathWithinRoot(
                            character.SkeletonPath,
                            fullManagedRoot) ||
                        character.RequiresStandingMigration ||
                        !ResourcesExist(character))
                    .ToArray();
                removals.AddRange(staleCharacters.Select(character =>
                    new CharacterRemovalPlan(character, IsMerge: false)));

                CharacterConfig[] externalCharacters = existing
                    .Except(staleCharacters)
                    .ToArray();
                if (externalCharacters.Length > 1)
                {
                    CharacterConfig retained =
                        SelectPreferredCharacter(externalCharacters);
                    removals.AddRange(externalCharacters
                        .Where(character =>
                            !ReferenceEquals(character, retained))
                        .Select(character =>
                            new CharacterRemovalPlan(
                                character,
                                IsMerge: true)));
                }

                continue;
            }

            CharacterConfig retainedCharacter =
                SelectPreferredCharacter(existing);
            removals.AddRange(existing
                .Where(character =>
                    !ReferenceEquals(character, retainedCharacter))
                .Select(character =>
                    new CharacterRemovalPlan(character, IsMerge: true)));

            CharacterResourceFiles selectedResources = SelectResources(
                retainedCharacter,
                GetIdentity(retainedCharacter),
                standingResources);
            bool selectionChanged =
                !ResourcesMatch(retainedCharacter, selectedResources);
            bool requiresUpdate =
                selectionChanged ||
                !string.Equals(
                    retainedCharacter.Name,
                    selectedResources.Identity.DisplayName,
                    StringComparison.OrdinalIgnoreCase) ||
                retainedCharacter.RequiresStandingMigration;
            if (requiresUpdate)
            {
                updates.Add(new CharacterUpdatePlan(
                    retainedCharacter,
                    selectedResources,
                    ClearConfiguredAnimation:
                        selectionChanged &&
                        !string.IsNullOrEmpty(
                            retainedCharacter.ConfiguredAnimation)));
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

            CharacterResourceFiles? selected = variants
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
            if (selected != null)
            {
                additions.Add(selected);
            }
        }

        return new CharacterResourceSynchronizationPlan(
            removals,
            updates,
            additions,
            SeedFirstCharacterVisible:
                characters.Count == 0 && additions.Count > 0);
    }

    public string GetGroupKey(CharacterConfig character) =>
        GetGroupKey(GetIdentity(character), character.SkeletonPath);

    public string GetGroupKey(CharacterResourceFiles resources) =>
        GetGroupKey(resources.Identity, resources.SkeletonPath);

    public CharacterConfig? FindPreferredCharacter(
        IReadOnlyList<CharacterConfig> characters,
        CharacterResourceFiles resources)
    {
        string key = GetGroupKey(resources);
        CharacterConfig[] matches = characters
            .Where(character => string.Equals(
                GetGroupKey(character),
                key,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return matches.Length == 0
            ? null
            : SelectPreferredCharacter(matches);
    }

    public static bool ResourcesMatch(
        CharacterConfig character,
        CharacterResourceFiles resources) =>
        string.Equals(
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

    private CharacterIdentity GetIdentity(CharacterConfig character) =>
        identityService.Resolve(character.SkeletonPath, character.Name);

    private static string GetGroupKey(
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
        CharacterConfig[] characters) =>
        characters.FirstOrDefault(character => character.Visible) ??
        characters[0];

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
        return selected ?? OrderResources(
                resources,
                character.SkeletonPath)
            .First();
    }

    private static IOrderedEnumerable<CharacterResourceFiles> OrderResources(
        IEnumerable<CharacterResourceFiles> resources,
        string preferredSkeletonPath) =>
        resources
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

    private static bool IsSameSkin(
        CharacterIdentity left,
        CharacterIdentity right) =>
        string.Equals(
            left.SkinCode,
            right.SkinCode,
            StringComparison.OrdinalIgnoreCase);

    public static bool ResourcesExist(CharacterConfig character) =>
        File.Exists(character.SkeletonPath) &&
        File.Exists(character.AtlasPath) &&
        File.Exists(character.TexturePath) &&
        character.AdditionalTexturePaths.All(File.Exists);

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
                !string.Equals(relativePath, "..", StringComparison.Ordinal) &&
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
}

internal sealed record CharacterResourceSynchronizationPlan(
    IReadOnlyList<CharacterRemovalPlan> Removals,
    IReadOnlyList<CharacterUpdatePlan> Updates,
    IReadOnlyList<CharacterResourceFiles> Additions,
    bool SeedFirstCharacterVisible);

internal sealed record CharacterRemovalPlan(
    CharacterConfig Character,
    bool IsMerge);

internal sealed record CharacterUpdatePlan(
    CharacterConfig Character,
    CharacterResourceFiles Resources,
    bool ClearConfiguredAnimation);
