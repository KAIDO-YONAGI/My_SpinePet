using System.IO;
using SpinePet.Models;

namespace SpinePet.Services;

public static class CharacterIconService
{
    public static string GetThumbnailPath(
        CharacterConfig character,
        CharacterIdentity identity)
    {
        string? resourceDirectory =
            Path.GetDirectoryName(character.SkeletonPath);
        if (!string.IsNullOrWhiteSpace(resourceDirectory) &&
            !string.IsNullOrWhiteSpace(identity.ResourceName))
        {
            string legacyIconPath = Path.Combine(
                resourceDirectory,
                $"{identity.ResourceName}_icon.png");
            string resourceDirectoryName = Path.GetFileName(
                Path.TrimEndingDirectorySeparator(resourceDirectory));
            if (CharacterResourceTypes.IsRenderable(resourceDirectoryName))
            {
                string? characterDirectory =
                    Path.GetDirectoryName(resourceDirectory);
                if (!string.IsNullOrWhiteSpace(characterDirectory))
                {
                    string iconPath = Path.Combine(
                        characterDirectory,
                        CharacterResourceTypes.Icons,
                        $"{identity.ResourceName}_icon.png");
                    if (File.Exists(iconPath))
                    {
                        return iconPath;
                    }
                }
            }

            if (File.Exists(legacyIconPath))
            {
                return legacyIconPath;
            }
        }

        return character.TexturePath;
    }
}
