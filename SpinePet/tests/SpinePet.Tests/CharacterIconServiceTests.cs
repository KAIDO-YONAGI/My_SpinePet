using System.IO;
using SpinePet.Models;
using SpinePet.Services;

namespace SpinePet.Tests;

public sealed class CharacterIconServiceTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "SpinePet.Tests",
        Guid.NewGuid().ToString("N"));

    public CharacterIconServiceTests()
    {
        Directory.CreateDirectory(_temporaryDirectory);
    }

    [Fact]
    public void GetThumbnailPathPrefersDownloadedIcon()
    {
        string characterDirectory =
            Path.Combine(_temporaryDirectory, "Anis Star");
        string skinDirectory = Path.Combine(characterDirectory, "00");
        string standingDirectory = Path.Combine(
            skinDirectory,
            CharacterResourceTypes.Standing);
        string iconDirectory = Path.Combine(
            skinDirectory,
            CharacterResourceTypes.Icons);
        Directory.CreateDirectory(standingDirectory);
        Directory.CreateDirectory(iconDirectory);
        string iconPath = Path.Combine(
            iconDirectory,
            "c017_00_icon.png");
        File.WriteAllBytes(iconPath, []);
        CharacterConfig character = new()
        {
            SkeletonPath = Path.Combine(
                standingDirectory,
                "c017_00.skel"),
            TexturePath = Path.Combine(
                standingDirectory,
                "c017_00.png")
        };
        CharacterIdentity identity = new(
            "c017_00",
            "017",
            "00",
            "Anis Star");

        string thumbnailPath =
            CharacterIconService.GetThumbnailPath(character, identity);

        Assert.Equal(iconPath, thumbnailPath);
    }

    [Fact]
    public void GetThumbnailPathFallsBackToCharacterTexture()
    {
        CharacterConfig character = new()
        {
            SkeletonPath = Path.Combine(
                _temporaryDirectory,
                "c010_00.skel"),
            TexturePath = Path.Combine(
                _temporaryDirectory,
                "c010_00.png")
        };
        CharacterIdentity identity = new(
            "c010_00",
            "010",
            "00",
            "Rapi");

        string thumbnailPath =
            CharacterIconService.GetThumbnailPath(character, identity);

        Assert.Equal(character.TexturePath, thumbnailPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }
}
