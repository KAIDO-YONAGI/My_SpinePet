using System.IO;
using System.Windows;
using SpinePet.Models;

namespace SpinePet.Services;

internal static class ConfigNormalizer
{
    public static void Normalize(AppConfig config, Rect? workArea)
    {
        ArgumentNullException.ThrowIfNull(config);

        string sourceVersion = config.Version ?? string.Empty;
        bool migrateLegacyPositions = IsLegacyVersion(sourceVersion);
        bool preserveFutureVersion = IsFutureVersion(sourceVersion);
        if (!preserveFutureVersion &&
            !string.Equals(
                sourceVersion,
                AppConfig.CurrentVersion,
                StringComparison.Ordinal))
        {
            config.RequiresRewrite = true;
        }
        if (!preserveFutureVersion)
        {
            config.Version = AppConfig.CurrentVersion;
        }

        config.Global ??= new GlobalConfig();
        config.Global.TargetFrameRate =
            GlobalConfig.NormalizeTargetFrameRate(
                config.Global.TargetFrameRate);
        config.Characters = config.Characters?
            .OfType<CharacterConfig>()
            .ToList() ?? [];

        bool needsWorkArea =
            migrateLegacyPositions ||
            config.Characters.Any(character =>
                !double.IsFinite(character.PositionX) ||
                !double.IsFinite(character.PositionY));
        Rect normalizedWorkArea = needsWorkArea
            ? workArea ?? SystemParameters.WorkArea
            : Rect.Empty;
        double centerX = normalizedWorkArea.Left +
            normalizedWorkArea.Width / 2;
        double feetY = normalizedWorkArea.Bottom - 24;
        HashSet<string> characterIds = new(StringComparer.Ordinal);

        foreach (CharacterConfig character in config.Characters)
        {
            if (string.IsNullOrWhiteSpace(character.Id) ||
                !characterIds.Add(character.Id))
            {
                do
                {
                    character.Id = Guid.NewGuid().ToString("D");
                }
                while (!characterIds.Add(character.Id));
            }

            character.Name ??= string.Empty;
            character.SkeletonPath ??= string.Empty;
            character.AtlasPath ??= string.Empty;
            character.TexturePath ??= string.Empty;
            character.ConfiguredAnimation ??= string.Empty;
            character.AdditionalTexturePaths = character
                .AdditionalTexturePaths?
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? [];
            if (character.LegacyResourceType != null &&
                !preserveFutureVersion)
            {
                config.RequiresRewrite = true;
            }

            bool hasRetiredResourcePath = IsRetiredResourcePath(
                character.SkeletonPath);
            if ((!string.IsNullOrWhiteSpace(character.LegacyResourceType) &&
                 !string.Equals(
                     character.LegacyResourceType,
                     CharacterResourceTypes.Standing,
                     StringComparison.OrdinalIgnoreCase)) ||
                hasRetiredResourcePath)
            {
                character.RequiresStandingMigration = true;
                character.ConfiguredAnimation = string.Empty;
            }
            character.LegacyResourceType = null;
            if (!double.IsFinite(character.AnimationSpeed) ||
                character.AnimationSpeed <= 0)
            {
                character.AnimationSpeed =
                    CharacterConfig.DefaultAnimationSpeed;
            }
            character.AnimationSpeed = Math.Clamp(
                character.AnimationSpeed,
                0.1,
                2.0);

            if (!double.IsFinite(character.Scale) ||
                character.Scale <= 0)
            {
                character.Scale = 1.0;
            }

            if (!double.IsFinite(character.PositionX))
            {
                character.PositionX = centerX;
            }
            if (!double.IsFinite(character.PositionY))
            {
                character.PositionY = feetY;
            }
        }

        if (!migrateLegacyPositions)
        {
            return;
        }

        CharacterConfig[] visibleCharacters = config.Characters
            .Where(character => character.Visible)
            .ToArray();
        for (int index = 0; index < visibleCharacters.Length; index++)
        {
            visibleCharacters[index].PositionX =
                normalizedWorkArea.Left +
                normalizedWorkArea.Width *
                (index + 1) /
                (visibleCharacters.Length + 1);
            visibleCharacters[index].PositionY = feetY;
        }

        foreach (CharacterConfig character in config.Characters.Where(
                     character => !character.Visible))
        {
            character.PositionX = centerX;
            character.PositionY = feetY;
        }
    }

    private static bool IsLegacyVersion(string version) =>
        version is "1.0" or "1.1" or "1.2" or "1.3";

    private static bool IsFutureVersion(string version) =>
        Version.TryParse(version, out Version? parsedVersion) &&
        Version.TryParse(
            AppConfig.CurrentVersion,
            out Version? currentVersion) &&
        parsedVersion > currentVersion;

    private static bool IsRetiredResourcePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            string? directory = Path.GetDirectoryName(path);
            string directoryName = Path.GetFileName(
                Path.TrimEndingDirectorySeparator(
                    directory ?? string.Empty));
            return directoryName.Equals(
                    "aim",
                    StringComparison.OrdinalIgnoreCase) ||
                directoryName.Equals(
                    "cover",
                    StringComparison.OrdinalIgnoreCase);
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
