using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using SpinePet.Infrastructure;
using SpinePet.Models;
using SpinePet.Services;

namespace SpinePet.Tests;

public sealed class UnityBundleImportServiceTests : IDisposable
{
    private static readonly TimeSpan ProcessTestTimeout =
        TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ProcessCleanupTimeout =
        TimeSpan.FromSeconds(2);

    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "SpinePet.Tests",
        Guid.NewGuid().ToString("N"));

    public UnityBundleImportServiceTests()
    {
        Directory.CreateDirectory(_temporaryDirectory);
    }

    [Fact]
    public void TryParseFileNameReadsIdentitySkinAndRenderableType()
    {
        bool parsed = UnityBundleImportService.TryParseFileName(
            Path.Combine(
                _temporaryDirectory,
                "c233_01_standing_假面紫罗兰_Dorothy"),
            out CharacterBundleDescriptor? descriptor);

        Assert.True(parsed);
        Assert.NotNull(descriptor);
        Assert.Equal("c233_01", descriptor.ResourceName);
        Assert.Equal("233", descriptor.CharacterCode);
        Assert.Equal("01", descriptor.SkinCode);
        Assert.Equal(CharacterResourceTypes.Standing, descriptor.ResourceType);
    }

    [Fact]
    public void TryParseFileNameAcceptsIconBundles()
    {
        Assert.True(UnityBundleImportService.TryParseFileName(
            "c015_00_icons_Na0h_Summer-Anis",
            out CharacterBundleDescriptor? descriptor));
        Assert.NotNull(descriptor);
        Assert.Equal(CharacterResourceTypes.Icons, descriptor.ResourceType);
    }

    [Theory]
    [InlineData("c233_01_aim_author")]
    [InlineData("c233_01_cover_author")]
    public void TryParseFileNameRejectsRetiredStateBundles(string fileName)
    {
        Assert.False(UnityBundleImportService.TryParseFileName(
            fileName,
            out CharacterBundleDescriptor? descriptor));
        Assert.Null(descriptor);
    }

    [Fact]
    public void HasUnityFsHeaderChecksMagicBytesInsteadOfExtension()
    {
        string unityPath = Path.Combine(
            _temporaryDirectory,
            "c233_01_cover_author_name");
        string otherPath = Path.Combine(
            _temporaryDirectory,
            "c233_01_aim_author_name.bundle");
        File.WriteAllBytes(
            unityPath,
            [0x55, 0x6E, 0x69, 0x74, 0x79, 0x46, 0x53, 0x00]);
        File.WriteAllBytes(otherPath, [0x50, 0x4B, 0x03, 0x04]);

        Assert.True(UnityBundleImportService.HasUnityFsHeader(unityPath));
        Assert.False(UnityBundleImportService.HasUnityFsHeader(otherPath));
    }

    [Fact]
    public async Task ImportAsyncCreatesStandingAndIconsLayoutAndRefusesToOverwrite()
    {
        string extractorPath =
            Path.Combine(_temporaryDirectory, "fake_extractor.py");
        await File.WriteAllTextAsync(
            extractorPath,
            """
            import argparse
            from pathlib import Path

            parser = argparse.ArgumentParser()
            parser.add_argument("--bundle")
            parser.add_argument("--resource-id", required=True)
            parser.add_argument("--resource-type", required=True)
            parser.add_argument("--output-directory", required=True, type=Path)
            args = parser.parse_args()
            args.output_directory.mkdir(parents=True, exist_ok=True)
            (args.output_directory / f"{args.resource_id}.skel").write_bytes(
                b"\0" * 8 + bytes([7]) + b"4.1.24"
            )
            (args.output_directory / f"{args.resource_id}.atlas").write_text(
                f"{args.resource_id}.png\nsize:1,1\n",
                encoding="utf-8",
            )
            (args.output_directory / f"{args.resource_id}.png").write_bytes(b"png")
            """);
        string bundlePath = Path.Combine(
            _temporaryDirectory,
            "c233_01_standing_author_Dorothy");
        await File.WriteAllBytesAsync(
            bundlePath,
            [0x55, 0x6E, 0x69, 0x74, 0x79, 0x46, 0x53, 0x00]);
        string resourceDirectory =
            Path.Combine(_temporaryDirectory, "res");
        CharacterIdentityService identityService = new();
        CharacterResourceDiscoveryService discoveryService =
            new(identityService);
        UnityBundleImportService service = new(
            identityService,
            discoveryService,
            extractorPath);

        CharacterBundleImportResult result =
            await service.ImportAsync(bundlePath, resourceDirectory);

        string characterDirectory =
            Path.Combine(resourceDirectory, "Dorothy");
        string skinDirectory = Path.Combine(characterDirectory, "01");
        Assert.Equal(
            Path.Combine(skinDirectory, CharacterResourceTypes.Standing),
            result.DestinationDirectory);
        Assert.NotNull(result.Resources);
        Assert.Equal("01", result.Identity.SkinCode);
        foreach (string resourceType in CharacterResourceTypes.All)
        {
            Assert.True(Directory.Exists(
                Path.Combine(skinDirectory, resourceType)));
        }
        Assert.False(Directory.Exists(Path.Combine(skinDirectory, "aim")));
        Assert.False(Directory.Exists(Path.Combine(skinDirectory, "cover")));

        Assert.True(File.Exists(Path.Combine(
            result.DestinationDirectory,
            "c233_01.skel")));
        await Assert.ThrowsAsync<IOException>(
            () => service.ImportAsync(bundlePath, resourceDirectory));
        Assert.Empty(
            Directory.EnumerateDirectories(
                resourceDirectory,
                ".SpinePet-Import-*"));
    }

    [Fact]
    public async Task ImportAsyncExtractsIconIntoSkinIconsDirectory()
    {
        string extractorPath =
            Path.Combine(_temporaryDirectory, "extract_spine_bundle.py");
        File.Copy(AppPaths.BundleExtractorScript, extractorPath);
        await File.WriteAllTextAsync(
            Path.Combine(_temporaryDirectory, "UnityPy.py"),
            """
            from pathlib import Path

            class FakeImage:
                width = 64
                height = 64

                def save(self, path, format):
                    Path(path).write_bytes(b"png")

            class FakeSprite:
                m_Name = "mi_c015_00_s"
                image = FakeImage()

            class FakeType:
                name = "Sprite"

            class FakeObject:
                type = FakeType()

                def read(self):
                    return FakeSprite()

            class FakeEnvironment:
                objects = [FakeObject()]

            def load(path):
                return FakeEnvironment()
            """);
        string bundlePath = Path.Combine(
            _temporaryDirectory,
            "c015_00_icons_author");
        await File.WriteAllBytesAsync(
            bundlePath,
            [0x55, 0x6E, 0x69, 0x74, 0x79, 0x46, 0x53, 0x00]);
        string resourceDirectory = Path.Combine(_temporaryDirectory, "res-icons");
        UnityBundleImportService service = new(
            extractorScriptPath: extractorPath);

        CharacterBundleImportResult result =
            await service.ImportAsync(bundlePath, resourceDirectory);

        string expectedDirectory = Path.Combine(
            resourceDirectory,
            "Anis Sparkling Summer",
            "00",
            CharacterResourceTypes.Icons);
        Assert.True(result.IsIcon);
        Assert.False(result.IsRenderable);
        Assert.Null(result.Resources);
        Assert.Equal(expectedDirectory, result.DestinationDirectory);
        Assert.Equal(
            Path.Combine(expectedDirectory, "c015_00_icon.png"),
            result.IconPath);
        Assert.True(File.Exists(result.IconPath));
        Assert.Empty(
            Directory.EnumerateDirectories(
                resourceDirectory,
                ".SpinePet-Import-*"));
    }

    [Fact]
    public void ImportSkeletonCopiesCompleteSetAndAcceptsAlreadyArchivedSource()
    {
        string sourceDirectory = Path.Combine(_temporaryDirectory, "incoming");
        Directory.CreateDirectory(sourceDirectory);
        string skeletonPath = Path.Combine(sourceDirectory, "c233_04.skel");
        string atlasPath = Path.Combine(sourceDirectory, "c233_04.atlas");
        string firstTexture = Path.Combine(sourceDirectory, "page-one.png");
        string secondTexture = Path.Combine(sourceDirectory, "page-two.png");
        WriteSkeletonHeader(skeletonPath, "4.1.24");
        File.WriteAllText(
            atlasPath,
            "page-one.png\nsize: 1,1\npage-two.png\nsize: 1,1\n");
        File.WriteAllBytes(firstTexture, [2]);
        File.WriteAllBytes(secondTexture, [3]);
        string resourceDirectory = Path.Combine(_temporaryDirectory, "res-skel");
        UnityBundleImportService service = new();

        CharacterBundleImportResult result = service.ImportSkeleton(
            skeletonPath,
            resourceDirectory,
            CharacterResourceTypes.Standing);

        string skinDirectory = Path.Combine(
            resourceDirectory,
            "Dorothy",
            "04");
        Assert.Equal(
            Path.Combine(skinDirectory, CharacterResourceTypes.Standing),
            result.DestinationDirectory);
        Assert.NotNull(result.Resources);
        Assert.Equal(
            2,
            1 + result.Resources.AdditionalTexturePaths.Count);
        Assert.True(File.Exists(skeletonPath));
        Assert.True(File.Exists(atlasPath));
        Assert.True(File.Exists(firstTexture));
        Assert.True(File.Exists(secondTexture));
        foreach (string resourceType in CharacterResourceTypes.All)
        {
            Assert.True(Directory.Exists(
                Path.Combine(skinDirectory, resourceType)));
        }

        CharacterBundleImportResult repeated = service.ImportSkeleton(
            result.Resources.SkeletonPath,
            resourceDirectory,
            CharacterResourceTypes.Standing);

        Assert.Equal(
            result.Resources.SkeletonPath,
            repeated.Resources?.SkeletonPath);
    }

    [Theory]
    [InlineData("aim")]
    [InlineData("cover")]
    public void ImportSkeletonRejectsRetiredResourceTypes(string resourceType)
    {
        string sourceDirectory = Path.Combine(
            _temporaryDirectory,
            "retired-state-incoming");
        Directory.CreateDirectory(sourceDirectory);
        string skeletonPath = Path.Combine(sourceDirectory, "c233_04.skel");

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => new UnityBundleImportService().ImportSkeleton(
                skeletonPath,
                Path.Combine(_temporaryDirectory, "res-retired-state"),
                resourceType));

        Assert.Contains("Only standing", exception.Message);
        Assert.False(Directory.Exists(Path.Combine(
            _temporaryDirectory,
            "res-retired-state")));
    }

    [Fact]
    public void ImportSkeletonRejectsSkeletonLocatedInRetiredStateDirectory()
    {
        string sourceDirectory = Path.Combine(_temporaryDirectory, "aim");
        Directory.CreateDirectory(sourceDirectory);
        string skeletonPath = Path.Combine(sourceDirectory, "c233_04.skel");

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => new UnityBundleImportService().ImportSkeleton(
                skeletonPath,
                Path.Combine(_temporaryDirectory, "res-retired-directory")));

        Assert.Contains("no longer supported", exception.Message);
        Assert.Contains("standing", exception.Message);
    }

    [Fact]
    public void ImportSkeletonRejectsCharacterMissingFromNameMap()
    {
        string sourceDirectory = Path.Combine(
            _temporaryDirectory,
            "unknown-incoming");
        Directory.CreateDirectory(sourceDirectory);
        string skeletonPath = Path.Combine(sourceDirectory, "c999_00.skel");
        File.WriteAllBytes(skeletonPath, []);
        File.WriteAllText(
            Path.Combine(sourceDirectory, "c999_00.atlas"),
            "c999_00.png\nsize: 1,1\n");
        File.WriteAllBytes(
            Path.Combine(sourceDirectory, "c999_00.png"),
            []);
        string resourceDirectory = Path.Combine(
            _temporaryDirectory,
            "res-unknown");
        CharacterIdentityService identityService = new(
            new Dictionary<string, string>());
        UnityBundleImportService service = new(
            identityService,
            new CharacterResourceDiscoveryService(identityService));

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => service.ImportSkeleton(skeletonPath, resourceDirectory));

        Assert.Contains("Character ID '999'", exception.Message);
        Assert.Contains("CharacterNames.json", exception.Message);
        Assert.False(Directory.Exists(resourceDirectory));
    }

    [Fact]
    public void ImportSkeletonRejectsInvalidMappedDirectoryName()
    {
        string sourceDirectory = Path.Combine(
            _temporaryDirectory,
            "invalid-name-incoming");
        Directory.CreateDirectory(sourceDirectory);
        string skeletonPath = Path.Combine(sourceDirectory, "c233_01.skel");
        File.WriteAllBytes(skeletonPath, []);
        File.WriteAllText(
            Path.Combine(sourceDirectory, "c233_01.atlas"),
            "c233_01.png\nsize: 1,1\n");
        File.WriteAllBytes(
            Path.Combine(sourceDirectory, "c233_01.png"),
            []);
        string resourceDirectory = Path.Combine(
            _temporaryDirectory,
            "res-invalid-name");
        CharacterIdentityService identityService = new(
            new Dictionary<string, string>
            {
                ["233"] = "Bad/Name"
            });
        UnityBundleImportService service = new(
            identityService,
            new CharacterResourceDiscoveryService(identityService));

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => service.ImportSkeleton(skeletonPath, resourceDirectory));

        Assert.Contains("cannot be used as a directory name", exception.Message);
        Assert.False(Directory.Exists(resourceDirectory));
    }

    [Fact]
    public void ImportSkeletonRejectsMappedNameWithSurroundingWhitespace()
    {
        string sourceDirectory = Path.Combine(
            _temporaryDirectory,
            "whitespace-name-incoming");
        Directory.CreateDirectory(sourceDirectory);
        string skeletonPath = Path.Combine(sourceDirectory, "c233_01.skel");
        WriteSkeletonHeader(skeletonPath, "4.1.24");
        File.WriteAllText(
            Path.Combine(sourceDirectory, "c233_01.atlas"),
            "c233_01.png\nsize: 1,1\n");
        File.WriteAllBytes(
            Path.Combine(sourceDirectory, "c233_01.png"),
            []);
        string resourceDirectory = Path.Combine(
            _temporaryDirectory,
            "res-whitespace-name");
        CharacterIdentityService identityService = new(
            new Dictionary<string, string>
            {
                ["233"] = " Dorothy "
            });
        UnityBundleImportService service = new(
            identityService,
            new CharacterResourceDiscoveryService(identityService));

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => service.ImportSkeleton(skeletonPath, resourceDirectory));

        Assert.Contains("cannot be used as a directory name", exception.Message);
        Assert.False(Directory.Exists(resourceDirectory));
    }

    [Fact]
    public void ImportSkeletonChecksRetiredDirectoriesForAnotherCharacterCode()
    {
        string sourceDirectory = Path.Combine(
            _temporaryDirectory,
            "duplicate-name-incoming");
        Directory.CreateDirectory(sourceDirectory);
        string skeletonPath = Path.Combine(sourceDirectory, "c511_00.skel");
        File.WriteAllBytes(skeletonPath, []);
        File.WriteAllText(
            Path.Combine(sourceDirectory, "c511_00.atlas"),
            "c511_00.png\nsize: 1,1\n");
        File.WriteAllBytes(
            Path.Combine(sourceDirectory, "c511_00.png"),
            []);
        string resourceDirectory = Path.Combine(
            _temporaryDirectory,
            "res-duplicate-name");
        string existingRetiredDirectory = Path.Combine(
            resourceDirectory,
            "Cinderella",
            "00",
            "aim",
            "legacy",
            "nested");
        Directory.CreateDirectory(existingRetiredDirectory);
        File.WriteAllBytes(
            Path.Combine(existingRetiredDirectory, "c515_00.skel"),
            []);
        CharacterIdentityService identityService = new(
            new Dictionary<string, string>
            {
                ["511"] = "Cinderella",
                ["515"] = "Cinderella"
            });
        UnityBundleImportService service = new(
            identityService,
            new CharacterResourceDiscoveryService(identityService));

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => service.ImportSkeleton(skeletonPath, resourceDirectory));

        Assert.Contains("character ID '515'", exception.Message);
        Assert.False(Directory.Exists(Path.Combine(
            resourceDirectory,
            "Cinderella",
            "00",
            CharacterResourceTypes.Standing)));
    }

    [Fact]
    public void ImportSkeletonRejectsNestedTargetReparsePoint()
    {
        string sourceDirectory = Path.Combine(
            _temporaryDirectory,
            "nested-reparse-incoming");
        Directory.CreateDirectory(sourceDirectory);
        string skeletonPath = Path.Combine(sourceDirectory, "c233_01.skel");
        WriteSkeletonHeader(skeletonPath, "4.1.24");
        File.WriteAllText(
            Path.Combine(sourceDirectory, "c233_01.atlas"),
            "c233_01.png\nsize: 1,1\n");
        File.WriteAllBytes(
            Path.Combine(sourceDirectory, "c233_01.png"),
            []);
        string resourceDirectory = Path.Combine(
            _temporaryDirectory,
            "res-nested-reparse");
        string legacyDirectory = Path.Combine(
            resourceDirectory,
            "Dorothy",
            "01",
            "legacy");
        string externalDirectory = Path.Combine(
            _temporaryDirectory,
            "import-external-target");
        string linkDirectory = Path.Combine(legacyDirectory, "nested-link");
        Directory.CreateDirectory(legacyDirectory);
        Directory.CreateDirectory(externalDirectory);
        string externalFile = Path.Combine(externalDirectory, "keep.txt");
        File.WriteAllText(externalFile, "keep");
        Directory.CreateSymbolicLink(linkDirectory, externalDirectory);

        try
        {
            InvalidDataException exception = Assert.Throws<InvalidDataException>(
                () => new UnityBundleImportService().ImportSkeleton(
                    skeletonPath,
                    resourceDirectory));

            Assert.Contains("reparse point", exception.Message);
            Assert.True(File.Exists(externalFile));
            Assert.False(Directory.Exists(Path.Combine(
                resourceDirectory,
                "Dorothy",
                "01",
                CharacterResourceTypes.Standing)));
        }
        finally
        {
            if (Directory.Exists(linkDirectory))
            {
                Directory.Delete(linkDirectory);
            }
        }
    }

    [Fact]
    public void ImportSkeletonRejectsUnsupportedSpineVersion()
    {
        string sourceDirectory = Path.Combine(
            _temporaryDirectory,
            "unsupported-version-incoming");
        Directory.CreateDirectory(sourceDirectory);
        string skeletonPath = Path.Combine(sourceDirectory, "c233_01.skel");
        WriteSkeletonHeader(skeletonPath, "4.0.47");
        File.WriteAllText(
            Path.Combine(sourceDirectory, "c233_01.atlas"),
            "c233_01.png\nsize: 1,1\n");
        File.WriteAllBytes(
            Path.Combine(sourceDirectory, "c233_01.png"),
            []);
        string resourceDirectory = Path.Combine(
            _temporaryDirectory,
            "res-unsupported-version");
        UnityBundleImportService service = new();

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => service.ImportSkeleton(skeletonPath, resourceDirectory));

        Assert.Contains("Skeleton version 4.0.47", exception.Message);
        Assert.Contains("Spine runtime 4.1", exception.Message);
        Assert.False(Directory.Exists(resourceDirectory));
    }

    [Fact]
    public async Task PreCanceledImportDoesNotStartExtractor()
    {
        string extractorPath =
            Path.Combine(_temporaryDirectory, "marker_extractor.py");
        await File.WriteAllTextAsync(
            extractorPath,
            """
            import argparse
            from pathlib import Path

            parser = argparse.ArgumentParser()
            parser.add_argument("--bundle", required=True)
            parser.add_argument("--resource-id", required=True)
            parser.add_argument("--resource-type", required=True)
            parser.add_argument("--output-directory", required=True)
            args = parser.parse_args()
            Path(f"{args.bundle}.started").write_text(
                "started",
                encoding="utf-8",
            )
            """);
        string bundlePath = Path.Combine(
            _temporaryDirectory,
            "c233_01_standing_pre_canceled");
        await File.WriteAllBytesAsync(
            bundlePath,
            [0x55, 0x6E, 0x69, 0x74, 0x79, 0x46, 0x53, 0x00]);
        string resourceDirectory =
            Path.Combine(_temporaryDirectory, "res-pre-canceled");
        UnityBundleImportService service = new(
            extractorScriptPath: extractorPath);
        using CancellationTokenSource cancellationSource = new();
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.ImportAsync(
                bundlePath,
                resourceDirectory,
                cancellationSource.Token));

        Assert.False(File.Exists($"{bundlePath}.started"));
        Assert.False(Directory.Exists(resourceDirectory));
    }

    [Fact]
    public async Task ImportCancellationStopsProcessTreeAndCleansWorkingDirectory()
    {
        string extractorPath =
            Path.Combine(_temporaryDirectory, "slow_extractor.py");
        await File.WriteAllTextAsync(
            extractorPath,
            """
            import argparse
            import os
            import subprocess
            import sys
            import time
            from pathlib import Path

            parser = argparse.ArgumentParser()
            parser.add_argument("--bundle", required=True)
            parser.add_argument("--resource-id", required=True)
            parser.add_argument("--resource-type", required=True)
            parser.add_argument("--output-directory", required=True)
            args = parser.parse_args()
            child = subprocess.Popen(
                [
                    sys.executable,
                    "-c",
                    "import time; time.sleep(60)",
                ],
                stdout=sys.stdout,
                stderr=sys.stderr,
            )
            Path(f"{args.bundle}.parent.pid").write_text(
                str(os.getpid()),
                encoding="utf-8",
            )
            Path(f"{args.bundle}.child.pid").write_text(
                str(child.pid),
                encoding="utf-8",
            )
            time.sleep(60)
            """);
        string bundlePath = Path.Combine(
            _temporaryDirectory,
            "c233_01_standing_slow");
        await File.WriteAllBytesAsync(
            bundlePath,
            [0x55, 0x6E, 0x69, 0x74, 0x79, 0x46, 0x53, 0x00]);
        string parentProcessIdPath =
            $"{bundlePath}.parent.pid";
        string childProcessIdPath =
            $"{bundlePath}.child.pid";
        string resourceDirectory =
            Path.Combine(_temporaryDirectory, "res");
        UnityBundleImportService service = new(
            extractorScriptPath: extractorPath);
        using CancellationTokenSource cancellationSource = new();
        Task<CharacterBundleImportResult>? importTask = null;
        int? parentProcessId = null;
        int? childProcessId = null;

        try
        {
            importTask = service.ImportAsync(
                bundlePath,
                resourceDirectory,
                cancellationSource.Token);
            using CancellationTokenSource processStartTimeout =
                new(ProcessTestTimeout);
            parentProcessId = await WaitForProcessIdAsync(
                parentProcessIdPath,
                processStartTimeout.Token);
            childProcessId = await WaitForProcessIdAsync(
                childProcessIdPath,
                processStartTimeout.Token);

            cancellationSource.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () => await importTask
                    .WaitAsync(
                        ProcessTestTimeout,
                        CancellationToken.None));

            using CancellationTokenSource processExitTimeout =
                new(ProcessTestTimeout);
            await Task.WhenAll(
                WaitForProcessExitAsync(
                    parentProcessId.Value,
                    processExitTimeout.Token),
                WaitForProcessExitAsync(
                    childProcessId.Value,
                    processExitTimeout.Token));
            Assert.False(IsProcessRunning(parentProcessId.Value));
            Assert.False(IsProcessRunning(childProcessId.Value));
            Assert.Empty(
                Directory.EnumerateDirectories(
                    resourceDirectory,
                    ".SpinePet-Import-*"));
        }
        finally
        {
            cancellationSource.Cancel();
            await TerminateKnownProcessAsync(childProcessId);
            await TerminateKnownProcessAsync(parentProcessId);
            if (importTask != null)
            {
                await ObserveImportTaskAsync(importTask);
            }
        }
    }

    private static async Task<int> WaitForProcessIdAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            try
            {
                string text = await File.ReadAllTextAsync(
                    filePath,
                    cancellationToken);
                if (int.TryParse(
                    text,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int processId) &&
                    processId > 0)
                {
                    return processId;
                }
            }
            catch (IOException)
            {
                // The script may still be creating or writing the PID file.
            }

            await Task.Delay(
                TimeSpan.FromMilliseconds(25),
                cancellationToken);
        }
    }

    private static void WriteSkeletonHeader(string path, string version)
    {
        byte[] versionBytes = System.Text.Encoding.UTF8.GetBytes(version);
        Assert.InRange(versionBytes.Length, 1, 126);
        using FileStream stream = File.Create(path);
        stream.Write(new byte[8]);
        stream.WriteByte((byte)(versionBytes.Length + 1));
        stream.Write(versionBytes);
    }

    private static async Task WaitForProcessExitAsync(
        int processId,
        CancellationToken cancellationToken)
    {
        while (IsProcessRunning(processId))
        {
            await Task.Delay(
                TimeSpan.FromMilliseconds(25),
                cancellationToken);
        }
    }

    private static bool IsProcessRunning(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static async Task TerminateKnownProcessAsync(
        int? processId)
    {
        if (processId == null)
        {
            return;
        }

        try
        {
            using Process process =
                Process.GetProcessById(processId.Value);
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            using CancellationTokenSource timeoutSource =
                new(ProcessCleanupTimeout);
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (ArgumentException)
        {
            // The process has already exited.
        }
        catch (InvalidOperationException)
        {
            // The process exited while it was being inspected.
        }
        catch (NotSupportedException)
        {
            // Best-effort cleanup must not hide the test's primary failure.
        }
        catch (Win32Exception)
        {
            // Best-effort cleanup must not hide the test's primary failure.
        }
        catch (OperationCanceledException)
        {
            // The cleanup attempt is deliberately bounded.
        }
    }

    private static async Task ObserveImportTaskAsync(
        Task<CharacterBundleImportResult> importTask)
    {
        Task observationTask = importTask.ContinueWith(
            completedTask =>
            {
                _ = completedTask.Exception;
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        try
        {
            await observationTask.WaitAsync(
                ProcessCleanupTimeout,
                CancellationToken.None);
        }
        catch (TimeoutException)
        {
            // Known processes were already terminated; keep cleanup bounded.
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }
}
