using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using SpinePet.Infrastructure;
using SpinePet.Models;

namespace SpinePet.Services;

public sealed class ConfigService
{
    private const string CorruptTimestampFormat =
        "yyyyMMdd'T'HHmmssfffffff'Z'";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _configPath;
    private bool _protectExistingConfig;

    public ConfigService(string? configPath = null)
    {
        _configPath = configPath ?? AppPaths.ConfigFile;
    }

    public AppConfig Load()
    {
        if (!File.Exists(_configPath))
        {
            _protectExistingConfig = false;
            return new AppConfig();
        }

        try
        {
            string json = File.ReadAllText(_configPath);
            AppConfig config =
                JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ??
                throw new JsonException(
                    "The configuration root cannot be null.");
            Normalize(config);
            _protectExistingConfig = false;
            return config;
        }
        catch (Exception ex)
        {
            _protectExistingConfig = true;
            string? backupPath = TryCreateCorruptBackup();
            if (backupPath != null)
            {
                _protectExistingConfig = false;
            }

            AppLogger.Write(
                nameof(ConfigService),
                $"load-failed backup={backupPath ?? "unavailable"} " +
                $"message={ex.Message}");
        }

        return new AppConfig();
    }

    internal static void Normalize(AppConfig config)
    {
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
        Rect workArea = needsWorkArea
            ? SystemParameters.WorkArea
            : Rect.Empty;
        double centerX = workArea.Left + workArea.Width / 2;
        double feetY = workArea.Bottom - 24;
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
            if (!double.IsFinite(character.AnimationSpeed) || character.AnimationSpeed <= 0)
            {
                character.AnimationSpeed = 1.0;
            }

            character.AnimationSpeed = Math.Clamp(character.AnimationSpeed, 0.1, 2.0);

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

        if (migrateLegacyPositions)
        {
            CharacterConfig[] visibleCharacters = config.Characters
                .Where(character => character.Visible)
                .ToArray();
            for (int index = 0;
                 index < visibleCharacters.Length;
                 index++)
            {
                visibleCharacters[index].PositionX =
                    workArea.Left +
                    workArea.Width *
                    (index + 1) /
                    (visibleCharacters.Length + 1);
                visibleCharacters[index].PositionY = feetY;
            }

            foreach (CharacterConfig character in config.Characters)
            {
                if (!character.Visible)
                {
                    character.PositionX = centerX;
                    character.PositionY = feetY;
                }
            }
        }
    }

    private static bool IsLegacyVersion(string version)
    {
        return version switch
        {
            "1.0" or "1.1" or "1.2" or "1.3" => true,
            _ => false
        };
    }

    private static bool IsFutureVersion(string version)
    {
        return Version.TryParse(version, out Version? parsedVersion) &&
            Version.TryParse(
                AppConfig.CurrentVersion,
                out Version? currentVersion) &&
            parsedVersion > currentVersion;
    }

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
                Path.TrimEndingDirectorySeparator(directory ?? string.Empty));
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

    public void Save(AppConfig config)
    {
        if (_protectExistingConfig)
        {
            AppLogger.Write(
                nameof(ConfigService),
                "save-skipped reason=corrupt-config-backup-unavailable");
            return;
        }

        string temporaryPath = $"{_configPath}.tmp";
        try
        {
            Normalize(config);
            string? directory = Path.GetDirectoryName(_configPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string json = JsonSerializer.Serialize(config, JsonOptions);
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, _configPath, overwrite: true);
            config.RequiresRewrite = false;
        }
        catch (Exception ex)
        {
            AppLogger.Write(nameof(ConfigService), $"save-failed message={ex.Message}");
        }
        finally
        {
            TryDeleteTemporaryFile(temporaryPath);
        }
    }

    private string? TryCreateCorruptBackup()
    {
        try
        {
            if (!File.Exists(_configPath))
            {
                return _configPath;
            }

            DateTime lastWriteTimeUtc =
                File.GetLastWriteTimeUtc(_configPath).ToUniversalTime();
            string timestamp = lastWriteTimeUtc.ToString(
                CorruptTimestampFormat,
                CultureInfo.InvariantCulture);
            string backupStem = $"{_configPath}.{timestamp}";
            string backupPath = $"{backupStem}.corrupt";
            int sequence = 1;
            while (File.Exists(backupPath) || Directory.Exists(backupPath))
            {
                backupPath = $"{backupStem}.{sequence}.corrupt";
                sequence++;
            }

            File.Copy(_configPath, backupPath);
            File.SetLastWriteTimeUtc(backupPath, lastWriteTimeUtc);
            return backupPath;
        }
        catch (Exception exception)
        {
            AppLogger.Write(
                nameof(ConfigService),
                $"corrupt-config-backup-failed message={exception.Message}");
            return null;
        }
    }

    private static void TryDeleteTemporaryFile(string temporaryPath)
    {
        try
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
        catch (Exception exception)
        {
            AppLogger.Write(
                nameof(ConfigService),
                $"temporary-file-cleanup-failed message={exception.Message}");
        }
    }
}
