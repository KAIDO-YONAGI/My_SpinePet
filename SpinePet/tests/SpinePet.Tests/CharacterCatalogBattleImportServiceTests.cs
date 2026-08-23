using SpinePet.Models;
using SpinePet.Services;

namespace SpinePet.Tests;

public sealed class CharacterCatalogBattleImportServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"SpinePet-Catalog-{Guid.NewGuid():N}");

    [Fact]
    public void AuditRequiresAllThreeExactStatesAndSupportsNestedLayouts()
    {
        string source = Path.Combine(_root, "source");
        CreateResource(
            source,
            "Complete",
            "Standing",
            "Sprite Sheet",
            "c100_01",
            CharacterResourceTypes.Standing);
        CreateResource(
            source,
            "Complete",
            "Aim",
            "Sprite Sheet",
            "c100_aim_01",
            CharacterResourceTypes.Aim);
        CreateResource(
            source,
            "Complete",
            "Cover",
            "Sprite Sheet",
            "c100_cover_01",
            CharacterResourceTypes.Cover);
        CreateResource(
            source,
            "Complete",
            "Aim (Chinese Censored Version)",
            "Sprite Sheet",
            "c999_aim_01",
            CharacterResourceTypes.Aim);
        CreateResource(
            source,
            "StandingOnly",
            null,
            null,
            "c200_00",
            CharacterResourceTypes.Standing);

        CharacterCatalogAudit audit = CreateService().Audit(source);

        CharacterCatalogBattleSet complete = Assert.Single(
            audit.CompleteSets);
        Assert.Equal("Complete", complete.SourceName);
        Assert.Contains(
            $"{Path.DirectorySeparatorChar}Aim{Path.DirectorySeparatorChar}",
            complete.Aim.SkeletonPath);
        Assert.Equal(2, audit.SourceDirectoryCount);
        Assert.Single(audit.SkippedEntries);
    }

    [Fact]
    public void ImportAddsNewIndependentSkinAndIsIdempotent()
    {
        string source = Path.Combine(_root, "source");
        string destination = Path.Combine(_root, "destination");
        CreateBattleSet(source, "Variant", "c100_01");
        CharacterIdentityService identities = new(
            new Dictionary<string, string>
            {
                ["100"] = "Base",
                ["10001"] = "Variant"
            });
        CharacterCatalogBattleImportService service = CreateService(
            identities);

        CharacterCatalogBattleImportResult first = service.Import(
            source,
            destination);
        CharacterCatalogBattleImportResult second = service.Import(
            source,
            destination);

        Assert.Equal(1, first.AddedCharacterCount);
        Assert.Equal(1, first.ImportedBattleCount);
        Assert.Equal(0, first.AlreadyPresentCount);
        Assert.Equal(0, second.AddedCharacterCount);
        Assert.Equal(0, second.ImportedBattleCount);
        Assert.Equal(1, second.AlreadyPresentCount);
        string skin = Path.Combine(destination, "Variant", "01");
        Assert.True(File.Exists(Path.Combine(
            skin,
            CharacterResourceTypes.Standing,
            "c10001_01.skel")));
        Assert.True(File.Exists(Path.Combine(
            skin,
            CharacterResourceTypes.Aim,
            "c10001_01_aim.skel")));
        string atlas = File.ReadAllText(Path.Combine(
            skin,
            CharacterResourceTypes.Aim,
            "c10001_01_aim.atlas"));
        Assert.Contains("c10001_01_aim.png", atlas);
    }

    [Fact]
    public void ImportMatchesExistingDisplayDirectoryBeforeIdentity()
    {
        string source = Path.Combine(_root, "source");
        string destination = Path.Combine(_root, "destination");
        CreateBattleSet(source, "Custom Display", "c100_01");
        CreateResource(
            destination,
            "Custom Display",
            "standing",
            null,
            "CustomStanding",
            CharacterResourceTypes.Standing);
        CharacterCatalogBattleImportService service = CreateService();

        CharacterCatalogBattleImportResult result = service.Import(
            source,
            destination);
        CharacterCatalogBattleImportResult second = service.Import(
            source,
            destination);

        Assert.Equal(0, result.AddedCharacterCount);
        Assert.Equal(1, result.ImportedBattleCount);
        Assert.Equal(1, second.AlreadyPresentCount);
        Assert.True(Directory.Exists(Path.Combine(
            destination,
            "Custom Display",
            CharacterResourceTypes.Aim)));
        string atlas = File.ReadAllText(Path.Combine(
            destination,
            "Custom Display",
            CharacterResourceTypes.Aim,
            "c100_01_aim.atlas"));
        Assert.Contains("c100_01_aim.png", atlas);
        Assert.False(Directory.Exists(Path.Combine(
            destination,
            "Custom Display",
            "01")));
    }

    [Fact]
    public void ImportSkipsIncompleteBattleSet()
    {
        string source = Path.Combine(_root, "source");
        string destination = Path.Combine(_root, "destination");
        CreateResource(
            source,
            "Partial",
            null,
            null,
            "c200_00",
            CharacterResourceTypes.Standing);
        CreateResource(
            source,
            "Partial",
            "aim",
            null,
            "c200_aim_00",
            CharacterResourceTypes.Aim);

        CharacterCatalogBattleImportResult result =
            CreateService().Import(source, destination);

        Assert.Equal(0, result.CompleteSetCount);
        Assert.Empty(Directory.EnumerateFileSystemEntries(destination));
        CharacterCatalogSkippedEntry skipped =
            Assert.Single(result.SkippedEntries);
        Assert.True(skipped.HasStanding);
        Assert.True(skipped.HasAim);
        Assert.False(skipped.HasCover);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static CharacterCatalogBattleImportService CreateService(
        CharacterIdentityService? identities = null)
    {
        identities ??= new CharacterIdentityService(
            new Dictionary<string, string>());
        return new CharacterCatalogBattleImportService(
            identities,
            new CharacterResourceDiscoveryService(identities),
            _ => { });
    }

    private static void CreateBattleSet(
        string root,
        string name,
        string standingStem)
    {
        CreateResource(
            root,
            name,
            null,
            null,
            standingStem,
            CharacterResourceTypes.Standing);
        CreateResource(
            root,
            name,
            "aim",
            null,
            $"{standingStem}_aim",
            CharacterResourceTypes.Aim);
        CreateResource(
            root,
            name,
            "cover",
            null,
            $"{standingStem}_cover",
            CharacterResourceTypes.Cover);
    }

    private static void CreateResource(
        string root,
        string character,
        string? stateDirectory,
        string? nestedDirectory,
        string stem,
        string state)
    {
        string directory = Path.Combine(root, character);
        if (stateDirectory != null)
        {
            directory = Path.Combine(directory, stateDirectory);
        }
        if (nestedDirectory != null)
        {
            directory = Path.Combine(directory, nestedDirectory);
        }

        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, $"{stem}.skel"), [1]);
        File.WriteAllText(
            Path.Combine(directory, $"{stem}.atlas"),
            $"{stem}.png{Environment.NewLine}");
        File.WriteAllBytes(Path.Combine(directory, $"{stem}.png"), [1]);
        _ = state;
    }
}
