using System.IO;
using System.Text.Json;
using SpinePet.Models;

namespace SpinePet.Services;

public sealed class NikkeDbResourceImportService(
    CharacterResourceDiscoveryService discovery)
{
    public NikkeDbImportResult Import(
        string resourceId,
        string nikkedbDirectory,
        string destinationRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        string mapPath = Path.Combine(
            nikkedbDirectory,
            "data",
            "indexes",
            "rename-map.json");
        using JsonDocument document = JsonDocument.Parse(
            File.ReadAllText(mapPath));
        JsonElement entry = document.RootElement
            .GetProperty("entries")
            .EnumerateArray()
            .FirstOrDefault(item =>
                item.GetProperty("id").GetString()?.Equals(
                    resourceId.Trim(),
                    StringComparison.OrdinalIgnoreCase) == true);
        if (entry.ValueKind == JsonValueKind.Undefined)
        {
            throw new InvalidOperationException(
                $"NikkeDB resource ID was not found: {resourceId}");
        }

        string id = entry.GetProperty("id").GetString()!;
        string displayName = entry.GetProperty("displayName").GetString() ?? id;
        string relativePath =
            entry.GetProperty("currentRelativePath").GetString()!;
        string sourceRoot = Path.GetFullPath(
            Path.Combine(nikkedbDirectory, "l2d", relativePath));
        CharacterResourceFiles standing = DiscoverSingle(
            sourceRoot,
            CharacterResourceTypes.Standing);
        CharacterResourceFiles? aim = TryDiscoverSingle(
            Path.Combine(sourceRoot, CharacterResourceTypes.Aim),
            CharacterResourceTypes.Aim);
        CharacterResourceFiles? cover = TryDiscoverSingle(
            Path.Combine(sourceRoot, CharacterResourceTypes.Cover),
            CharacterResourceTypes.Cover);
        bool importBattle = aim != null && cover != null;

        string characterDirectory = SanitizeSegment(displayName);
        string skinDirectory = SanitizeSegment(id);
        string destination = Path.Combine(
            destinationRoot,
            characterDirectory,
            skinDirectory);
        using IDisposable targetLock =
            CharacterImportTargetLock.Acquire(destination, cancellationToken);
        using CharacterImportTransaction transaction =
            new(destinationRoot);

        foreach (string resourceType in CharacterResourceTypes.All)
        {
            transaction.CreateDirectory(
                Path.Combine(destination, resourceType));
        }

        CopyResource(
            standing,
            Path.Combine(destination, CharacterResourceTypes.Standing),
            transaction,
            cancellationToken);
        if (importBattle)
        {
            CopyResource(
                aim!,
                Path.Combine(destination, CharacterResourceTypes.Aim),
                transaction,
                cancellationToken);
            CopyResource(
                cover!,
                Path.Combine(destination, CharacterResourceTypes.Cover),
                transaction,
                cancellationToken);
        }

        transaction.Complete();
        CharacterResourceFiles importedStanding = DiscoverSingle(
            Path.Combine(destination, CharacterResourceTypes.Standing),
            CharacterResourceTypes.Standing);
        return new NikkeDbImportResult(
            id,
            destination,
            importedStanding,
            importBattle);
    }

    private CharacterResourceFiles DiscoverSingle(
        string directory,
        string state) =>
        TryDiscoverSingle(directory, state) ??
        throw new InvalidDataException(
            $"NikkeDB {state} resources are incomplete: {directory}");

    private CharacterResourceFiles? TryDiscoverSingle(
        string directory,
        string state)
    {
        if (!Directory.Exists(directory))
        {
            return null;
        }

        return Directory.EnumerateFiles(directory, "*.skel")
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => discovery.DiscoverForSkeleton(path, state))
            .FirstOrDefault(resource => resource != null);
    }

    private static void CopyResource(
        CharacterResourceFiles resource,
        string destination,
        CharacterImportTransaction transaction,
        CancellationToken cancellationToken)
    {
        transaction.CreateDirectory(destination);
        foreach (string source in new[]
                 {
                     resource.SkeletonPath,
                     resource.AtlasPath,
                     resource.PrimaryTexturePath
                 }.Concat(resource.AdditionalTexturePaths))
        {
            string target = Path.Combine(destination, Path.GetFileName(source));
            if (File.Exists(target))
            {
                throw new IOException(
                    $"Import destination already contains {Path.GetFileName(source)}.");
            }

            transaction.CopyFile(source, target, cancellationToken);
        }
    }

    private static string SanitizeSegment(string value)
    {
        string sanitized = string.Concat(value.Select(character =>
            Path.GetInvalidFileNameChars().Contains(character)
                ? '_'
                : character)).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "Unknown" : sanitized;
    }
}
