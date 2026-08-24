using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Spine;
using SpinePet.Infrastructure.Import;
using SpinePet.Models;

namespace SpinePet.Services;

public sealed class CharacterCatalogBattleImportService
{
    private static readonly Regex ResourcePrefixPattern = new(
        @"^c(?<character>\d+)(?=[_.])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    private readonly CharacterIdentityService _identityService;
    private readonly CharacterResourceDiscoveryService _discovery;
    private readonly Action<string> _validateSkeleton;
    private readonly Action<CharacterResourceFiles> _validateResource;

    public CharacterCatalogBattleImportService(
        CharacterIdentityService? identityService = null,
        CharacterResourceDiscoveryService? discovery = null)
        : this(
            identityService,
            discovery,
            path => _ = SpineSkeletonCompatibility.EnsureSupported(path),
            ValidateReadable)
    {
    }

    internal CharacterCatalogBattleImportService(
        CharacterIdentityService? identityService,
        CharacterResourceDiscoveryService? discovery,
        Action<string> validateSkeleton,
        Action<CharacterResourceFiles>? validateResource = null)
    {
        _identityService = identityService ?? new CharacterIdentityService();
        _discovery = discovery ??
            new CharacterResourceDiscoveryService(_identityService);
        _validateSkeleton = validateSkeleton;
        _validateResource = validateResource ?? (_ => { });
    }

    public CharacterCatalogBattleImportResult Import(
        string sourceRoot,
        string destinationRoot,
        IReadOnlyCollection<string> sourceNames,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationRoot);

        string resolvedSourceRoot = Path.GetFullPath(sourceRoot);
        string resolvedDestinationRoot = Path.GetFullPath(destinationRoot);
        if (!Directory.Exists(resolvedSourceRoot))
        {
            throw new DirectoryNotFoundException(
                $"Character catalog was not found: {resolvedSourceRoot}");
        }

        Directory.CreateDirectory(resolvedDestinationRoot);
        CharacterCatalogAudit audit = Audit(resolvedSourceRoot, sourceNames);
        CharacterResourceFiles[] targetStanding =
            DiscoverTargetStanding(resolvedDestinationRoot);

        int importedBattleCount = 0;
        int addedCharacterCount = 0;
        int alreadyPresentCount = 0;
        List<CharacterCatalogSkippedEntry> skipped =
            audit.SkippedEntries.ToList();

        using CharacterImportTransaction transaction =
            new(resolvedDestinationRoot);
        foreach (CharacterCatalogBattleSet set in audit.CompleteSets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CharacterResourceFiles? existingStanding = FindTargetStanding(
                set,
                targetStanding,
                resolvedDestinationRoot);
            bool addCharacter = existingStanding == null;
            string targetCharacterCode;
            string skinDirectory;
            if (existingStanding != null)
            {
                targetCharacterCode =
                    existingStanding.Identity.CharacterCode;
                skinDirectory = GetSkinDirectory(existingStanding);
            }
            else
            {
                targetCharacterCode = ResolveNewCharacterCode(
                    set.Standing.Identity);
                string displayName = ResolveNewDisplayName(
                    targetCharacterCode,
                    set.SourceName);
                skinDirectory = Path.Combine(
                    resolvedDestinationRoot,
                    SanitizeSegment(displayName),
                    SanitizeSegment(set.Standing.Identity.SkinCode));
                foreach (string resourceType in CharacterResourceTypes.All)
                {
                    transaction.CreateDirectory(
                        Path.Combine(skinDirectory, resourceType));
                }

                CopyResource(
                    set.Standing,
                    Path.Combine(
                        skinDirectory,
                        CharacterResourceTypes.Standing),
                    set.Standing.Identity.CharacterCode,
                    targetCharacterCode,
                    transaction,
                    cancellationToken);
                addedCharacterCount++;
            }

            bool aimPresent = ImportStateIfMissing(
                set.Aim,
                Path.Combine(skinDirectory, CharacterResourceTypes.Aim),
                set.Standing.Identity.CharacterCode,
                targetCharacterCode,
                transaction,
                cancellationToken);
            bool coverPresent = ImportStateIfMissing(
                set.Cover,
                Path.Combine(skinDirectory, CharacterResourceTypes.Cover),
                set.Standing.Identity.CharacterCode,
                targetCharacterCode,
                transaction,
                cancellationToken);
            if (aimPresent && coverPresent)
            {
                alreadyPresentCount++;
            }
            else
            {
                importedBattleCount++;
            }

            if (addCharacter)
            {
                string skeletonName = GetDestinationFileName(
                    set.Standing.SkeletonPath,
                    set.Standing.Identity.CharacterCode,
                    targetCharacterCode);
                CharacterResourceFiles? importedStanding =
                    _discovery.DiscoverForSkeleton(
                        Path.Combine(
                            skinDirectory,
                            CharacterResourceTypes.Standing,
                            skeletonName),
                        CharacterResourceTypes.Standing);
                if (importedStanding == null)
                {
                    throw new InvalidDataException(
                        $"Imported standing resources could not be read: " +
                        $"{set.SourceName}");
                }

                targetStanding =
                    [.. targetStanding, importedStanding];
            }
        }

        transaction.Complete();
        return new CharacterCatalogBattleImportResult(
            audit.SourceDirectoryCount,
            audit.CompleteSets.Count,
            importedBattleCount,
            addedCharacterCount,
            alreadyPresentCount,
            skipped);
    }

