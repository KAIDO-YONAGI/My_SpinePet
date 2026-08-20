using System.IO;
using System.Text.RegularExpressions;

namespace SpinePet.Services;

internal static partial class CharacterResourceDirectorySafety
{
    private static readonly EnumerationOptions DirectChildren = new()
    {
        AttributesToSkip = 0,
        IgnoreInaccessible = false,
        RecurseSubdirectories = false,
        ReturnSpecialDirectories = false
    };

    public static void EnsureTreeOwnedByCharacter(
        string skinDirectory,
        string expectedCharacterCode,
        Func<string, Exception> createException)
    {
        if (!Directory.Exists(skinDirectory))
        {
            return;
        }

        Stack<DirectoryInfo> pending = new();
        pending.Push(new DirectoryInfo(skinDirectory));
        while (pending.TryPop(out DirectoryInfo? directory))
        {
            directory.Refresh();
            if (directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                throw createException(
                    $"contains a reparse point: {directory.FullName}");
            }

            foreach (FileSystemInfo entry in directory.EnumerateFileSystemInfos(
                         "*",
                         DirectChildren))
            {
                entry.Refresh();
                if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    throw createException(
                        $"contains a reparse point: {entry.FullName}");
                }

                if (entry.Attributes.HasFlag(FileAttributes.Directory))
                {
                    pending.Push((DirectoryInfo)entry);
                    continue;
                }

                Match match = CharacterResourceFileNamePattern().Match(
                    entry.Name);
                if (match.Success &&
                    !string.Equals(
                        match.Groups["character"].Value,
                        expectedCharacterCode,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw createException(
                        "also contains resources for character ID " +
                        $"'{match.Groups["character"].Value}': " +
                        entry.FullName);
                }
            }
        }
    }

    [GeneratedRegex(
        @"^c(?<character>\d+)_",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CharacterResourceFileNamePattern();
}
