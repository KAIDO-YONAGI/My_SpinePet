using System.IO;
using System.Text.Json;
using SpinePet.Models;
using SpinePet.Services;
using SpinePet.Tests.TestDoubles;

namespace SpinePet.Tests;

public sealed class CharacterManagerTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "SpinePet.Tests",
        Guid.NewGuid().ToString("N"));

    public CharacterManagerTests()
    {
        Directory.CreateDirectory(_temporaryDirectory);
    }

    [Fact]
    public void ConstructorAcceptsRenderHostDependency()
    {
        FakeCharacterRenderHost renderHost = new();
        ConfigService configService = new(Path.Combine(
            _temporaryDirectory,
            "config.json"));

        CharacterManager manager = new(
            configService,
            new CharacterIdentityService(
                new Dictionary<string, string>()),
            renderHost);

        Assert.Same(renderHost, manager.RenderHost);
        Assert.Equal(1, renderHost.TargetFrameRateSetCount);
        Assert.Equal(
            GlobalConfig.DefaultTargetFrameRate,
            renderHost.TargetFrameRate);
    }

    [Fact]
    public void FrameRateChangeUpdatesRendererAndPersistsConfiguration()
    {
        string configPath = Path.Combine(
            _temporaryDirectory,
            "frame-rate-config.json");
        FakeCharacterRenderHost renderHost = new();
        ConfigService configService = new(configPath);
        CharacterManager manager = new(
            configService,
            new CharacterIdentityService(
                new Dictionary<string, string>()),
            renderHost);

        manager.SetTargetFrameRate(GlobalConfig.HighRefreshTargetFrameRate);

        Assert.Equal(
            GlobalConfig.HighRefreshTargetFrameRate,
            manager.TargetFrameRate);
        Assert.Equal(
            GlobalConfig.HighRefreshTargetFrameRate,
            renderHost.TargetFrameRate);
        Assert.Equal(2, renderHost.TargetFrameRateSetCount);
        Assert.Equal(
            GlobalConfig.HighRefreshTargetFrameRate,
            configService.Load().Global.TargetFrameRate);
    }

    [Fact]
    public void AddCharacterKeepsCurrentSkinWhenNewSkinIsImported()
    {
        CharacterResourceFiles skin00 = CreateResources(
            "managed",
            "Rapi",
            "010",
            "00",
            CharacterResourceTypes.Standing);
        CharacterResourceFiles skin01 = CreateResources(
            "managed",
            "Rapi",
            "010",
            "01",
            CharacterResourceTypes.Standing);
        CharacterManager manager = CreateManager("add-skins.json");

        Assert.True(manager.AddCharacter(skin00));
        Assert.False(manager.AddCharacter(skin01));

        CharacterConfig character = Assert.Single(manager.Characters);
        Assert.Equal(skin00.SkeletonPath, character.SkeletonPath);
        Assert.Equal("00", manager.GetCharacterIdentity(character).SkinCode);
    }

    [Fact]
    public void AddCharacterRejectsRetiredStateResources()
    {
        CharacterResourceFiles aim = CreateResources(
            "retired-add",
            "Rapi",
            "010",
            "00",
            "aim");
        CharacterManager manager = CreateManager("retired-add.json");

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => manager.AddCharacter(aim));

        Assert.Contains("Only standing", exception.Message);
        Assert.Empty(manager.Characters);
    }

    [Fact]
    public async Task SwitchCharacterResourcesAllowsAnotherSkinOfSameCharacter()
    {
        CharacterResourceFiles skin00 = CreateResources(
            "switch",
            "Rapi",
            "010",
            "00",
            CharacterResourceTypes.Standing);
        CharacterResourceFiles skin01 = CreateResources(
            "switch",
            "Rapi",
            "010",
            "01",
            CharacterResourceTypes.Standing);
        CharacterResourceFiles neon = CreateResources(
            "switch",
            "Neon",
            "007",
            "00",
            CharacterResourceTypes.Standing);
        CharacterManager manager = CreateManager("switch-skin.json");
        manager.AddCharacter(skin00);
        CharacterConfig character = Assert.Single(manager.Characters);
        character.ConfiguredAnimation = "idle";

        await manager.SwitchCharacterResourcesAsync(character, skin01);

        Assert.Equal(skin01.SkeletonPath, character.SkeletonPath);
        Assert.Equal("01", manager.GetCharacterIdentity(character).SkinCode);
        Assert.Empty(character.ConfiguredAnimation);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            manager.SwitchCharacterResourcesAsync(character, neon));
    }

    [Fact]
    public async Task FailedVisibleSkinSwitchRestoresPreviousResources()
    {
        CharacterResourceFiles skin00 = CreateResources(
            "switch-rollback",
            "Rapi",
            "010",
            "00",
            CharacterResourceTypes.Standing);
        CharacterResourceFiles skin01 = CreateResources(
            "switch-rollback",
            "Rapi",
            "010",
            "01",
            CharacterResourceTypes.Standing);
        string configPath = Path.Combine(
            _temporaryDirectory,
            "switch-rollback.json");
        ConfigService configService = new(configPath);
        FakeCharacterRenderHost renderHost = new()
        {
            ShowCharacterHandler = candidate =>
                string.Equals(
                    candidate.SkeletonPath,
                    skin01.SkeletonPath,
                    StringComparison.OrdinalIgnoreCase)
                    ? Task.FromException(
                        new InvalidDataException("broken skin"))
                    : Task.CompletedTask
        };
        CharacterManager manager = CreateManager(configService, renderHost);
        manager.AddCharacter(skin00);
        CharacterConfig character = Assert.Single(manager.Characters);
        character.ConfiguredAnimation = "idle";

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => manager.SwitchCharacterResourcesAsync(character, skin01));

        Assert.Equal("broken skin", exception.Message);
        Assert.Equal(skin00.SkeletonPath, character.SkeletonPath);
        Assert.Equal(skin00.AtlasPath, character.AtlasPath);
        Assert.Equal(skin00.PrimaryTexturePath, character.TexturePath);
        Assert.Equal("idle", character.ConfiguredAnimation);
        Assert.True(character.Visible);
        Assert.Equal(
            [skin01.SkeletonPath, skin00.SkeletonPath],
            renderHost.ShownSkeletonPaths);
        CharacterConfig persisted = Assert.Single(configService.Load().Characters);
        Assert.Equal(skin00.SkeletonPath, persisted.SkeletonPath);
        Assert.True(persisted.Visible);
    }

    [Fact]
    public async Task UnsupportedSkinSwitchLeavesCurrentCharacterLoaded()
    {
        CharacterResourceFiles skin00 = CreateResources(
            "switch-version",
            "Rapi",
            "010",
            "00",
            CharacterResourceTypes.Standing);
        CharacterResourceFiles skin01 = CreateResources(
            "switch-version",
            "Rapi",
            "010",
            "01",
            CharacterResourceTypes.Standing);
        WriteSkeletonHeader(skin01.SkeletonPath, "4.0.47");
        FakeCharacterRenderHost renderHost = new();
        CharacterManager manager = CreateManager(
            new ConfigService(Path.Combine(
                _temporaryDirectory,
                "switch-version.json")),
            renderHost);
        manager.AddCharacter(skin00);
        CharacterConfig character = Assert.Single(manager.Characters);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => manager.SwitchCharacterResourcesAsync(character, skin01));

        Assert.Contains("Skeleton version 4.0.47", exception.Message);
        Assert.Equal(skin00.SkeletonPath, character.SkeletonPath);
        Assert.True(character.Visible);
        Assert.Empty(renderHost.RemovedCharacterIds);
    }

    [Fact]
    public void UnloadCharacterOnlyReleasesRendererResources()
    {
        CharacterResourceFiles resources = CreateResources(
            "unload",
            "Rapi",
            "010",
            "00",
            CharacterResourceTypes.Standing);
        FakeCharacterRenderHost renderHost = new();
        CharacterManager manager = CreateManager(
            new ConfigService(Path.Combine(
                _temporaryDirectory,
                "unload-config.json")),
            renderHost);
        manager.AddCharacter(resources);
        CharacterConfig character = Assert.Single(manager.Characters);

        manager.UnloadCharacter(character);

        Assert.Contains(character.Id, renderHost.RemovedCharacterIds);
        Assert.Same(character, Assert.Single(manager.Characters));
        Assert.True(character.Visible);
    }

    [Fact]
    public void SynchronizeCreatesOneCardForAllSkinsOfCharacter()
    {
        CharacterResourceFiles skin00 = CreateResources(
            "catalog",
            "Rapi",
            "010",
            "00",
            CharacterResourceTypes.Standing);
        CharacterResourceFiles skin01 = CreateResources(
            "catalog",
            "Rapi",
            "010",
            "01",
            CharacterResourceTypes.Standing);
        CharacterResourceFiles skin00Aim = CreateResources(
            "catalog",
            "Rapi",
            "010",
            "00",
            "aim");
        CharacterManager manager = CreateManager("sync-skins.json");
        int notifications = 0;
        manager.CharactersChanged += () => notifications++;

        CharacterResourceSynchronizationResult result =
            manager.SynchronizeResources(
                [skin01, skin00Aim, skin00],
                Path.Combine(_temporaryDirectory, "catalog"));

        Assert.Equal(1, result.AddedCount);
        Assert.True(result.HasChanges);
        Assert.Equal(1, notifications);
        CharacterConfig character = Assert.Single(manager.Characters);
        Assert.Equal(skin00.SkeletonPath, character.SkeletonPath);
        Assert.False(character.RequiresStandingMigration);
    }

    [Fact]
    public void SynchronizeMergesDuplicateConfigsAndKeepsVisibleSelection()
    {
        string managedRoot = Path.Combine(_temporaryDirectory, "merge");
        CharacterResourceFiles skin00 = CreateResources(
            "merge",
            "Rapi",
            "010",
            "00",
            CharacterResourceTypes.Standing);
        CharacterResourceFiles skin01 = CreateResources(
            "merge",
            "Rapi",
            "010",
            "01",
            CharacterResourceTypes.Standing);
        CharacterResourceFiles skin01Aim = CreateResources(
            "merge",
            "Rapi",
            "010",
            "01",
            "aim");
        CharacterConfig hiddenSkin00 = CreateConfig(
            Path.Combine(managedRoot, "legacy", "c010_00.skel"),
            CharacterResourceTypes.Standing,
            visible: false,
            id: "hidden");
        CharacterConfig visibleSkin01 = CreateConfig(
            Path.Combine(managedRoot, "legacy", "c010_01.skel"),
            "aim",
            visible: true,
            id: "visible");
        visibleSkin01.PositionX = 321;
        visibleSkin01.Scale = 0.75;
        visibleSkin01.ConfiguredAnimation = "aim_idle";
        ConfigService configService = SaveConfig(
            "duplicate-config.json",
            hiddenSkin00,
            visibleSkin01);
        FakeCharacterRenderHost renderHost = new();
        CharacterManager manager = CreateManager(configService, renderHost);
        int notifications = 0;
        manager.CharactersChanged += () => notifications++;

        CharacterResourceSynchronizationResult result =
            manager.SynchronizeResources(
                [skin00, skin01, skin01Aim],
                managedRoot);

        Assert.Equal(1, result.MergedCount);
        Assert.Equal(1, result.UpdatedCount);
        Assert.Equal(1, notifications);
        CharacterConfig retained = Assert.Single(manager.Characters);
        Assert.Equal("visible", retained.Id);
        Assert.Equal(skin01.SkeletonPath, retained.SkeletonPath);
        Assert.False(retained.RequiresStandingMigration);
        Assert.Empty(retained.ConfiguredAnimation);
        Assert.Equal(321, retained.PositionX);
        Assert.Equal(0.75, retained.Scale);
        Assert.Contains("hidden", renderHost.RemovedCharacterIds);
        Assert.Single(configService.Load().Characters);
    }

    [Fact]
    public void SynchronizeFallsBackWithinSkinThenToFirstStandingSkin()
    {
        string managedRoot = Path.Combine(_temporaryDirectory, "fallback");
        CharacterResourceFiles skin00 = CreateResources(
            "fallback",
            "Rapi",
            "010",
            "00",
            CharacterResourceTypes.Standing);
        CharacterResourceFiles skin01 = CreateResources(
            "fallback",
            "Rapi",
            "010",
            "01",
            CharacterResourceTypes.Standing);
        CharacterConfig configuredCover = CreateConfig(
            Path.Combine(managedRoot, "old", "c010_01.skel"),
            "cover",
            visible: true,
            id: "rapi");
        configuredCover.ConfiguredAnimation = "cover_idle";
        ConfigService configService = SaveConfig(
            "fallback-config.json",
            configuredCover);
        CharacterManager manager = CreateManager(
            configService,
            new FakeCharacterRenderHost());

        CharacterResourceSynchronizationResult sameSkinResult =
            manager.SynchronizeResources([skin00, skin01], managedRoot);

        CharacterConfig retained = Assert.Single(manager.Characters);
        Assert.Equal(skin01.SkeletonPath, retained.SkeletonPath);
        Assert.False(retained.RequiresStandingMigration);
        Assert.Empty(retained.ConfiguredAnimation);
        Assert.Equal(1, sameSkinResult.UpdatedCount);

        retained.ConfiguredAnimation = "standing_idle";
        CharacterResourceSynchronizationResult firstSkinResult =
            manager.SynchronizeResources([skin00], managedRoot);

        Assert.Equal(skin00.SkeletonPath, retained.SkeletonPath);
        Assert.Empty(retained.ConfiguredAnimation);
        Assert.Equal(1, firstSkinResult.UpdatedCount);
    }

    [Fact]
    public void SynchronizeRemovesStaleManagedCharacter()
    {
        string managedRoot = Path.Combine(_temporaryDirectory, "res");
        CharacterConfig staleCharacter = CreateConfig(
            Path.Combine(
                managedRoot,
                "Neon",
                "00",
                CharacterResourceTypes.Standing,
                "c007_00.skel"),
            CharacterResourceTypes.Standing,
            visible: true,
            id: "neon");
        ConfigService configService = SaveConfig(
            "stale-config.json",
            staleCharacter);
        FakeCharacterRenderHost renderHost = new();
        CharacterManager manager = CreateManager(configService, renderHost);

        CharacterResourceSynchronizationResult result =
            manager.SynchronizeResources([], managedRoot);

        Assert.Equal(1, result.RemovedCount);
        Assert.Empty(manager.Characters);
        Assert.Contains("neon", renderHost.RemovedCharacterIds);
        Assert.Empty(configService.Load().Characters);
    }

    [Fact]
    public void SynchronizePreservesCompleteExternalStandingCharacter()
    {
        string managedRoot = Path.Combine(_temporaryDirectory, "res");
        string externalSkeletonPath = Path.Combine(
            _temporaryDirectory,
            "res-backup",
            "External",
            "external.skel");
        CharacterConfig externalCharacter = CreateConfig(
            externalSkeletonPath,
            CharacterResourceTypes.Standing,
            visible: true,
            id: "external");
        Directory.CreateDirectory(Path.GetDirectoryName(
            externalSkeletonPath)!);
        File.WriteAllBytes(externalCharacter.SkeletonPath, []);
        File.WriteAllBytes(externalCharacter.AtlasPath, []);
        File.WriteAllBytes(externalCharacter.TexturePath, []);
        ConfigService configService = SaveConfig(
            "external-config.json",
            externalCharacter);
        CharacterManager manager = CreateManager(
            configService,
            new FakeCharacterRenderHost());
        int notifications = 0;
        manager.CharactersChanged += () => notifications++;

        CharacterResourceSynchronizationResult result =
            manager.SynchronizeResources([], managedRoot);

        Assert.False(result.HasChanges);
        Assert.Equal(0, notifications);
        Assert.Equal(externalCharacter.Id, Assert.Single(manager.Characters).Id);
    }

    [Fact]
    public void SynchronizeRewritesLegacyStandingConfigWithoutLibraryChange()
    {
        string managedRoot = Path.Combine(_temporaryDirectory, "res");
        string externalDirectory = Path.Combine(
            _temporaryDirectory,
            "external-standing");
        Directory.CreateDirectory(externalDirectory);
        string skeletonPath = Path.Combine(externalDirectory, "c010_00.skel");
        string atlasPath = Path.Combine(externalDirectory, "c010_00.atlas");
        string texturePath = Path.Combine(externalDirectory, "c010_00.png");
        File.WriteAllBytes(skeletonPath, []);
        File.WriteAllBytes(atlasPath, []);
        File.WriteAllBytes(texturePath, []);
        string configPath = Path.Combine(
            _temporaryDirectory,
            "legacy-standing.json");
        File.WriteAllText(
            configPath,
            JsonSerializer.Serialize(new
            {
                Version = "1.4",
                Characters = new[]
                {
                    new
                    {
                        Id = "legacy-standing",
                        Name = "Rapi",
                        SkelPath = skeletonPath,
                        AtlasPath = atlasPath,
                        TexturePath = texturePath,
                        ResourceType = "standing",
                        Visible = true
                    }
                }
            }));
        ConfigService configService = new(configPath);
        CharacterManager manager = CreateManager(
            configService,
            new FakeCharacterRenderHost());

        CharacterResourceSynchronizationResult result =
            manager.SynchronizeResources([], managedRoot);

        Assert.False(result.HasChanges);
        Assert.Single(manager.Characters);
        string rewrittenJson = File.ReadAllText(configPath);
        Assert.Contains(
            $"\"Version\": \"{AppConfig.CurrentVersion}\"",
            rewrittenJson);
        Assert.DoesNotContain(
            "ResourceType",
            rewrittenJson,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SynchronizeRemovesRetiredExternalStateWithoutDeletingFiles()
    {
        string managedRoot = Path.Combine(_temporaryDirectory, "res");
        string externalDirectory = Path.Combine(
            _temporaryDirectory,
            "external",
            "cover");
        Directory.CreateDirectory(externalDirectory);
        string skeletonPath = Path.Combine(
            externalDirectory,
            "c010_00.skel");
        string atlasPath = Path.Combine(externalDirectory, "c010_00.atlas");
        string texturePath = Path.Combine(externalDirectory, "c010_00.png");
        File.WriteAllBytes(skeletonPath, []);
        File.WriteAllBytes(atlasPath, []);
        File.WriteAllBytes(texturePath, []);
        string configPath = Path.Combine(
            _temporaryDirectory,
            "retired-external.json");
        File.WriteAllText(
            configPath,
            JsonSerializer.Serialize(new
            {
                Version = "1.4",
                Characters = new[]
                {
                    new
                    {
                        Id = "retired-external",
                        Name = "Rapi",
                        SkelPath = skeletonPath,
                        AtlasPath = atlasPath,
                        TexturePath = texturePath,
                        ResourceType = "cover",
                        CurrentAnimation = "cover_idle",
                        Visible = true
                    }
                }
            }));
        ConfigService configService = new(configPath);
        FakeCharacterRenderHost renderHost = new();
        CharacterManager manager = CreateManager(configService, renderHost);

        CharacterResourceSynchronizationResult result =
            manager.SynchronizeResources([], managedRoot);

        Assert.Equal(1, result.RemovedCount);
        Assert.Empty(manager.Characters);
        Assert.Contains("retired-external", renderHost.RemovedCharacterIds);
        Assert.True(File.Exists(skeletonPath));
        Assert.True(File.Exists(atlasPath));
        Assert.True(File.Exists(texturePath));
    }

    private CharacterManager CreateManager(string configFileName)
    {
        return CreateManager(
            new ConfigService(Path.Combine(
                _temporaryDirectory,
                configFileName)),
            new FakeCharacterRenderHost());
    }

    private static CharacterManager CreateManager(
        ConfigService configService,
        FakeCharacterRenderHost renderHost)
    {
        return new CharacterManager(
            configService,
            new CharacterIdentityService(
                new Dictionary<string, string>
                {
                    ["010"] = "Rapi",
                    ["007"] = "Neon"
                }),
            renderHost);
    }

    private ConfigService SaveConfig(
        string configFileName,
        params CharacterConfig[] characters)
    {
        ConfigService configService = new(Path.Combine(
            _temporaryDirectory,
            configFileName));
        configService.Save(new AppConfig
        {
            Characters = characters.ToList()
        });
        return configService;
    }

    private CharacterResourceFiles CreateResources(
        string rootName,
        string characterName,
        string characterCode,
        string skinCode,
        string resourceType)
    {
        string resourceName = $"c{characterCode}_{skinCode}";
        string directory = Path.Combine(
            _temporaryDirectory,
            rootName,
            characterName,
            skinCode,
            resourceType);
        Directory.CreateDirectory(directory);
        string skeletonPath = Path.Combine(
            directory,
            $"{resourceName}.skel");
        string atlasPath = Path.Combine(
            directory,
            $"{resourceName}.atlas");
        string texturePath = Path.Combine(
            directory,
            $"{resourceName}.png");
        WriteSkeletonHeader(skeletonPath);
        File.WriteAllText(atlasPath, string.Empty);
        File.WriteAllBytes(texturePath, []);
        return new CharacterResourceFiles(
            skeletonPath,
            atlasPath,
            texturePath,
            [],
            resourceType,
            new CharacterIdentity(
                resourceName,
                characterCode,
                skinCode,
                characterName));
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

    private static CharacterConfig CreateConfig(
        string skeletonPath,
        string resourceType,
        bool visible,
        string id)
    {
        string directory = Path.GetDirectoryName(skeletonPath)!;
        string stem = Path.GetFileNameWithoutExtension(skeletonPath);
        return new CharacterConfig
        {
            Id = id,
            Name = stem.StartsWith(
                "c010",
                StringComparison.OrdinalIgnoreCase)
                    ? "Rapi"
                    : "External",
            SkeletonPath = skeletonPath,
            AtlasPath = Path.Combine(directory, $"{stem}.atlas"),
            TexturePath = Path.Combine(directory, $"{stem}.png"),
            LegacyResourceType = resourceType,
            Visible = visible
        };
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }
}
