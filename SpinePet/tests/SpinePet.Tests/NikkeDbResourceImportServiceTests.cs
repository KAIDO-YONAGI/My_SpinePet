using System.IO;
using System.Text.Json;
using SpinePet.Models;
using SpinePet.Services;

namespace SpinePet.Tests;

public sealed class NikkeDbResourceImportServiceTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "SpinePet.Tests",
        Guid.NewGuid().ToString("N"));

    public NikkeDbResourceImportServiceTests()
    {
        Directory.CreateDirectory(_temporaryDirectory);
    }

    [Fact]
    public void ImportUsesExactRenameMapIdAndCreatesUnifiedLayout()
    {
        string nikkedb = CreateDatabase(
            "c017_01",
            "Anis Star",
            includeAim: true,
            includeCover: true);
        string destinationRoot = Path.Combine(_temporaryDirectory, "res");
        NikkeDbResourceImportService service = CreateService();

        NikkeDbImportResult result = service.Import(
            "C017_01",
            nikkedb,
            destinationRoot);

        Assert.Equal("c017_01", result.ResourceId);
        Assert.True(result.BattleImported);
        Assert.EndsWith(
            Path.Combine("Anis Star", "c017_01"),
            result.DestinationDirectory,
            StringComparison.OrdinalIgnoreCase);
        foreach (string resourceType in CharacterResourceTypes.All)
        {
            Assert.True(Directory.Exists(Path.Combine(
                result.DestinationDirectory,
                resourceType)));
        }
        Assert.True(File.Exists(Path.Combine(
            result.DestinationDirectory,
            CharacterResourceTypes.Aim,
            "c017_01_aim.skel")));
        Assert.True(File.Exists(Path.Combine(
            result.DestinationDirectory,
            CharacterResourceTypes.Cover,
            "c017_01_cover.skel")));
    }

    [Fact]
    public void ImportRejectsUnknownOrPartialIds()
    {
        string nikkedb = CreateDatabase(
            "c017_01",
            "Anis Star",
            includeAim: false,
            includeCover: false);
        NikkeDbResourceImportService service = CreateService();

        InvalidOperationException exception = Assert.Throws<
            InvalidOperationException>(() => service.Import(
                "c017",
                nikkedb,
                Path.Combine(_temporaryDirectory, "res")));

        Assert.Contains("was not found", exception.Message);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void ImportCopiesOnlyStandingWhenBattlePairIsIncomplete(
        bool includeAim,
        bool includeCover)
    {
        string nikkedb = CreateDatabase(
            "c017_01",
            "Anis Star",
            includeAim,
            includeCover);
        NikkeDbImportResult result = CreateService().Import(
            "c017_01",
            nikkedb,
            Path.Combine(_temporaryDirectory, "res"));

        Assert.False(result.BattleImported);
        Assert.Single(Directory.EnumerateFiles(
            Path.Combine(
                result.DestinationDirectory,
                CharacterResourceTypes.Standing),
            "*.skel"));
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(
            result.DestinationDirectory,
            CharacterResourceTypes.Aim)));
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(
            result.DestinationDirectory,
            CharacterResourceTypes.Cover)));
    }

    [Fact]
    public void ImportPreservesEveryAtlasTexturePage()
    {
        string nikkedb = CreateDatabase(
            "c017_01",
            "Anis Star",
            includeAim: true,
            includeCover: true,
            standingPages: 2,
            aimPages: 2,
            coverPages: 3);
        NikkeDbImportResult result = CreateService().Import(
            "c017_01",
            nikkedb,
            Path.Combine(_temporaryDirectory, "res"));
        CharacterResourceDiscoveryService discovery = new(
            new CharacterIdentityService(new Dictionary<string, string>()));
        IReadOnlyList<CharacterResourceFiles> resources =
            discovery.DiscoverAll(
                Path.Combine(_temporaryDirectory, "res"));

        Assert.Equal(3, resources.Count);
        Assert.Single(
            Assert.Single(
                resources,
                resource =>
                    resource.ResourceType ==
                    CharacterResourceTypes.Standing)
                .AdditionalTexturePaths);
        Assert.Single(
            Assert.Single(
                resources,
                resource =>
                    resource.ResourceType == CharacterResourceTypes.Aim)
                .AdditionalTexturePaths);
        Assert.Equal(
            2,
            Assert.Single(
                resources,
                resource =>
                    resource.ResourceType == CharacterResourceTypes.Cover)
                .AdditionalTexturePaths.Count);
        Assert.True(result.BattleImported);
    }

    [Fact]
    public void ImportRefusesExistingFilesAndRollsBackNewCopies()
    {
        string nikkedb = CreateDatabase(
            "c017_01",
            "Anis Star",
            includeAim: true,
            includeCover: true);
        string destinationRoot = Path.Combine(_temporaryDirectory, "res");
        NikkeDbResourceImportService service = CreateService();
        NikkeDbImportResult first = service.Import(
            "c017_01",
            nikkedb,
            destinationRoot);
        string existingSkeleton = first.Standing.SkeletonPath;

        IOException exception = Assert.Throws<IOException>(() =>
            service.Import("c017_01", nikkedb, destinationRoot));

        Assert.Contains("already contains", exception.Message);
        Assert.True(File.Exists(existingSkeleton));
        Assert.Single(Directory.EnumerateFiles(
            Path.GetDirectoryName(existingSkeleton)!,
            "*.skel"));
    }

    private string CreateDatabase(
        string id,
        string displayName,
        bool includeAim,
        bool includeCover,
        int standingPages = 1,
        int aimPages = 1,
        int coverPages = 1)
    {
        string database = Path.Combine(
            _temporaryDirectory,
            Guid.NewGuid().ToString("N"),
            "nikkedb");
        string relativePath = Path.Combine("mapped", "entry");
        string source = Path.Combine(database, "l2d", relativePath);
        Directory.CreateDirectory(source);
        string indexDirectory = Path.Combine(database, "data", "indexes");
        Directory.CreateDirectory(indexDirectory);
        File.WriteAllText(
            Path.Combine(indexDirectory, "rename-map.json"),
            JsonSerializer.Serialize(new
            {
                entries = new[]
                {
                    new
                    {
                        id,
                        displayName,
                        currentRelativePath = relativePath
                    }
                }
            }));
        CreateResourceSet(source, id, standingPages);
        if (includeAim)
        {
            CreateResourceSet(
                Path.Combine(source, CharacterResourceTypes.Aim),
                $"{id}_aim",
                aimPages);
        }
        if (includeCover)
        {
            CreateResourceSet(
                Path.Combine(source, CharacterResourceTypes.Cover),
                $"{id}_cover",
                coverPages);
        }

        return database;
    }

    private static void CreateResourceSet(
        string directory,
        string stem,
        int pageCount)
    {
        Directory.CreateDirectory(directory);
        WriteSkeletonHeader(Path.Combine(directory, $"{stem}.skel"));
        string[] pages = Enumerable
            .Range(1, pageCount)
            .Select(index => $"{stem}_{index}.png")
            .ToArray();
        File.WriteAllText(
            Path.Combine(directory, $"{stem}.atlas"),
            string.Join(
                Environment.NewLine,
                pages.Select(page => $"{page}{Environment.NewLine}size: 1,1")));
        foreach (string page in pages)
        {
            File.WriteAllBytes(Path.Combine(directory, page), []);
        }
    }

    private static void WriteSkeletonHeader(string path)
    {
        byte[] versionBytes =
            System.Text.Encoding.UTF8.GetBytes("4.1.24");
        using FileStream stream = File.Create(path);
        stream.Write(new byte[8]);
        stream.WriteByte((byte)(versionBytes.Length + 1));
        stream.Write(versionBytes);
    }

    private static NikkeDbResourceImportService CreateService() =>
        new(new CharacterResourceDiscoveryService(
            new CharacterIdentityService(
                new Dictionary<string, string>())));

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }
}