    public CharacterCatalogAudit Audit(
        string sourceRoot,
        IReadOnlyCollection<string> sourceNames)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRoot);
        ArgumentNullException.ThrowIfNull(sourceNames);
        if (sourceNames.Count == 0)
        {
            throw new ArgumentException(
                "At least one source directory must be specified.",
                nameof(sourceNames));
        }

        string resolvedSourceRoot = Path.GetFullPath(sourceRoot);
        if (!Directory.Exists(resolvedSourceRoot))
        {
            throw new DirectoryNotFoundException(
                $"Character catalog was not found: {resolvedSourceRoot}");
        }

        string[] sourceDirectories = ResolveSourceDirectories(
            resolvedSourceRoot,
            sourceNames);
        List<CharacterCatalogBattleSet> completeSets = [];
        List<CharacterCatalogSkippedEntry> skippedEntries = [];
        foreach (string sourceDirectory in sourceDirectories)
        {
            Dictionary<string, List<string>> candidates =
                CharacterResourceTypes.Renderable.ToDictionary(
                    state => state,
                    _ => new List<string>(),
                    StringComparer.OrdinalIgnoreCase);
            foreach (string skeletonPath in Directory
                         .EnumerateFiles(
                             sourceDirectory,
                             "*.skel",
                             SearchOption.AllDirectories)
                         .OrderBy(
                             path => path,
                             StringComparer.OrdinalIgnoreCase))
            {
                string? state = ResolveSourceState(
                    sourceDirectory,
                    skeletonPath);
                if (state != null)
                {
                    candidates[state].Add(skeletonPath);
                }
            }

            CharacterResourceFiles? standing = SelectValidResource(
                candidates[CharacterResourceTypes.Standing],
                CharacterResourceTypes.Standing);
            CharacterResourceFiles? aim = SelectValidResource(
                candidates[CharacterResourceTypes.Aim],
                CharacterResourceTypes.Aim);
            CharacterResourceFiles? cover = SelectValidResource(
                candidates[CharacterResourceTypes.Cover],
                CharacterResourceTypes.Cover);
            string sourceName = Path.GetFileName(
                Path.TrimEndingDirectorySeparator(sourceDirectory));
            if (standing == null || aim == null || cover == null)
            {
                skippedEntries.Add(new CharacterCatalogSkippedEntry(
                    sourceName,
                    standing != null,
                    aim != null,
                    cover != null));
                continue;
            }

            completeSets.Add(new CharacterCatalogBattleSet(
                sourceName,
                standing,
                aim,
                cover));
        }

        return new CharacterCatalogAudit(
            sourceDirectories.Length,
            completeSets,
            skippedEntries);
    }

    private static string[] ResolveSourceDirectories(
        string sourceRoot,
        IEnumerable<string> sourceNames)
    {
        string[] names = sourceNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (names.Length == 0)
        {
            throw new ArgumentException(
                "At least one source directory must be specified.",
                nameof(sourceNames));
        }

        List<string> directories = [];
        foreach (string name in names)
        {
            if (Path.IsPathRooted(name) ||
                name.Contains(Path.DirectorySeparatorChar) ||
                name.Contains(Path.AltDirectorySeparatorChar) ||
                name is "." or "..")
            {
                throw new ArgumentException(
                    $"Source selection must be a direct directory name: '{name}'.",
                    nameof(sourceNames));
            }

            string directory = Path.Combine(sourceRoot, name);
            if (!Directory.Exists(directory))
            {
                throw new DirectoryNotFoundException(
                    $"Selected character directory was not found: {directory}");
            }

            directories.Add(directory);
        }

        return [.. directories];
    }

    private CharacterResourceFiles? SelectValidResource(
        IEnumerable<string> skeletonPaths,
        string state)
    {
        foreach (string skeletonPath in skeletonPaths)
        {
            CharacterResourceFiles? resource =
                _discovery.DiscoverForSkeleton(skeletonPath, state);
            if (resource == null)
            {
                continue;
            }

            try
            {
                _validateSkeleton(resource.SkeletonPath);
                _validateResource(resource);
                return resource;
            }
            catch (InvalidDataException)
            {
                // A catalog may retain an older Spine export beside its
                // supported replacement. Continue looking deterministically.
            }
        }

        return null;
    }

    private CharacterResourceFiles[] DiscoverTargetStanding(string root) =>
        Directory
            .EnumerateFiles(root, "*.skel", SearchOption.AllDirectories)
            .Where(path =>
            {
                string? directory = Path.GetDirectoryName(path);
                return directory != null &&
                    string.Equals(
                        Path.GetFileName(
                            Path.TrimEndingDirectorySeparator(directory)),
                        CharacterResourceTypes.Standing,
                        StringComparison.OrdinalIgnoreCase);
            })
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => SelectValidResource(
                [path],
                CharacterResourceTypes.Standing))
            .Where(resource => resource != null)
            .Cast<CharacterResourceFiles>()
            .ToArray();

    private static CharacterResourceFiles? FindTargetStanding(
        CharacterCatalogBattleSet set,
        IEnumerable<CharacterResourceFiles> targetStanding,
        string destinationRoot)
    {
        CharacterResourceFiles[] resources = targetStanding.ToArray();
        CharacterResourceFiles[] exactDirectoryMatches = resources
            .Where(resource => string.Equals(
                GetTopLevelDirectoryName(
                    resource.SkeletonPath,
                    destinationRoot),
                set.SourceName,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (exactDirectoryMatches.Length > 0)
        {
            return SelectClosestIdentity(
                exactDirectoryMatches,
                set.Standing.Identity);
        }

        string transformedCode =
            ResolveTransformedCharacterCode(set.Standing.Identity);
        CharacterResourceFiles? transformedMatch = resources.FirstOrDefault(
            resource =>
                string.Equals(
                    resource.Identity.CharacterCode,
                    transformedCode,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    resource.Identity.SkinCode,
                    set.Standing.Identity.SkinCode,
                    StringComparison.OrdinalIgnoreCase));
        if (transformedMatch != null)
        {
            return transformedMatch;
        }

        return resources.FirstOrDefault(resource =>
            string.Equals(
                resource.Identity.CharacterCode,
                set.Standing.Identity.CharacterCode,
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                resource.Identity.SkinCode,
                set.Standing.Identity.SkinCode,
                StringComparison.OrdinalIgnoreCase));
    }

    private static CharacterResourceFiles SelectClosestIdentity(
        IEnumerable<CharacterResourceFiles> resources,
        CharacterIdentity sourceIdentity) =>
        resources
            .OrderBy(resource => !string.Equals(
                resource.Identity.SkinCode,
                sourceIdentity.SkinCode,
                StringComparison.OrdinalIgnoreCase))
            .ThenBy(
                resource => resource.SkeletonPath,
                StringComparer.OrdinalIgnoreCase)
            .First();

    private string ResolveNewCharacterCode(CharacterIdentity identity)
    {
        string transformed = ResolveTransformedCharacterCode(identity);
        if (_identityService.TryGetCharacterName(transformed, out _))
        {
            return transformed;
        }

        return identity.CharacterCode;
    }

    private static string ResolveTransformedCharacterCode(
        CharacterIdentity identity)
    {
        if (string.IsNullOrWhiteSpace(identity.CharacterCode) ||
            string.IsNullOrWhiteSpace(identity.SkinCode) ||
            string.Equals(
                identity.SkinCode,
                "00",
                StringComparison.OrdinalIgnoreCase))
        {
            return identity.CharacterCode;
        }

        return int.TryParse(
            identity.SkinCode,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out _)
                ? $"{identity.CharacterCode}{identity.SkinCode}"
                : identity.CharacterCode;
    }

    private string ResolveNewDisplayName(
        string characterCode,
        string sourceName) =>
        _identityService.TryGetCharacterName(characterCode, out string name)
            ? name
            : sourceName;

    private bool ImportStateIfMissing(
        CharacterResourceFiles source,
        string destination,
        string sourceCharacterCode,
        string targetCharacterCode,
        CharacterImportTransaction transaction,
        CancellationToken cancellationToken)
    {
        CharacterResourceFiles? existing = Directory.Exists(destination)
            ? Directory
                .EnumerateFiles(destination, "*.skel")
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(path => _discovery.DiscoverForSkeleton(
                    path,
                    source.ResourceType))
                .FirstOrDefault(resource => resource != null)
            : null;
        if (existing != null)
        {
            return true;
        }

        if (Directory.Exists(destination) &&
            Directory.EnumerateFileSystemEntries(destination).Any())
        {
            if (CanReplaceKnownResourceSet(
                    source,
                    destination,
                    sourceCharacterCode,
                    targetCharacterCode))
            {
                ReplaceKnownResourceSet(
                    source,
                    destination,
                    sourceCharacterCode,
                    targetCharacterCode,
                    cancellationToken);
                return false;
            }

            throw new IOException(
                $"Battle resource directory is incomplete and was not " +
                $"overwritten: {destination}");
        }

        transaction.CreateDirectory(destination);
        CopyResource(
            source,
            destination,
            sourceCharacterCode,
            targetCharacterCode,
            transaction,
            cancellationToken);
        return false;
    }

    private static void CopyResource(
        CharacterResourceFiles resource,
        string destination,
        string sourceCharacterCode,
        string targetCharacterCode,
        CharacterImportTransaction transaction,
        CancellationToken cancellationToken)
    {
        transaction.CreateDirectory(destination);
        foreach (string sourcePath in EnumerateResourceFiles(resource))
        {
            string destinationName = GetDestinationFileName(
                sourcePath,
                sourceCharacterCode,
                targetCharacterCode);
            string destinationPath = Path.Combine(
                destination,
                destinationName);
            if (File.Exists(destinationPath))
            {
                throw new IOException(
                    $"Import destination already contains {destinationName}.");
            }

            transaction.CopyFile(
                sourcePath,
                destinationPath,
                cancellationToken);
            if (string.Equals(
                    Path.GetExtension(destinationPath),
                    ".atlas",
                    StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(sourceCharacterCode) &&
                !string.IsNullOrWhiteSpace(targetCharacterCode) &&
                !string.Equals(
                    sourceCharacterCode,
                    targetCharacterCode,
                    StringComparison.OrdinalIgnoreCase))
            {
                RewriteAtlasPageNames(
                    destinationPath,
                    sourceCharacterCode,
                    targetCharacterCode);
            }
        }
    }

    private static bool CanReplaceKnownResourceSet(
        CharacterResourceFiles resource,
        string destination,
        string sourceCharacterCode,
        string targetCharacterCode)
    {
        string[] expectedNames = EnumerateResourceFiles(resource)
            .Select(path => GetDestinationFileName(
                path,
                sourceCharacterCode,
                targetCharacterCode))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        string[] actualNames = Directory
            .EnumerateFiles(destination)
            .Select(path => Path.GetFileName(path)!)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return expectedNames.SequenceEqual(
            actualNames,
            StringComparer.OrdinalIgnoreCase);
    }

    private static void ReplaceKnownResourceSet(
        CharacterResourceFiles resource,
        string destination,
        string sourceCharacterCode,
        string targetCharacterCode,
        CancellationToken cancellationToken)
    {
        foreach (string sourcePath in EnumerateResourceFiles(resource))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string destinationPath = Path.Combine(
                destination,
                GetDestinationFileName(
                    sourcePath,
                    sourceCharacterCode,
                    targetCharacterCode));
            File.Copy(sourcePath, destinationPath, overwrite: true);
            if (string.Equals(
                    Path.GetExtension(destinationPath),
                    ".atlas",
                    StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(sourceCharacterCode) &&
                !string.IsNullOrWhiteSpace(targetCharacterCode) &&
                !string.Equals(
                    sourceCharacterCode,
                    targetCharacterCode,
                    StringComparison.OrdinalIgnoreCase))
            {
                RewriteAtlasPageNames(
                    destinationPath,
                    sourceCharacterCode,
                    targetCharacterCode);
            }
        }
    }

    private static IEnumerable<string> EnumerateResourceFiles(
        CharacterResourceFiles resource) =>
        new[]
        {
            resource.SkeletonPath,
            resource.AtlasPath,
            resource.PrimaryTexturePath
        }
        .Concat(resource.AdditionalTexturePaths)
        .Distinct(StringComparer.OrdinalIgnoreCase);

    private static string GetDestinationFileName(
        string sourcePath,
        string sourceCharacterCode,
        string targetCharacterCode)
    {
        string fileName = Path.GetFileName(sourcePath);
        if (string.IsNullOrWhiteSpace(sourceCharacterCode) ||
            string.IsNullOrWhiteSpace(targetCharacterCode) ||
            string.Equals(
                sourceCharacterCode,
                targetCharacterCode,
                StringComparison.OrdinalIgnoreCase))
        {
            return fileName;
        }

        Match match = ResourcePrefixPattern.Match(fileName);
        return match.Success &&
            string.Equals(
                match.Groups["character"].Value,
                sourceCharacterCode,
                StringComparison.OrdinalIgnoreCase)
                ? $"c{targetCharacterCode}{fileName[match.Length..]}"
                : fileName;
    }

    private static void RewriteAtlasPageNames(
        string atlasPath,
        string sourceCharacterCode,
        string targetCharacterCode)
    {
        string text = File.ReadAllText(atlasPath);
        string pattern =
            $@"(?m)^(?<indent>\s*)c{Regex.Escape(sourceCharacterCode)}(?=[_.])";
        string replacement = $"${{indent}}c{targetCharacterCode}";
        File.WriteAllText(
            atlasPath,
            Regex.Replace(
                text,
                pattern,
                replacement,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            Utf8NoBom);
    }

    private static string? ResolveSourceState(
        string sourceDirectory,
        string skeletonPath)
    {
        string? skeletonDirectory = Path.GetDirectoryName(skeletonPath);
        if (skeletonDirectory == null)
        {
            return null;
        }

        string relativeDirectory = Path.GetRelativePath(
            sourceDirectory,
            skeletonDirectory);
        if (string.Equals(relativeDirectory, ".", StringComparison.Ordinal))
        {
            return CharacterResourceTypes.Standing;
        }

        string[] segments = relativeDirectory.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        foreach (string segment in segments)
        {
            string? exact = CharacterResourceTypes.Renderable
                .FirstOrDefault(state => string.Equals(
                    segment,
                    state,
                    StringComparison.OrdinalIgnoreCase));
            if (exact != null)
            {
                return exact;
            }
        }

        return null;
    }

    private static string GetSkinDirectory(
        CharacterResourceFiles standing)
    {
        string? standingDirectory =
            Path.GetDirectoryName(standing.SkeletonPath);
        string? skinDirectory = standingDirectory == null
            ? null
            : Path.GetDirectoryName(standingDirectory);
        return skinDirectory ??
            throw new InvalidDataException(
                $"Standing resource has no skin directory: " +
                $"{standing.SkeletonPath}");
    }

    private static string GetTopLevelDirectoryName(
        string path,
        string root)
    {
        string relative = Path.GetRelativePath(root, path);
        string? first = relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return first ?? string.Empty;
    }

    private static string SanitizeSegment(string value)
    {
        string sanitized = string.Concat(value.Select(character =>
            Path.GetInvalidFileNameChars().Contains(character)
                ? '_'
                : character)).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "Unknown" : sanitized;
    }

    private static void ValidateReadable(CharacterResourceFiles resource)
    {
        try
        {
            Atlas atlas = new(resource.AtlasPath, new NoopTextureLoader());
            try
            {
                _ = Path.GetExtension(resource.SkeletonPath)
                    .Equals(".json", StringComparison.OrdinalIgnoreCase)
                        ? new SkeletonJson(atlas).ReadSkeletonData(
                            resource.SkeletonPath)
                        : new SkeletonBinary(atlas).ReadSkeletonData(
                            resource.SkeletonPath);
            }
            finally
            {
                atlas.Dispose();
            }
        }
        catch (Exception exception)
        {
            throw new InvalidDataException(
                $"Spine resource cannot be loaded: {resource.SkeletonPath}",
                exception);
        }
    }

    private sealed class NoopTextureLoader : TextureLoader
    {
        public void Load(AtlasPage page, string path) { }
        public void Unload(object texture) { }
    }
}

public sealed record CharacterCatalogBattleSet(
    string SourceName,
    CharacterResourceFiles Standing,
    CharacterResourceFiles Aim,
    CharacterResourceFiles Cover);

public sealed record CharacterCatalogSkippedEntry(
    string SourceName,
    bool HasStanding,
    bool HasAim,
    bool HasCover);

public sealed record CharacterCatalogAudit(
    int SourceDirectoryCount,
    IReadOnlyList<CharacterCatalogBattleSet> CompleteSets,
    IReadOnlyList<CharacterCatalogSkippedEntry> SkippedEntries);

public sealed record CharacterCatalogBattleImportResult(
    int SourceDirectoryCount,
    int CompleteSetCount,
    int ImportedBattleCount,
    int AddedCharacterCount,
    int AlreadyPresentCount,
    IReadOnlyList<CharacterCatalogSkippedEntry> SkippedEntries);
