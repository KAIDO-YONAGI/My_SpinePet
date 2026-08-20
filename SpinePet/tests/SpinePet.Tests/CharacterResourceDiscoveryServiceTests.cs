using System.IO;
using SpinePet.Models;
using SpinePet.Services;

namespace SpinePet.Tests;

public sealed class CharacterResourceDiscoveryServiceTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "SpinePet.Tests",
        Guid.NewGuid().ToString("N"));

    public CharacterResourceDiscoveryServiceTests()
    {
        Directory.CreateDirectory(_temporaryDirectory);
    }

    [Fact]
    public void DiscoverReturnsCompleteCharactersInDeterministicOrder()
    {
        CreateCharacter("Zulu", "z");
        CreateCharacter(
            "Alpha",
            "a",
            includeAdditionalTexture: true,
            includeIcon: true);
        Directory.CreateDirectory(Path.Combine(_temporaryDirectory, "Incomplete"));

        CharacterResourceDiscoveryService service = new();
        IReadOnlyList<CharacterResourceFiles> resources =
            service.Discover(_temporaryDirectory);

        Assert.Equal(2, resources.Count);
        Assert.EndsWith(
            Path.Combine("Alpha", "a.skel"),
            resources[0].SkeletonPath,
            StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(
            Path.Combine("Zulu", "z.skel"),
            resources[1].SkeletonPath,
            StringComparison.OrdinalIgnoreCase);
        Assert.Single(resources[0].AdditionalTexturePaths);
        Assert.DoesNotContain(
            resources[0].AdditionalTexturePaths,
            path => path.EndsWith(
                "_icon.png",
                StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Alpha", resources[0].Identity.DisplayName);
        Assert.Equal(
            CharacterResourceTypes.Standing,
            resources[0].ResourceType);
    }

    [Fact]
    public void DiscoverGroupsSkinsAndIgnoresRetiredStateDirectories()
    {
        string characterDirectory =
            Path.Combine(_temporaryDirectory, "Anis Star");
        string skin00Directory = Path.Combine(characterDirectory, "00");
        string skin01Directory = Path.Combine(characterDirectory, "01");
        CreateResourceSet(
            Path.Combine(
                skin00Directory,
                CharacterResourceTypes.Standing),
            "c017_00");
        CreateResourceSet(
            Path.Combine(
                skin01Directory,
                CharacterResourceTypes.Standing),
            "c017_01");
        CreateResourceSet(
            Path.Combine(skin00Directory, "aim"),
            "c017_00");
        Directory.CreateDirectory(
            Path.Combine(skin00Directory, "cover"));
        string iconDirectory =
            Path.Combine(skin00Directory, CharacterResourceTypes.Icons);
        Directory.CreateDirectory(iconDirectory);
        File.WriteAllBytes(
            Path.Combine(iconDirectory, "c017_00_icon.png"),
            []);

        CharacterResourceDiscoveryService service = new();
        IReadOnlyList<CharacterResourceFiles> standing =
            service.Discover(_temporaryDirectory);
        IReadOnlyList<CharacterResourceFiles> all =
            service.DiscoverAll(_temporaryDirectory);

        Assert.Equal(2, standing.Count);
        Assert.Equal(2, all.Count);
        Assert.All(
            all,
            resource => Assert.Equal(
                CharacterResourceTypes.Standing,
                resource.ResourceType));
        Assert.DoesNotContain(
            all,
            resource => resource.SkeletonPath.Contains(
                $"{Path.DirectorySeparatorChar}aim{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DiscoverUsesCharacterLayerAsFallbackDisplayName()
    {
        string standingDirectory = Path.Combine(
            _temporaryDirectory,
            "Fallback Hero",
            "07",
            CharacterResourceTypes.Standing);
        CreateResourceSet(standingDirectory, "c999_07");
        CharacterIdentityService identityService = new(
            new Dictionary<string, string>());
        CharacterResourceDiscoveryService service = new(identityService);

        CharacterResourceFiles resource = Assert.Single(
            service.Discover(_temporaryDirectory));

        Assert.Equal("Fallback Hero", resource.Identity.DisplayName);
        Assert.Equal("07", resource.Identity.SkinCode);
    }

    [Fact]
    public void DiscoverAllReadsLegacyStandingButIgnoresLegacyAim()
    {
        string aimDirectory = Path.Combine(
            _temporaryDirectory,
            "Legacy Hero",
            "aim");
        CreateResourceSet(aimDirectory, "c999_03");
        string standingDirectory = Path.Combine(
            _temporaryDirectory,
            "Legacy Hero",
            CharacterResourceTypes.Standing);
        CreateResourceSet(standingDirectory, "c999_03");
        CharacterIdentityService identityService = new(
            new Dictionary<string, string>());
        CharacterResourceDiscoveryService service = new(identityService);

        CharacterResourceFiles resource = Assert.Single(
            service.DiscoverAll(_temporaryDirectory));

        Assert.Equal(CharacterResourceTypes.Standing, resource.ResourceType);
        Assert.Equal("Legacy Hero", resource.Identity.DisplayName);
        Assert.Equal("03", resource.Identity.SkinCode);
        Assert.Equal(standingDirectory, Path.GetDirectoryName(
            resource.SkeletonPath));
        Assert.Null(service.DiscoverForSkeleton(
            Path.Combine(aimDirectory, "c999_03.skel")));
    }

    [Fact]
    public void DiscoverForSkeletonUsesEveryExistingAtlasPage()
    {
        string directory = Path.Combine(_temporaryDirectory, "source");
        Directory.CreateDirectory(directory);
        string skeletonPath = Path.Combine(directory, "c999_02.skel");
        File.WriteAllBytes(skeletonPath, []);
        File.WriteAllText(
            Path.Combine(directory, "c999_02.atlas"),
            "page-a.png\nsize: 1,1\npage-b.png\nsize: 1,1\n");
        File.WriteAllBytes(Path.Combine(directory, "page-a.png"), []);
        File.WriteAllBytes(Path.Combine(directory, "page-b.png"), []);

        CharacterResourceDiscoveryService service = new();
        CharacterResourceFiles? resource =
            service.DiscoverForSkeleton(skeletonPath);

        Assert.NotNull(resource);
        Assert.EndsWith("page-a.png", resource.PrimaryTexturePath);
        Assert.Collection(
            resource.AdditionalTexturePaths,
            path => Assert.EndsWith("page-b.png", path));
    }

    [Fact]
    public void DiscoverForSkeletonRejectsNonSkeletonFiles()
    {
        string textPath = Path.Combine(_temporaryDirectory, "character.txt");
        File.WriteAllText(textPath, string.Empty);

        CharacterResourceDiscoveryService service = new();

        Assert.Null(service.DiscoverForSkeleton(textPath));
    }

    [Fact]
    public void DiscoverAllSkipsSkeletonsFromUnsupportedRuntimeVersions()
    {
        string standingDirectory = Path.Combine(
            _temporaryDirectory,
            "Old Export",
            "00",
            CharacterResourceTypes.Standing);
        CreateResourceSet(standingDirectory, "c999_00");
        WriteSkeletonHeader(
            Path.Combine(standingDirectory, "c999_00.skel"),
            "4.0.47");
        CharacterResourceDiscoveryService service = new(
            new CharacterIdentityService(
                new Dictionary<string, string>
                {
                    ["999"] = "Old Export"
                }));

        Assert.Empty(service.DiscoverAll(_temporaryDirectory));
        Assert.NotNull(service.DiscoverForSkeleton(Path.Combine(
            standingDirectory,
            "c999_00.skel")));
    }

    private void CreateCharacter(
        string directoryName,
        string fileName,
        bool includeAdditionalTexture = false,
        bool includeIcon = false)
    {
        string directory = Path.Combine(_temporaryDirectory, directoryName);
        Directory.CreateDirectory(directory);
        WriteSkeletonHeader(Path.Combine(directory, $"{fileName}.skel"));
        File.WriteAllText(Path.Combine(directory, $"{fileName}.atlas"), string.Empty);
        File.WriteAllBytes(Path.Combine(directory, $"{fileName}_a.png"), []);

        if (includeAdditionalTexture)
        {
            File.WriteAllBytes(Path.Combine(directory, $"{fileName}_b.png"), []);
        }

        if (includeIcon)
        {
            File.WriteAllBytes(
                Path.Combine(directory, $"{fileName}_icon.png"),
                []);
        }
    }

    private static void CreateResourceSet(
        string directory,
        string resourceName)
    {
        Directory.CreateDirectory(directory);
        WriteSkeletonHeader(
            Path.Combine(directory, $"{resourceName}.skel"));
        File.WriteAllText(
            Path.Combine(directory, $"{resourceName}.atlas"),
            string.Empty);
        File.WriteAllBytes(
            Path.Combine(directory, $"{resourceName}.png"),
            []);
    }

    private static void WriteSkeletonHeader(
        string path,
        string version = "4.1.24")
    {
        byte[] versionBytes = System.Text.Encoding.UTF8.GetBytes(version);
        using FileStream stream = File.Create(path);
        stream.Write(new byte[8]);
        stream.WriteByte((byte)(versionBytes.Length + 1));
        stream.Write(versionBytes);
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }
}
