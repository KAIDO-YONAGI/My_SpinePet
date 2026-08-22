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
    private readonly Rect? _workArea;
    private readonly ConfigFileCommitter _committer;
    private bool _protectExistingConfig;
    private long _saveVersion;
    private int _saveRequestCount;

    internal int SaveRequestCount => Volatile.Read(ref _saveRequestCount);

    internal int DiskReplacementCount => _committer.ReplacementCount;

    public ConfigService(string? configPath = null)
        : this(configPath, null)
    {
    }

    internal ConfigService(string? configPath, Rect? workArea)
    {
        _configPath = configPath ?? AppPaths.ConfigFile;
        _workArea = workArea;
        _committer = new ConfigFileCommitter(_configPath);
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
            NormalizeConfiguration(config);
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
        ConfigNormalizer.Normalize(config, null);
    }

    internal static void Normalize(AppConfig config, Rect workArea)
    {
        ConfigNormalizer.Normalize(config, workArea);
    }

    private void NormalizeConfiguration(AppConfig config)
    {
        ConfigNormalizer.Normalize(config, _workArea);
    }

    public void Save(AppConfig config)
    {
        Interlocked.Increment(ref _saveRequestCount);
        string? json = PrepareSaveJson(config);
        if (json == null)
        {
            return;
        }

        long version = Interlocked.Increment(ref _saveVersion);
        if (_committer.Commit(json, version))
        {
            config.RequiresRewrite = false;
        }
    }

    public Task SaveAsync(AppConfig config)
    {
        Interlocked.Increment(ref _saveRequestCount);
        string? json = PrepareSaveJson(config);
        if (json == null)
        {
            return Task.CompletedTask;
        }

        long version = Interlocked.Increment(ref _saveVersion);
        return Task.Run(() =>
        {
            if (_committer.Commit(json, version))
            {
                config.RequiresRewrite = false;
            }
        });
    }

    private string? PrepareSaveJson(AppConfig config)
    {
        if (_protectExistingConfig)
        {
            AppLogger.Write(
                nameof(ConfigService),
                "save-skipped reason=corrupt-config-backup-unavailable");
            return null;
        }

        try
        {
            NormalizeConfiguration(config);
            return JsonSerializer.Serialize(config, JsonOptions);
        }
        catch (Exception ex)
        {
            AppLogger.Write(
                nameof(ConfigService),
                $"save-prepare-failed message={ex.Message}");
            return null;
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

}
