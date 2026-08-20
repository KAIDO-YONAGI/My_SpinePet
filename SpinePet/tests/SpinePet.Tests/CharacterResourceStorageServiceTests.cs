using System.IO;
using SpinePet.Models;
using SpinePet.Services;

namespace SpinePet.Tests;

public sealed class CharacterResourceStorageServiceTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "SpinePet.Tests",
        Guid.NewGuid().ToString("N"));

    public CharacterResourceStorageServiceTests()
    {
        Directory.CreateDirectory(_temporaryDirectory);
    }

    [Fact]
    public void GetSkinDirectoryReturnsManagedSkinRoot()
    {
        string stateDirectory = Path.Combine(
            _temporaryDirectory,
            "Rapi",
            "00",
            CharacterResourceTypes.Standing);
        Directory.CreateDirectory(stateDirectory);
        CharacterResourceFiles resources = CreateResources(
            stateDirectory,
            "00");

        string result = CharacterResourceStorageService.GetSkinDirectory(
            resources,
            _temporaryDirectory);

        Assert.Equal(
            Path.Combine(_temporaryDirectory, "Rapi", "00"),
            result);
    }

    [Theory]
    [InlineData(".")]
    [InlineData("Rapi")]
    [InlineData("Rapi\\00\\standing")]
    [InlineData("..\\Outside\\00")]
    public void ValidateSkinDirectoryRejectsUnsafeScope(string relativePath)
    {
        string candidate = Path.GetFullPath(Path.Combine(
            _temporaryDirectory,
            relativePath));

        Assert.Throws<InvalidOperationException>(() =>
            CharacterResourceStorageService.ValidateSkinDirectory(
                candidate,
                _temporaryDirectory,
                "00",
                "010"));
    }

    [Fact]
    public void RecycleSkinDirectoryUsesValidatedExactTarget()
    {
        string skinDirectory = Path.Combine(
            _temporaryDirectory,
            "Rapi",
            "01");
        Directory.CreateDirectory(skinDirectory);
        string? recycledPath = null;
        CharacterResourceStorageService service = new(
            path => recycledPath = path);

        service.RecycleSkinDirectory(
            skinDirectory,
            _temporaryDirectory,
            "01",
            "010");

        Assert.Equal(Path.GetFullPath(skinDirectory), recycledPath);
    }

    [Fact]
    public void RecycleSkinDirectoryRejectsAnotherSkin()
    {
        string skinDirectory = Path.Combine(
            _temporaryDirectory,
            "Rapi",
            "01");
        Directory.CreateDirectory(skinDirectory);
        CharacterResourceStorageService service = new(_ =>
            throw new InvalidOperationException("must not be called"));

        Assert.Throws<InvalidOperationException>(() =>
            service.RecycleSkinDirectory(
                skinDirectory,
                _temporaryDirectory,
                "00",
                "010"));
    }

    [Fact]
    public void RecycleSkinDirectoryRejectsNestedMixedCharacterCodes()
    {
        string skinDirectory = Path.Combine(
            _temporaryDirectory,
            "Cinderella",
            "00");
        string standingDirectory = Path.Combine(
            skinDirectory,
            CharacterResourceTypes.Standing);
        string retiredAimDirectory = Path.Combine(
            skinDirectory,
            "aim",
            "legacy",
            "nested");
        Directory.CreateDirectory(standingDirectory);
        Directory.CreateDirectory(retiredAimDirectory);
        File.WriteAllBytes(
            Path.Combine(standingDirectory, "c511_00.skel"),
            []);
        File.WriteAllBytes(
            Path.Combine(retiredAimDirectory, "c515_00.skel"),
            []);
        CharacterResourceStorageService service = new(_ =>
            throw new InvalidOperationException("must not be called"));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => service.RecycleSkinDirectory(
                skinDirectory,
                _temporaryDirectory,
                "00",
                "511"));

        Assert.Contains("character ID '515'", exception.Message);
    }

    [Fact]
    public void RecycleSkinDirectoryRejectsNestedReparsePoint()
    {
        string skinDirectory = Path.Combine(
            _temporaryDirectory,
            "Rapi",
            "02");
        string legacyDirectory = Path.Combine(skinDirectory, "legacy");
        string externalDirectory = Path.Combine(
            _temporaryDirectory,
            "external-target");
        string linkDirectory = Path.Combine(legacyDirectory, "nested-link");
        Directory.CreateDirectory(legacyDirectory);
        Directory.CreateDirectory(externalDirectory);
        string externalFile = Path.Combine(externalDirectory, "keep.txt");
        File.WriteAllText(externalFile, "keep");
        Directory.CreateSymbolicLink(linkDirectory, externalDirectory);
        CharacterResourceStorageService service = new(_ =>
            throw new InvalidOperationException("must not be called"));

        try
        {
            InvalidOperationException exception =
                Assert.Throws<InvalidOperationException>(() =>
                    service.RecycleSkinDirectory(
                        skinDirectory,
                        _temporaryDirectory,
                        "02",
                        "010"));

            Assert.Contains("reparse point", exception.Message);
            Assert.True(File.Exists(externalFile));
        }
        finally
        {
            if (Directory.Exists(linkDirectory))
            {
                Directory.Delete(linkDirectory);
            }
        }
    }

    private static CharacterResourceFiles CreateResources(
        string stateDirectory,
        string skinCode)
    {
        string resourceName = $"c010_{skinCode}";
        return new CharacterResourceFiles(
            Path.Combine(stateDirectory, $"{resourceName}.skel"),
            Path.Combine(stateDirectory, $"{resourceName}.atlas"),
            Path.Combine(stateDirectory, $"{resourceName}.png"),
            [],
            CharacterResourceTypes.Standing,
            new CharacterIdentity(
                resourceName,
                "010",
                skinCode,
                "Rapi"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }
}
