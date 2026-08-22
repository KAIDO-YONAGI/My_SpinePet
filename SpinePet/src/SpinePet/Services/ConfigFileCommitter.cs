using System.IO;
using SpinePet.Infrastructure;

namespace SpinePet.Services;

internal sealed class ConfigFileCommitter(string configPath)
{
    private readonly object _sync = new();
    private long _lastCommittedVersion;
    private int _replacementCount;

    internal int ReplacementCount => Volatile.Read(ref _replacementCount);

    public bool Commit(string json, long version)
    {
        lock (_sync)
        {
            if (version < _lastCommittedVersion)
            {
                return true;
            }

            string temporaryPath =
                $"{configPath}.tmp.{Guid.NewGuid():N}";
            try
            {
                if (File.Exists(configPath) &&
                    string.Equals(
                        File.ReadAllText(configPath),
                        json,
                        StringComparison.Ordinal))
                {
                    _lastCommittedVersion = version;
                    return true;
                }

                string? directory = Path.GetDirectoryName(configPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(temporaryPath, json);
                File.Move(temporaryPath, configPath, overwrite: true);
                Interlocked.Increment(ref _replacementCount);
                _lastCommittedVersion = version;
                return true;
            }
            catch (Exception exception)
            {
                AppLogger.Write(
                    nameof(ConfigService),
                    $"save-failed message={exception.Message}");
                return false;
            }
            finally
            {
                TryDeleteTemporaryFile(temporaryPath);
            }
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
