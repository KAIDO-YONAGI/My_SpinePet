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
        Assert.Null(single.BattleEffects);
        Assert.Equal("idle", single.CoverIdle);
        Assert.Equal("to_cover", single.ToCover);
        Assert.Equal(["cover_reload"], single.ReloadSequence);
        Assert.Equal(
            ["reload_start", "reload_loop", "reload_end"],
            sequence.ReloadSequence);
    }

    [Fact]
    public void ResolveAnimationsDoesNotGuessFireEffectsFromNames()
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
        Assert.Null(animations.BattleEffects);
    }

    [Fact]
    public void RealC01701ResourcesBuildCompleteBattleProfile()
    {
        string resourceRoot = Path.Combine(_temporaryDirectory, "res");
        StageNikkeDbResource(resourceRoot);
        CharacterResourceDiscoveryService discovery = new();
        IReadOnlyList<CharacterResourceFiles> resources =
            discovery.DiscoverAll(resourceRoot);
        CharacterResourceFiles standing = Assert.Single(
            resources,
            resource =>
                resource.ResourceType == CharacterResourceTypes.Standing);

        CharacterBattleConfig? battle =
            CharacterBattleConfigFactory.TryCreate(standing, resources);

        Assert.NotNull(battle);
        Assert.Equal("aim_idle", battle.Animations.AimIdle);
        Assert.Equal("to_aim", battle.Animations.ToAim);
        Assert.Null(battle.Animations.AimFire);
        Assert.Null(battle.Animations.BattleEffects);
        CharacterBattleLayerConfig fireLayer = Assert.Single(
            battle.Animations.AimFireLayers!);
        Assert.Equal("aim_fire", fireLayer.Animation);
        Assert.Equal(73, fireLayer.ExcludeTimelines?.Count);
        Assert.Equal("cover_idle", battle.Animations.CoverIdle);
        Assert.Equal("to_cover", battle.Animations.ToCover);
        Assert.Equal(["cover_reload"], battle.Animations.ReloadSequence);
        Assert.True(battle.Aim.Exists());
        Assert.True(battle.Cover.Exists());
    }

    [Fact]
    public void ScanBackfillsRemovesAndNormalizesBattleProfile()
    {
        string resourceRoot = Path.Combine(_temporaryDirectory, "scan-res");
        StageNikkeDbResource(resourceRoot);
        CharacterResourceDiscoveryService discovery = new();
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
    public void InstalledResourcesProduceCompleteAuditedFireLayerProfiles()
    {
        string repositoryRoot = FindRepositoryRoot();
        string resourceRoot = Path.Combine(repositoryRoot, "res");
        if (!Directory.Exists(resourceRoot))
            return;

        IReadOnlyList<CharacterResourceFiles> resources =
            new CharacterResourceDiscoveryService()
                .DiscoverAll(resourceRoot);
        Dictionary<string, CharacterBattleConfig> profiles = resources
            .Where(resource =>
                resource.ResourceType == CharacterResourceTypes.Standing)
            .Select(standing => new
            {
                Profile = GetProfilePath(resourceRoot, standing),
                Battle = CharacterBattleConfigFactory.TryCreate(
                    standing,
                    resources)
            })
            .Where(item => item.Battle != null)
            .ToDictionary(
                item => item.Profile,
                item => item.Battle!,
                StringComparer.OrdinalIgnoreCase);

        Assert.Equal(41, profiles.Count);
        CharacterBattleConfig[] configured = profiles.Values
            .Where(profile =>
                profile.Animations.AimFireLayers?.Count > 0)
            .ToArray();
        Assert.Equal(40, configured.Length);
        Assert.Equal(
            45,
            configured.Sum(profile =>
                profile.Animations.AimFireLayers!.Count));

        CharacterBattleConfig[] filtered = configured
            .Where(profile => profile.Animations.AimFireLayers!.Any(layer =>
                layer.ExcludeTimelines?.Count > 0))
            .ToArray();
        Assert.Equal(18, filtered.Length);
        Assert.Equal(
            350,
            filtered.Sum(profile =>
                profile.Animations.AimFireLayers!.Sum(layer =>
                    layer.ExcludeTimelines?.Count ?? 0)));
        Assert.All(
            configured.SelectMany(profile =>
                profile.Animations.AimFireLayers!),
            layer =>
            {
                Assert.False(string.IsNullOrWhiteSpace(layer.Animation));
                Assert.Equal(
                    CharacterBattleEffectBlendModes.Replace,
                    layer.Blend);
                Assert.Equal(1, layer.Alpha);
                Assert.True(layer.Loop);
                if (layer.ExcludeTimelines != null)
                {
                    Assert.Equal(
                        layer.ExcludeTimelines.Order(
                            StringComparer.Ordinal),
                        layer.ExcludeTimelines);
                }
            });
        Assert.DoesNotContain(
            configured
                .SelectMany(profile => profile.Animations.AimFireLayers!)
                .SelectMany(layer => layer.ExcludeTimelines ?? []),
            timeline =>
                timeline.StartsWith(
                    "AttachmentTimeline@",
                    StringComparison.Ordinal) ||
                timeline.StartsWith(
                    "DrawOrderTimeline@",
                    StringComparison.Ordinal));

        AssertLayers(
            profiles[Path.Combine("Cinderella Crystal Wave", "00")],
            "aim_fire",
            "aim_fire_hair");
        AssertLayers(
            profiles[Path.Combine("Laplace Neo", "00")],
            "aim_fire",
            "aim_fire_hair");
        AssertLayers(
            profiles[Path.Combine("Laplace Neo Variant01", "01")],
            "aim_fire",
            "aim_fire_hair");
        AssertLayers(
            profiles[Path.Combine("Sugar - Wild Backyard", "02")],
            "aim_fire",
            "aim_fire_hair",
            "aim_fire_hip");

        CharacterBattleAnimationsConfig sugar =
            profiles[Path.Combine("Sugar - Wild Backyard", "02")]
                .Animations;
        Assert.Equal(
            13,
            sugar.AimFireLayers!
                .Single(layer => layer.Animation == "aim_fire_hair")
                .ExcludeTimelines!
                .Count);
        Assert.Equal(
            14,
            sugar.AimFireLayers!
                .Single(layer => layer.Animation == "aim_fire_hip")
                .ExcludeTimelines!
                .Count);
        Assert.DoesNotContain(
            "RotateTimeline@bone:aim_holster",
            sugar.AimFireLayers!
                .Single(layer => layer.Animation == "aim_fire_hair")
                .ExcludeTimelines!);
        Assert.DoesNotContain(
            "ScaleTimeline@bone:aim_body_12",
            sugar.AimFireLayers!
                .Single(layer => layer.Animation == "aim_fire_hip")
                .ExcludeTimelines!);
        Assert.DoesNotContain(
            "TranslateTimeline@bone:aim_body_12",
            sugar.AimFireLayers!
                .Single(layer => layer.Animation == "aim_fire_hip")
                .ExcludeTimelines!);

        Assert.Null(
            profiles[Path.Combine("Rouge Variant 01", "01")]
                .Animations
                .AimFireLayers);
    }

    [Fact]
    public void LegacyEffectsMigrateOnlyForAuditedSkeletonProfiles()
    {
        List<CharacterBattleEffectConfig>? laplace =
            CharacterBattleConfigFactory.ResolveLegacyBattleEffects(
                @"C:\res\c103_aim_00.skel",
                ["aim_fire_hair", "aim_fire_hip"]);
        List<CharacterBattleEffectConfig>? blanc =
            CharacterBattleConfigFactory.ResolveLegacyBattleEffects(
                @"C:\res\c270_aim_01.skel",
                ["aim_fire_hair"]);

        CharacterBattleEffectConfig effect = Assert.Single(laplace!);
        Assert.Equal("aim_fire_hair", effect.Animation);
        Assert.Equal(CharacterBattleEffectBlendModes.Replace, effect.Blend);
        Assert.Equal(1, effect.Alpha);
        Assert.True(effect.Loop);
        Assert.Null(blanc);
    }

    private static void AssertLayers(
        CharacterBattleConfig battle,
        params string[] animationNames)
    {
        Assert.Equal(
            animationNames,
            battle.Animations.AimFireLayers!
                .Select(layer => layer.Animation));
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

    // Stages the real c017_01 evidence files from the workspace nikkedb
    // library into a managed res layout (standing/aim/cover subfolders),
    // mirroring what the removed NikkeDB import produced.
    private static void StageNikkeDbResource(string destinationRoot)
    {
        string sourceRoot = Path.Combine(
            FindWorkspaceRoot(),
            "resources",
            "nikkedb",
            "l2d",
            "mapped",
            "2026-04-25__Anis Star Variant 01 [c017_01]");
        StageState(
            sourceRoot,
            destinationRoot,
            sourceDirectoryName: null,
            CharacterResourceTypes.Standing);
        StageState(
            sourceRoot,
            destinationRoot,
            CharacterResourceTypes.Aim,
            CharacterResourceTypes.Aim);
        StageState(
            sourceRoot,
            destinationRoot,
            CharacterResourceTypes.Cover,
            CharacterResourceTypes.Cover);
    }

    private static void StageState(
        string sourceRoot,
        string destinationRoot,
        string? sourceDirectoryName,
        string resourceType)
    {
        string source = sourceDirectoryName == null
            ? sourceRoot
            : Path.Combine(sourceRoot, sourceDirectoryName);
        string target = Path.Combine(
            destinationRoot,
            "Anis",
            "c017_01",
            resourceType);
        Directory.CreateDirectory(target);
        foreach (string filePath in Directory.EnumerateFiles(source))
        {
            File.Copy(
                filePath,
                Path.Combine(target, Path.GetFileName(filePath)));
        }
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
