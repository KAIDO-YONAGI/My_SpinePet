using System.IO;
using SpinePet.Infrastructure;
using SpinePet.Infrastructure.Import;
using SpinePet.Models;

namespace SpinePet.Services;

public sealed class CharacterResourceDiscoveryService
{
    private readonly CharacterIdentityService _identityService;

    private readonly EnumerationOptions _directoryEnumerationOptions = new()
    {
        IgnoreInaccessible = true,
        MatchCasing = MatchCasing.CaseInsensitive,
        RecurseSubdirectories = false
    };

    public CharacterResourceDiscoveryService(
        CharacterIdentityService? identityService = null)
    {
        _identityService = identityService ?? new CharacterIdentityService();
    }

    public IReadOnlyList<CharacterResourceFiles> Discover(
        string resourceDirectory)
    {
        return DiscoverAll(resourceDirectory)
            .Where(resource => string.Equals(
                resource.ResourceType,
                CharacterResourceTypes.Standing,
                StringComparison.OrdinalIgnoreCase))
            .GroupBy(
                resource => resource.Identity.ResourceName,
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(
                resource => resource.Identity.DisplayName,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(
                resource => resource.Identity.SkinCode,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<CharacterResourceFiles> DiscoverAll(
        string resourceDirectory)
    {
        if (!Directory.Exists(resourceDirectory))
        {
            return [];
        }

        List<CharacterResourceFiles> resources = [];
        foreach (string characterDirectory in EnumerateDirectories(
                     resourceDirectory))
        {
            AddResourcesFromDirectory(
                resources,
                characterDirectory,
                CharacterResourceTypes.Standing);

            foreach (string childDirectory in EnumerateDirectories(
                         characterDirectory))
            {
                string childName = Path.GetFileName(
                    Path.TrimEndingDirectorySeparator(childDirectory));
                if (CharacterResourceTypes.IsRenderable(childName))
                {
                    // Legacy layout: <character>/standing/files.
                    AddResourcesFromDirectory(
                        resources,
                        childDirectory,
                        childName);
                    continue;
                }

                // Current layout: <character>/<skin>/standing/files.
                foreach (string resourceType in
                         CharacterResourceTypes.Renderable)
                {
                    AddResourcesFromDirectory(
                        resources,
                        Path.Combine(childDirectory, resourceType),
                        resourceType);
                }
            }
        }

        return resources
            .DistinctBy(
                resource => Path.GetFullPath(resource.SkeletonPath),
                StringComparer.OrdinalIgnoreCase)
            .OrderBy(
                resource => resource.Identity.DisplayName,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(
                resource => resource.Identity.SkinCode,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(
                resource => resource.ResourceType,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public CharacterResourceFiles? DiscoverForSkeleton(
        string skeletonPath,
        string? resourceType = null)
    {
        if (!File.Exists(skeletonPath) ||
            !string.Equals(
                Path.GetExtension(skeletonPath),
                ".skel",
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string? directory = Path.GetDirectoryName(skeletonPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return null;
        }

        string resolvedType = resourceType ??
            ResolveResourceTypeFromDirectory(directory);
        if (!CharacterResourceTypes.IsRenderable(resolvedType))
        {
            return null;
        }

        return TryCreateForSkeleton(
            skeletonPath,
            CharacterResourceTypes.Standing);
    }

    private IEnumerable<string> EnumerateDirectories(string directory) =>
        Directory
            .EnumerateDirectories(
                directory,
                "*",
                _directoryEnumerationOptions)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);

    private void AddResourcesFromDirectory(
        List<CharacterResourceFiles> resources,
        string directory,
        string resourceType)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (string skeletonPath in Directory
                     .EnumerateFiles(
                         directory,
                         "*.skel",
                         _directoryEnumerationOptions)
                     .OrderBy(
                         path => path,
                         StringComparer.OrdinalIgnoreCase))
        {
            CharacterResourceFiles? resource =
                TryCreateForSkeleton(skeletonPath, resourceType);
            if (resource != null)
            {
                try
                {
                    SpineSkeletonCompatibility.EnsureSupported(
                        resource.SkeletonPath);
                }
                catch (InvalidDataException exception)
                {
                    AppLogger.Write(
                        nameof(CharacterResourceDiscoveryService),
                        $"resource-skipped path={resource.SkeletonPath} " +
                        $"message={exception.Message}");
                    continue;
                }

                resources.Add(resource);
            }
        }
    }

    private CharacterResourceFiles? TryCreateForSkeleton(
        string skeletonPath,
        string resourceType)
    {
        if (!CharacterResourceTypes.IsRenderable(resourceType))
        {
            return null;
        }

        string? directory = Path.GetDirectoryName(skeletonPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return null;
        }

        string skeletonStem = Path.GetFileNameWithoutExtension(skeletonPath);
        CharacterIdentity identity =
            _identityService.Resolve(skeletonPath, string.Empty);
        string atlasPath = Path.Combine(directory, $"{skeletonStem}.atlas");
        if (!File.Exists(atlasPath))
        {
            return null;
        }

        string[] texturePaths = ResolveTexturePaths(
            directory,
            atlasPath,
            identity.ResourceName);
        if (texturePaths.Length == 0)
        {
            return null;
        }

        string fallbackName = GetCharacterDirectoryName(
            directory,
            identity.SkinCode);
        identity = _identityService.Resolve(skeletonPath, fallbackName);

        return new CharacterResourceFiles(
            skeletonPath,
            atlasPath,
            texturePaths[0],
            texturePaths.Skip(1).ToArray(),
            CharacterResourceTypes.Standing,
            identity);
    }

    private string[] ResolveTexturePaths(
        string directory,
        string atlasPath,
        string resourceName)
    {
        try
        {
            string[] atlasPageNames = File
                .ReadLines(atlasPath)
                .Select(line => line.Trim())
                .Where(line => line.EndsWith(
                    ".png",
                    StringComparison.OrdinalIgnoreCase))
                .Where(line => string.Equals(
                    Path.GetFileName(line),
                    line,
                    StringComparison.Ordinal))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (atlasPageNames.Length > 0)
            {
                string[] atlasTextures = atlasPageNames
                    .Select(fileName => Path.Combine(directory, fileName))
                    .ToArray();
                return atlasTextures.All(File.Exists)
                    ? atlasTextures
                    : [];
            }
        }
        catch (IOException)
        {
            // Fall back to the historical resource-name match below.
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }

        return Directory
            .EnumerateFiles(
                directory,
                $"{resourceName}*.png",
                _directoryEnumerationOptions)
            .Where(path => !Path
                .GetFileNameWithoutExtension(path)
                .EndsWith("_icon", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string ResolveResourceTypeFromDirectory(string directory)
    {
        string directoryName =
            Path.GetFileName(Path.TrimEndingDirectorySeparator(directory));
        if (string.Equals(
                directoryName,
                CharacterResourceTypes.Standing,
                StringComparison.OrdinalIgnoreCase))
        {
            return CharacterResourceTypes.Standing;
        }

        // These retired directories can remain on disk, but must not be
        // treated as standing resources when a user browses to a .skel file.
        return directoryName.Equals("aim", StringComparison.OrdinalIgnoreCase) ||
            directoryName.Equals("cover", StringComparison.OrdinalIgnoreCase)
                ? directoryName
                : CharacterResourceTypes.Standing;
    }

    private static string GetCharacterDirectoryName(
        string resourceDirectory,
        string skinCode)
    {
        DirectoryInfo resourceDirectoryInfo = new(resourceDirectory);
        if (!CharacterResourceTypes.IsRenderable(resourceDirectoryInfo.Name) ||
            resourceDirectoryInfo.Parent == null)
        {
            return resourceDirectoryInfo.Name;
        }

        DirectoryInfo parent = resourceDirectoryInfo.Parent;
        if (!string.IsNullOrWhiteSpace(skinCode) &&
            string.Equals(
                parent.Name,
                skinCode,
                StringComparison.OrdinalIgnoreCase) &&
            parent.Parent != null)
        {
            return parent.Parent.Name;
        }

        return parent.Name;
    }
}
