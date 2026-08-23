using System.IO;
using System.Windows;
using SpinePet.Models;
using SpinePet.Services;
using SpinePet.Tests.TestDoubles;

namespace SpinePet.Tests;

public sealed class CharacterBattleConfigFactoryTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "SpinePet.Tests",
        Guid.NewGuid().ToString("N"));

    public CharacterBattleConfigFactoryTests()
    {
        Directory.CreateDirectory(_temporaryDirectory);
    }

    [Fact]
    public void ResolveAnimationsUsesSingleReloadBeforeSequenceFallback()
    {
        CharacterBattleAnimationsConfig single =
            CharacterBattleConfigFactory.ResolveAnimations(
                [
                    "idle",
                    "to_aim",
                    "fire",
                    "aim_fire_hip",
                    "aim_fire_hair"
                ],
                ["idle", "to_cover", "cover_reload",
                    "reload_start", "reload_loop", "reload_end"]);
        CharacterBattleAnimationsConfig sequence =
            CharacterBattleConfigFactory.ResolveAnimations(
                ["aim_idle"],
                ["cover_idle", "reload_end", "reload_start", "reload_loop"]);

        Assert.Equal("idle", single.AimIdle);
        Assert.Equal("to_aim", single.ToAim);
        Assert.Equal("fire", single.AimFire);
        Assert.Equal(
            ["aim_fire_hair", "aim_fire_hip"],
            single.AimFireEffects);
        Assert.Equal("idle", single.CoverIdle);
        Assert.Equal("to_cover", single.ToCover);
        Assert.Equal(["cover_reload"], single.ReloadSequence);
        Assert.Equal(
            ["reload_start", "reload_loop", "reload_end"],
            sequence.ReloadSequence);
    }

    [Fact]
    public void ResolveAnimationsSkipsEmptyFireEffectPlaceholders()
    {
        CharacterBattleAnimationsConfig animations =
            CharacterBattleConfigFactory.ResolveAnimations(
                [
                    new BattleAnimationMetadata("aim_idle", true),
                    new BattleAnimationMetadata("aim_fire", true),
                    new BattleAnimationMetadata("aim_fire_hair", false),
                    new BattleAnimationMetadata("aim_fire_hip", true)
                ],
                [
                    new BattleAnimationMetadata("cover_idle", true)
                ]);

        Assert.Equal("aim_fire", animations.AimFire);
        Assert.Equal(["aim_fire_hip"], animations.AimFireEffects);
    }

    [Fact]
    public void RealC01701ImportsAndBuildsCompleteBattleProfile()
    {
        string workspaceRoot = FindWorkspaceRoot();
        string nikkedb = Path.Combine(
            workspaceRoot,
            "resources",
            "nikkedb");
        CharacterResourceDiscoveryService discovery = new();
        NikkeDbImportResult imported =
            new NikkeDbResourceImportService(discovery).Import(
                "c017_01",
                nikkedb,
                Path.Combine(_temporaryDirectory, "res"));
        IReadOnlyList<CharacterResourceFiles> resources =
            discovery.DiscoverAll(Path.Combine(_temporaryDirectory, "res"));
        CharacterResourceFiles standing = Assert.Single(
            resources,
            resource =>
                resource.ResourceType == CharacterResourceTypes.Standing);

        CharacterBattleConfig? battle =
            CharacterBattleConfigFactory.TryCreate(standing, resources);

        Assert.True(imported.BattleImported);
        Assert.NotNull(battle);
        Assert.Equal("aim_idle", battle.Animations.AimIdle);
        Assert.Equal("to_aim", battle.Animations.ToAim);
        Assert.Equal("aim_fire", battle.Animations.AimFire);
        Assert.Null(battle.Animations.AimFireEffects);
        Assert.Equal("cover_idle", battle.Animations.CoverIdle);
        Assert.Equal("to_cover", battle.Animations.ToCover);
        Assert.Equal(["cover_reload"], battle.Animations.ReloadSequence);
        Assert.True(battle.Aim.Exists());
        Assert.True(battle.Cover.Exists());
    }

    [Fact]
    public void ScanBackfillsRemovesAndNormalizesBattleProfile()
    {
        string workspaceRoot = FindWorkspaceRoot();
        string resourceRoot = Path.Combine(_temporaryDirectory, "scan-res");
        CharacterResourceDiscoveryService discovery = new();
        new NikkeDbResourceImportService(discovery).Import(
            "c017_01",
            Path.Combine(workspaceRoot, "resources", "nikkedb"),
            resourceRoot);
        CharacterResourceFiles[] complete =
            discovery.DiscoverAll(resourceRoot).ToArray();
        ConfigService configService = new(Path.Combine(
            _temporaryDirectory,
            "scan-config.json"));
        CharacterManager manager = new(
            configService,
            new CharacterIdentityService(
                new Dictionary<string, string> { ["017"] = "Anis" }),
            new FakeCharacterRenderHost(),
            new Rect(0, 0, 1920, 1080));

        CharacterResourceSynchronizationResult added =
            manager.SynchronizeResources(complete, resourceRoot);
        CharacterConfig character = Assert.Single(manager.Characters);
        Assert.True(added.HasChanges);
        Assert.NotNull(character.Battle);

        CharacterResourceSynchronizationResult removed =
            manager.SynchronizeResources(
                complete.Where(resource =>
                    resource.ResourceType != CharacterResourceTypes.Cover),
                resourceRoot);
        Assert.True(removed.HasChanges);
        Assert.Null(character.Battle);

        int savesAfterRemoval = configService.SaveRequestCount;
        CharacterResourceSynchronizationResult repeated =
            manager.SynchronizeResources(
                complete.Where(resource =>
                    resource.ResourceType != CharacterResourceTypes.Cover),
                resourceRoot);
        Assert.False(repeated.HasChanges);
        Assert.Equal(savesAfterRemoval, configService.SaveRequestCount);
    }

    [Fact]
    public void InstalledResourcesDetectOnlyNonEmptyFireEffects()
    {
        string repositoryRoot = FindRepositoryRoot();
        string resourceRoot = Path.Combine(repositoryRoot, "res");
        if (!Directory.Exists(resourceRoot))
            return;

        IReadOnlyList<CharacterResourceFiles> resources =
            new CharacterResourceDiscoveryService()
                .DiscoverAll(resourceRoot);
        Dictionary<string, string[]> detected = resources
            .Where(resource =>
                resource.ResourceType == CharacterResourceTypes.Standing)
            .Select(standing => new
            {
                Profile = GetProfilePath(resourceRoot, standing),
                Battle = CharacterBattleConfigFactory.TryCreate(
                    standing,
                    resources)
            })
            .Where(item =>
                item.Battle?.Animations.AimFireEffects?.Count > 0)
            .ToDictionary(
                item => item.Profile,
                item => item.Battle!.Animations.AimFireEffects!.ToArray(),
                StringComparer.OrdinalIgnoreCase);

        Assert.Equal(6, detected.Count);
        Assert.Equal(
            ["aim_fire_hair"],
            detected["Blanc - White Rabbit"]);
        Assert.Equal(
            ["aim_fire_hair"],
            detected[Path.Combine("Blanc Variant 03", "03")]);
        Assert.Equal(
            ["aim_fire_hair"],
            detected[Path.Combine("Cinderella Crystal Wave", "00")]);
        Assert.Equal(
            ["aim_fire_hair"],
            detected[Path.Combine("Laplace Neo", "00")]);
        Assert.Equal(
            ["aim_fire_hair"],
            detected[Path.Combine("Laplace Neo Variant01", "01")]);
        Assert.Equal(
            ["aim_fire_hair", "aim_fire_hip"],
            detected[Path.Combine("Sugar - Wild Backyard", "02")]);
    }

    private static string GetProfilePath(
        string resourceRoot,
        CharacterResourceFiles standing)
    {
        string stateDirectory = Path.GetDirectoryName(
            standing.SkeletonPath) ??
            throw new InvalidOperationException(
                "Standing skeleton has no state directory.");
        string profileDirectory = Path.GetDirectoryName(stateDirectory) ??
            throw new InvalidOperationException(
                "Standing state has no profile directory.");
        return Path.GetRelativePath(resourceRoot, profileDirectory);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null &&
               !File.Exists(Path.Combine(directory.FullName, "SpinePet.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ??
               throw new DirectoryNotFoundException(
                   "Repository root not found.");
    }

    private static string FindWorkspaceRoot()
    {
        foreach (string start in new[]
                 {
                     AppContext.BaseDirectory,
                     Environment.CurrentDirectory
                 })
        {
            DirectoryInfo? directory = new(Path.GetFullPath(start));
            while (directory != null)
            {
                if (File.Exists(Path.Combine(
                        directory.FullName,
                        "resources",
                        "nikkedb",
                        "data",
                        "indexes",
                        "rename-map.json")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        throw new DirectoryNotFoundException("Workspace root not found.");
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }
}
