using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using SpinePet.Infrastructure;
using SpinePet.Infrastructure.Import;
using SpinePet.Models;

namespace SpinePet.Services;

public sealed partial class UnityBundleImportService
{
    private static readonly byte[] UnityFsHeader = "UnityFS"u8.ToArray();
    private static readonly TimeSpan ProcessTerminationTimeout =
        TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ProcessOutputDrainTimeout =
        TimeSpan.FromSeconds(2);

    private readonly CharacterIdentityService _identityService;
    private readonly CharacterResourceDiscoveryService _resourceDiscovery;
    private readonly string _extractorScriptPath;
    private readonly string _pythonCommand;

    public UnityBundleImportService(
        CharacterIdentityService? identityService = null,
        CharacterResourceDiscoveryService? resourceDiscovery = null,
        string? extractorScriptPath = null,
        string pythonCommand = "python")
    {
        _identityService = identityService ?? new CharacterIdentityService();
        _resourceDiscovery = resourceDiscovery ??
            new CharacterResourceDiscoveryService(_identityService);
        _extractorScriptPath =
            extractorScriptPath ?? AppPaths.BundleExtractorScript;
        _pythonCommand = pythonCommand;
    }

    public static bool HasUnityFsHeader(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return false;
        }

        using FileStream stream = File.OpenRead(filePath);
        Span<byte> header = stackalloc byte[UnityFsHeader.Length];
        return stream.Read(header) == header.Length &&
            header.SequenceEqual(UnityFsHeader);
    }

    public static bool TryParseFileName(
        string filePath,
        out CharacterBundleDescriptor? descriptor)
    {
        string fileName = Path.GetFileNameWithoutExtension(filePath);
        Match match = BundleFileNamePattern().Match(fileName);
        if (!match.Success)
        {
            descriptor = null;
            return false;
        }

        string resourceType = match.Groups["type"].Value.ToLowerInvariant();
        if (!CharacterResourceTypes.IsSupported(resourceType))
        {
            descriptor = null;
            return false;
        }

        string characterCode = match.Groups["character"].Value;
        string skinCode = match.Groups["skin"].Value;
        descriptor = new CharacterBundleDescriptor(
            $"c{characterCode}_{skinCode}",
            characterCode,
            skinCode,
            resourceType);
        return true;
    }

    public async Task<CharacterBundleImportResult> ImportAsync(
        string bundlePath,
        string resourceDirectory,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!HasUnityFsHeader(bundlePath))
        {
            throw new InvalidDataException(
                "The selected file does not have a UnityFS header.");
        }

        if (!TryParseFileName(bundlePath, out CharacterBundleDescriptor? descriptor) ||
            descriptor == null)
        {
            throw new InvalidDataException(
                "The file name must start with " +
                "c<character>_<skin>_<standing|icons>. " +
                "Aim and cover bundles are no longer supported.");
        }

        if (!File.Exists(_extractorScriptPath))
        {
            throw new FileNotFoundException(
                "The UnityPy extractor script was not found.",
                _extractorScriptPath);
        }

        CharacterIdentity identity = ResolveRequiredIdentity(
            descriptor.ResourceName);
        string characterDirectory = Path.Combine(
            resourceDirectory,
            ValidateDirectorySegment(
                identity.DisplayName,
                "character name"));
        string skinDirectory = Path.Combine(
            characterDirectory,
            ValidateDirectorySegment(
                descriptor.SkinCode,
                "skin ID"));
        EnsureSkinDirectoryOwnedByCharacter(
            skinDirectory,
            resourceDirectory,
            identity.CharacterCode);
        Directory.CreateDirectory(resourceDirectory);
        string destinationDirectory = Path.Combine(
            skinDirectory,
            descriptor.ResourceType);
        string workingDirectory = Path.Combine(
            resourceDirectory,
            $".SpinePet-Import-{Guid.NewGuid():N}");

        Directory.CreateDirectory(workingDirectory);
        try
        {
            await RunExtractorAsync(
                bundlePath,
                descriptor.ResourceName,
                descriptor.ResourceType,
                workingDirectory,
                cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            CharacterResourceFiles? stagedResources = null;
            string? extractedIconPath = null;
            if (CharacterResourceTypes.IsRenderable(
                descriptor.ResourceType))
            {
                string extractedSkeletonPath = Path.Combine(
                    workingDirectory,
                    $"{descriptor.ResourceName}.skel");
                stagedResources = _resourceDiscovery.DiscoverForSkeleton(
                    extractedSkeletonPath,
                    descriptor.ResourceType);
                if (stagedResources == null)
                {
                    throw new InvalidDataException(
                        "The Unity bundle does not contain a complete matching " +
                        "Spine skeleton, atlas, and texture set.");
                }

                SpineSkeletonCompatibility.EnsureSupported(
                    stagedResources.SkeletonPath);
            }
            else
            {
                extractedIconPath = Path.Combine(
                    workingDirectory,
                    $"{descriptor.ResourceName}_icon.png");
                if (!File.Exists(extractedIconPath))
                {
                    throw new InvalidDataException(
                        "The Unity bundle does not contain the matching " +
                        $"Sprite 'mi_{descriptor.ResourceName}_s'.");
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            EnsureSkinDirectories(skinDirectory);
            cancellationToken.ThrowIfCancellationRequested();
            MoveExtractedFiles(workingDirectory, destinationDirectory);

            if (stagedResources == null)
            {
                string importedIconPath = Path.Combine(
                    destinationDirectory,
                    Path.GetFileName(extractedIconPath!));
                return new CharacterBundleImportResult(
                    identity,
                    descriptor.ResourceType,
                    destinationDirectory,
                    IconPath: importedIconPath);
            }

            string destinationSkeletonPath = Path.Combine(
                destinationDirectory,
                $"{descriptor.ResourceName}.skel");
            CharacterResourceFiles? importedResources =
                _resourceDiscovery.DiscoverForSkeleton(
                    destinationSkeletonPath,
                    descriptor.ResourceType);
            if (importedResources == null)
            {
                throw new InvalidDataException(
                    "The extracted character files could not be discovered.");
            }
            return new CharacterBundleImportResult(
                importedResources.Identity,
                importedResources.ResourceType,
                destinationDirectory,
                Resources: importedResources);
        }
        finally
        {
            TryDeleteWorkingDirectory(
                workingDirectory,
                resourceDirectory);
        }
    }

    public CharacterBundleImportResult ImportSkeleton(
        string skeletonPath,
        string resourceDirectory,
        string? resourceType = null,
        CancellationToken cancellationToken = default) =>
        ImportResourceSet(
            skeletonPath,
            resourceDirectory,
            resourceType,
            cancellationToken);

    public CharacterBundleImportResult ImportResourceSet(
        string skeletonPath,
        string resourceDirectory,
        string? resourceType = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.IsNullOrWhiteSpace(resourceType) &&
            !CharacterResourceTypes.IsRenderable(resourceType))
        {
            throw new InvalidDataException(
                "Only standing skeleton resources can be imported.");
        }

        string sourceDirectoryName = Path.GetFileName(
            Path.TrimEndingDirectorySeparator(
                Path.GetDirectoryName(skeletonPath) ?? string.Empty));
        if (sourceDirectoryName.Equals(
                "aim",
                StringComparison.OrdinalIgnoreCase) ||
            sourceDirectoryName.Equals(
                "cover",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Aim and cover skeleton resources are no longer supported. " +
                "Select a standing skeleton instead.");
        }

        CharacterResourceFiles? sourceResources =
            _resourceDiscovery.DiscoverForSkeleton(
                skeletonPath,
                resourceType);
        if (sourceResources == null)
        {
            throw new InvalidDataException(
                "The selected standing skeleton does not have a complete " +
                "matching atlas and texture set.");
        }

        CharacterIdentity identity = ResolveRequiredIdentity(
            sourceResources.Identity.ResourceName);
        if (string.IsNullOrWhiteSpace(identity.SkinCode))
        {
            throw new InvalidDataException(
                "The skeleton file name must start with " +
                "c<character>_<skin>.");
        }

        string characterDirectory = Path.Combine(
            resourceDirectory,
            ValidateDirectorySegment(
                identity.DisplayName,
                "character name"));
        string skinDirectory = Path.Combine(
            characterDirectory,
            ValidateDirectorySegment(
                identity.SkinCode,
                "skin ID"));
        EnsureSkinDirectoryOwnedByCharacter(
            skinDirectory,
            resourceDirectory,
            identity.CharacterCode);
        SpineSkeletonCompatibility.EnsureSupported(
            sourceResources.SkeletonPath);
        Directory.CreateDirectory(resourceDirectory);
        string destinationDirectory = Path.Combine(
            skinDirectory,
            CharacterResourceTypes.Standing);

        cancellationToken.ThrowIfCancellationRequested();
        EnsureSkinDirectories(skinDirectory);
        cancellationToken.ThrowIfCancellationRequested();
        CopyResourceFiles(
            sourceResources,
            destinationDirectory,
            cancellationToken);

        string destinationSkeletonPath = Path.Combine(
            destinationDirectory,
            Path.GetFileName(sourceResources.SkeletonPath));
        CharacterResourceFiles? importedResources =
            _resourceDiscovery.DiscoverForSkeleton(
                destinationSkeletonPath,
                CharacterResourceTypes.Standing);
        if (importedResources == null)
        {
            throw new InvalidDataException(
                "The copied character files could not be discovered.");
        }

        return new CharacterBundleImportResult(
            importedResources.Identity,
            importedResources.ResourceType,
            destinationDirectory,
            Resources: importedResources);
    }

    private async Task RunExtractorAsync(
        string bundlePath,
        string resourceName,
        string resourceType,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = _pythonCommand,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(_extractorScriptPath);
        startInfo.ArgumentList.Add("--bundle");
        startInfo.ArgumentList.Add(bundlePath);
        startInfo.ArgumentList.Add("--resource-id");
        startInfo.ArgumentList.Add(resourceName);
        startInfo.ArgumentList.Add("--resource-type");
        startInfo.ArgumentList.Add(resourceType);
        startInfo.ArgumentList.Add("--output-directory");
        startInfo.ArgumentList.Add(outputDirectory);

        cancellationToken.ThrowIfCancellationRequested();
        using Process process = Process.Start(startInfo) ??
            throw new InvalidOperationException(
                "The UnityPy extractor could not be started.");
        Task<string> standardOutputTask =
            process.StandardOutput.ReadToEndAsync(
                CancellationToken.None);
        Task<string> standardErrorTask =
            process.StandardError.ReadToEndAsync(
                CancellationToken.None);

        try
        {
            await process.WaitForExitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            await TerminateProcessAsync(process)
                .ConfigureAwait(false);
            await ObserveProcessOutputAsync(
                    standardOutputTask,
                    standardErrorTask)
                .ConfigureAwait(false);

            throw;
        }

        string[] processOutput;
        try
        {
            processOutput = await Task.WhenAll(
                    standardOutputTask,
                    standardErrorTask)
                .WaitAsync(
                    ProcessOutputDrainTimeout,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (TimeoutException exception)
        {
            throw new InvalidDataException(
                "UnityPy extraction finished, but its output pipes " +
                "did not close. A child process may still be running.",
                exception);
        }

        string standardOutput = processOutput[0];
        string standardError = processOutput[1];

        if (process.ExitCode != 0)
        {
            string details = string.IsNullOrWhiteSpace(standardError)
                ? standardOutput
                : standardError;
            throw new InvalidDataException(
                $"UnityPy extraction failed: {details.Trim()}");
        }

        AppLogger.Write(
            nameof(UnityBundleImportService),
            $"extract-succeeded resource={resourceName} output={standardOutput.Trim()}");
    }

    private CharacterIdentity ResolveRequiredIdentity(string resourceName)
    {
        CharacterIdentity identity = _identityService.Resolve(
            $"{resourceName}.skel",
            string.Empty);
        if (string.IsNullOrWhiteSpace(identity.CharacterCode) ||
            !_identityService.TryGetCharacterName(
                identity.CharacterCode,
                out string displayName))
        {
            throw new InvalidDataException(
                $"Character ID '{identity.CharacterCode}' is missing from " +
                "Data/CharacterNames.json. Add its display name before " +
                "importing this resource.");
        }

        return identity with
        {
            DisplayName = ValidateDirectorySegment(
                displayName,
                "character name")
        };
    }

    private static async Task TerminateProcessAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception)
        {
            AppLogger.Write(
                nameof(UnityBundleImportService),
                $"extract-terminate-failed message={exception.Message}");
        }

        try
        {
            using CancellationTokenSource timeoutSource =
                new(ProcessTerminationTimeout);
            await process.WaitForExitAsync(timeoutSource.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            AppLogger.Write(
                nameof(UnityBundleImportService),
                $"extract-exit-wait-timeout " +
                $"timeoutMs={ProcessTerminationTimeout.TotalMilliseconds:F0}");
        }
        catch (Exception exception)
        {
            AppLogger.Write(
                nameof(UnityBundleImportService),
                $"extract-exit-wait-failed message={exception.Message}");
        }
    }

    private static async Task ObserveProcessOutputAsync(
        Task<string> standardOutputTask,
        Task<string> standardErrorTask)
    {
        Task outputTask = Task.WhenAll(
            standardOutputTask,
            standardErrorTask);
        try
        {
            await outputTask
                .WaitAsync(
                    ProcessOutputDrainTimeout,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            AppLogger.Write(
                nameof(UnityBundleImportService),
                $"extract-output-drain-timeout " +
                $"timeoutMs={ProcessOutputDrainTimeout.TotalMilliseconds:F0}");
            _ = outputTask.ContinueWith(
                completedTask => _ = completedTask.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted |
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
        catch (Exception exception)
        {
            AppLogger.Write(
                nameof(UnityBundleImportService),
                $"extract-output-drain-failed " +
                $"message={exception.Message}");
        }
    }

    private static void MoveExtractedFiles(
        string workingDirectory,
        string destinationDirectory)
    {
        string[] sourceFiles = Directory
            .EnumerateFiles(workingDirectory)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (sourceFiles.Length == 0)
        {
            throw new InvalidDataException(
                "The UnityPy extractor produced no files.");
        }

        Directory.CreateDirectory(destinationDirectory);
        string[] destinationPaths = sourceFiles
            .Select(sourcePath => Path.Combine(
                destinationDirectory,
                Path.GetFileName(sourcePath)))
            .ToArray();
        string? conflict = destinationPaths.FirstOrDefault(File.Exists);
        if (conflict != null)
        {
            throw new IOException(
                $"A character resource already exists: {conflict}");
        }

        List<string> movedFiles = [];
        try
        {
            for (int index = 0; index < sourceFiles.Length; index++)
            {
                File.Move(sourceFiles[index], destinationPaths[index]);
                movedFiles.Add(destinationPaths[index]);
            }
        }
        catch
        {
            foreach (string movedFile in movedFiles)
            {
                if (File.Exists(movedFile))
                {
                    File.Delete(movedFile);
                }
            }

            throw;
        }
    }

    private static void CopyResourceFiles(
        CharacterResourceFiles resources,
        string destinationDirectory,
        CancellationToken cancellationToken)
    {
        string[] sourceFiles = new[]
            {
                resources.SkeletonPath,
                resources.AtlasPath,
                resources.PrimaryTexturePath
            }
            .Concat(resources.AdditionalTexturePaths)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Directory.CreateDirectory(destinationDirectory);
        (string Source, string Destination)[] copies = sourceFiles
            .Select(sourcePath => (
                Source: sourcePath,
                Destination: Path.Combine(
                    destinationDirectory,
                    Path.GetFileName(sourcePath))))
            .Where(copy => !PathsEqual(copy.Source, copy.Destination))
            .ToArray();

        string? conflict = copies
            .Select(copy => copy.Destination)
            .FirstOrDefault(File.Exists);
        if (conflict != null)
        {
            throw new IOException(
                $"A character resource already exists: {conflict}");
        }

        List<string> copiedFiles = [];
        try
        {
            foreach ((string source, string destination) in copies)
            {
                cancellationToken.ThrowIfCancellationRequested();
                File.Copy(source, destination, overwrite: false);
                copiedFiles.Add(destination);
            }
        }
        catch
        {
            foreach (string copiedFile in copiedFiles)
            {
                if (File.Exists(copiedFile))
                {
                    File.Delete(copiedFile);
                }
            }

            throw;
        }
    }

    private static bool PathsEqual(string firstPath, string secondPath) =>
        string.Equals(
            Path.GetFullPath(firstPath),
            Path.GetFullPath(secondPath),
            StringComparison.OrdinalIgnoreCase);

    private static void EnsureSkinDirectories(string skinDirectory)
    {
        foreach (string resourceType in CharacterResourceTypes.All)
        {
            Directory.CreateDirectory(
                Path.Combine(skinDirectory, resourceType));
        }
    }

    private static void TryDeleteWorkingDirectory(
        string workingDirectory,
        string resourceDirectory)
    {
        try
        {
            DeleteWorkingDirectory(
                workingDirectory,
                resourceDirectory);
        }
        catch (Exception exception)
        {
            AppLogger.Write(
                nameof(UnityBundleImportService),
                $"import-cleanup-failed path={workingDirectory} " +
                $"message={exception.Message}");
        }
    }

    private static void DeleteWorkingDirectory(
        string workingDirectory,
        string resourceDirectory)
    {
        if (!Directory.Exists(workingDirectory))
        {
            return;
        }

        string resolvedWorkingDirectory =
            Path.GetFullPath(workingDirectory);
        string resolvedResourceDirectory =
            Path.GetFullPath(resourceDirectory)
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        if (!resolvedWorkingDirectory.StartsWith(
            resolvedResourceDirectory,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Refusing to delete an import directory outside res.");
        }

        Directory.Delete(resolvedWorkingDirectory, recursive: true);
    }

    private static string ValidateDirectorySegment(
        string value,
        string description)
    {
        char[] invalidCharacters = Path.GetInvalidFileNameChars();
        string normalized = value.Trim();
        if (string.IsNullOrWhiteSpace(normalized) ||
            normalized is "." or ".." ||
            !string.Equals(value, normalized, StringComparison.Ordinal) ||
            normalized.EndsWith('.') ||
            ReservedDirectoryNamePattern().IsMatch(
                Path.GetFileNameWithoutExtension(normalized)) ||
            normalized.Any(invalidCharacters.Contains))
        {
            throw new InvalidDataException(
                $"The mapped {description} '{value}' cannot be used as a " +
                "directory name. Correct Data/CharacterNames.json or the " +
                "resource file name before importing.");
        }

        return normalized;
    }

    private static void EnsureSkinDirectoryOwnedByCharacter(
        string skinDirectory,
        string resourceDirectory,
        string expectedCharacterCode)
    {
        string resolvedRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(resourceDirectory));
        string resolvedSkinDirectory = Path.GetFullPath(skinDirectory);
        DirectoryInfo? current = new(resolvedSkinDirectory);
        while (current != null &&
               !string.Equals(
                   Path.TrimEndingDirectorySeparator(current.FullName),
                   resolvedRoot,
                   StringComparison.OrdinalIgnoreCase))
        {
            if (current.Exists &&
                current.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                throw new InvalidDataException(
                    "The target import path contains a reparse point.");
            }

            current = current.Parent;
        }

        if (current == null)
        {
            throw new InvalidDataException(
                "The target skin directory is outside the resource directory.");
        }

        if (!Directory.Exists(skinDirectory))
        {
            return;
        }

        CharacterResourceDirectorySafety.EnsureTreeOwnedByCharacter(
            skinDirectory,
            expectedCharacterCode,
            reason => new InvalidDataException(
                $"The target skin directory {reason}. Give character ID " +
                $"'{expectedCharacterCode}' a unique display name in " +
                "Data/CharacterNames.json or remove the unsafe path before " +
                "importing."));
    }

    [GeneratedRegex(
        @"^c(?<character>\d+)_(?<skin>[^_]+)_" +
        @"(?<type>standing|icons)(?:_.*)?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BundleFileNamePattern();

    [GeneratedRegex(
        @"^(con|prn|aux|nul|com[1-9]|lpt[1-9])$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ReservedDirectoryNamePattern();
}
