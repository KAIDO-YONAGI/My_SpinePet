using System.Diagnostics;
using System.IO;
using Microsoft.VisualBasic.FileIO;
using SpinePet.Models;

namespace SpinePet.Services;

public sealed class CharacterResourceStorageService
{
    private readonly Action<string> _recycleDirectory;

    public CharacterResourceStorageService()
        : this(SendDirectoryToRecycleBin)
    {
    }

    internal CharacterResourceStorageService(Action<string> recycleDirectory)
    {
        _recycleDirectory = recycleDirectory;
    }

    public static void OpenResourceDirectory(string resourceDirectory)
    {
        string resolvedDirectory = Path.GetFullPath(resourceDirectory);
        Directory.CreateDirectory(resolvedDirectory);
        Process.Start(new ProcessStartInfo
        {
            FileName = resolvedDirectory,
            UseShellExecute = true
        });
    }

    public static string GetSkinDirectory(
        CharacterResourceFiles resources,
        string resourceDirectory)
    {
        string? standingDirectory = Path.GetDirectoryName(resources.SkeletonPath);
        if (string.IsNullOrWhiteSpace(standingDirectory) ||
            !CharacterResourceTypes.IsRenderable(Path.GetFileName(
                Path.TrimEndingDirectorySeparator(standingDirectory))))
        {
            throw new InvalidOperationException(
                "The selected skin is not stored in a standing directory.");
        }

        string? skinDirectory = Path.GetDirectoryName(standingDirectory);
        if (string.IsNullOrWhiteSpace(skinDirectory))
        {
            throw new InvalidOperationException(
                "The selected skin directory could not be resolved.");
        }

        return ValidateSkinDirectory(
            skinDirectory,
            resourceDirectory,
            resources.Identity.SkinCode,
            resources.Identity.CharacterCode);
    }

    public void RecycleSkinDirectory(
        string skinDirectory,
        string resourceDirectory,
        string expectedSkinCode,
        string expectedCharacterCode)
    {
        string resolvedSkinDirectory = ValidateSkinDirectory(
            skinDirectory,
            resourceDirectory,
            expectedSkinCode,
            expectedCharacterCode);
        if (!Directory.Exists(resolvedSkinDirectory))
        {
            throw new DirectoryNotFoundException(
                $"The skin resource directory no longer exists: {resolvedSkinDirectory}");
        }

        _recycleDirectory(resolvedSkinDirectory);
    }

    internal static string ValidateSkinDirectory(
        string skinDirectory,
        string resourceDirectory,
        string expectedSkinCode,
        string expectedCharacterCode)
    {
        if (string.IsNullOrWhiteSpace(expectedSkinCode))
        {
            throw new InvalidOperationException(
                "The selected character does not have a valid skin ID.");
        }

        if (string.IsNullOrWhiteSpace(expectedCharacterCode))
        {
            throw new InvalidOperationException(
                "The selected character does not have a valid character ID.");
        }

        string resolvedRoot = Path.GetFullPath(resourceDirectory)
            .TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
        string resolvedSkinDirectory = Path.GetFullPath(skinDirectory)
            .TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
        string relativePath = Path.GetRelativePath(
            resolvedRoot,
            resolvedSkinDirectory);
        string[] segments = relativePath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

        if (Path.IsPathRooted(relativePath) ||
            segments.Length != 2 ||
            segments.Any(segment => segment is "." or "..") ||
            !string.Equals(
                segments[1],
                expectedSkinCode,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Refusing to remove a path that is not a managed character skin directory.");
        }

        DirectoryInfo? current = new(resolvedSkinDirectory);
        while (current != null &&
               !string.Equals(
                   current.FullName.TrimEnd(
                       Path.DirectorySeparatorChar,
                       Path.AltDirectorySeparatorChar),
                   resolvedRoot,
                   StringComparison.OrdinalIgnoreCase))
        {
            if (current.Exists &&
                current.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                throw new InvalidOperationException(
                    "Refusing to remove a skin directory through a reparse point.");
            }

            current = current.Parent;
        }

        if (current == null)
        {
            throw new InvalidOperationException(
                "Refusing to remove a skin directory outside the resource root.");
        }

        EnsureDirectoryOwnedByCharacter(
            resolvedSkinDirectory,
            expectedCharacterCode);

        return resolvedSkinDirectory;
    }

    private static void EnsureDirectoryOwnedByCharacter(
        string skinDirectory,
        string expectedCharacterCode)
    {
        CharacterResourceDirectorySafety.EnsureTreeOwnedByCharacter(
            skinDirectory,
            expectedCharacterCode,
            reason => new InvalidOperationException(
                $"Refusing to remove a skin directory that {reason}."));
    }

    private static void SendDirectoryToRecycleBin(string directory)
    {
        FileSystem.DeleteDirectory(
            directory,
            UIOption.OnlyErrorDialogs,
            RecycleOption.SendToRecycleBin,
            UICancelOption.ThrowException);
    }
}
