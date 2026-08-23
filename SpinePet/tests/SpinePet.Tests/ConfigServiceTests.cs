using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using SpinePet.Models;
using SpinePet.Services;

namespace SpinePet.Tests;

public sealed class ConfigServiceTests : IDisposable
{
    private static readonly Rect TestWorkArea = new(0, 0, 1920, 1080);

    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "SpinePet.Tests",
        Guid.NewGuid().ToString("N"));

    public ConfigServiceTests()
    {
        Directory.CreateDirectory(_temporaryDirectory);
    }

    [Fact]
    public void SaveAndLoadRoundTripsConfiguration()
    {
        string configPath = Path.Combine(_temporaryDirectory, "config.json");
        ConfigService service = new(configPath, TestWorkArea);
        AppConfig expected = new()
        {
            Global = new GlobalConfig
            {
                AllowRenderDrag = false,
                TargetFrameRate = GlobalConfig.PowerSavingTargetFrameRate
            },
            Characters =
            [
                new CharacterConfig
                {
                    Id = "character-1",
                    Name = "Test Character",
                    AnimationSpeed = 1.25,
                    ConfiguredAnimation = "idle"
                }
            ]
        };

        service.Save(expected);
        AppConfig actual = service.Load();

        Assert.False(actual.Global.AllowRenderDrag);
        Assert.Equal(
            GlobalConfig.PowerSavingTargetFrameRate,
            actual.Global.TargetFrameRate);
        CharacterConfig character = Assert.Single(actual.Characters);
        Assert.Equal("character-1", character.Id);
        Assert.Equal("Test Character", character.Name);
        Assert.Equal(1.25, character.AnimationSpeed);
        Assert.Equal("idle", character.ConfiguredAnimation);
        Assert.False(character.RequiresStandingMigration);
        Assert.False(File.Exists($"{configPath}.tmp"));
        Assert.DoesNotContain(
            "ResourceType",
            File.ReadAllText(configPath),
            StringComparison.Ordinal);
    }

    [Fact]
    public void LoadNormalizesLegacyAndInvalidValues()
    {
        string configPath = Path.Combine(_temporaryDirectory, "legacy.json");
        File.WriteAllText(
            configPath,
            """
            {
              "Version": "1.0",
              "Global": {
                "AllowRenderDrag": true
              },
              "Characters": [
                {
                  "Id": "legacy",
                  "CurrentAnimation": "aim_idle",
                  "ResourceType": "aim",
                  "AnimationSpeed": 0,
                  "ExtraTexturePaths": null
                }
              ]
            }
            """);

        ConfigService service = new(configPath, TestWorkArea);
        AppConfig config = service.Load();

        Assert.Equal(AppConfig.CurrentVersion, config.Version);
        CharacterConfig character = Assert.Single(config.Characters);
        Assert.Equal(string.Empty, character.ConfiguredAnimation);
        Assert.Equal(1.0, character.AnimationSpeed);
        Assert.Empty(character.AdditionalTexturePaths);
        Assert.True(character.RequiresStandingMigration);
        Assert.Null(character.LegacyResourceType);
        Assert.Equal(
            TestWorkArea.Left + TestWorkArea.Width / 2,
            character.PositionX);
        Assert.Equal(
            TestWorkArea.Bottom - 24,
            character.PositionY);

        service.Save(config);
        Assert.DoesNotContain(
            "ResourceType",
            File.ReadAllText(configPath),
            StringComparison.Ordinal);
    }

    [Fact]
    public void LoadPreservesCorruptConfigurationBeforeDefaultCanBeSaved()
    {
        const string corruptJson = """{ "Characters": [ }""";
        string configPath = Path.Combine(
            _temporaryDirectory,
            "corrupt.json");
        File.WriteAllText(configPath, corruptJson);
        DateTime requestedTimestampUtc = new(
            2024,
            02,
            03,
            04,
            05,
            06,
            DateTimeKind.Utc);
        requestedTimestampUtc =
            requestedTimestampUtc.AddTicks(7_890_123);
        File.SetLastWriteTimeUtc(configPath, requestedTimestampUtc);
        DateTime storedTimestampUtc =
            File.GetLastWriteTimeUtc(configPath).ToUniversalTime();
        string timestamp = storedTimestampUtc.ToString(
            "yyyyMMdd'T'HHmmssfffffff'Z'",
            CultureInfo.InvariantCulture);
        string backupPath =
            $"{configPath}.{timestamp}.corrupt";

        ConfigService service = new(configPath, TestWorkArea);
        AppConfig recovered = service.Load();

        Assert.Equal(AppConfig.CurrentVersion, recovered.Version);
        Assert.Empty(recovered.Characters);
        Assert.True(File.Exists(backupPath));
        Assert.Equal(corruptJson, File.ReadAllText(backupPath));
        Assert.Equal(
            storedTimestampUtc,
            File.GetLastWriteTimeUtc(backupPath).ToUniversalTime());

        service.Save(recovered);

        using JsonDocument saved =
            JsonDocument.Parse(File.ReadAllText(configPath));
        Assert.Equal(
            AppConfig.CurrentVersion,
            saved.RootElement.GetProperty("Version").GetString());
        Assert.Equal(corruptJson, File.ReadAllText(backupPath));
    }

    [Fact]
    public void SaveDoesNotOverwriteCorruptConfigWhenBackupCannotBeCreated()
    {
        const string corruptJson = """{ "Characters": [ }""";
        string configPath = Path.Combine(
            _temporaryDirectory,
            "locked-corrupt.json");
        File.WriteAllText(configPath, corruptJson);
        ConfigService service = new(configPath);

        using (FileStream lockStream = new(
            configPath,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None))
        {
            AppConfig recovered = service.Load();
            service.Save(recovered);
        }

        Assert.Equal(corruptJson, File.ReadAllText(configPath));
        Assert.Empty(Directory.EnumerateFiles(
            _temporaryDirectory,
            "locked-corrupt.json.*.corrupt"));
    }

    [Fact]
    public void NormalizeRepairsNullFieldsDuplicateIdsAndNonFiniteValues()
    {
        CharacterConfig first = new()
        {
            Id = "duplicate",
            Name = null!,
            SkeletonPath = null!,
            AtlasPath = null!,
            TexturePath = null!,
            AdditionalTexturePaths = [null!, "", "page.png", "PAGE.png"],
            ConfiguredAnimation = null!,
            PositionX = double.NaN,
            PositionY = double.PositiveInfinity,
            Scale = double.NegativeInfinity,
            AnimationSpeed = double.NaN
        };
        CharacterConfig second = new()
        {
            Id = "duplicate"
        };
        AppConfig config = new()
        {
            Characters = [first, null!, second]
        };

        ConfigService.Normalize(config, new Rect(0, 0, 1920, 1080));

        Assert.Equal(2, config.Characters.Count);
        Assert.Equal("duplicate", first.Id);
        Assert.False(string.IsNullOrWhiteSpace(second.Id));
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(string.Empty, first.Name);
        Assert.Equal(string.Empty, first.SkeletonPath);
        Assert.Equal(string.Empty, first.AtlasPath);
        Assert.Equal(string.Empty, first.TexturePath);
        Assert.Equal(string.Empty, first.ConfiguredAnimation);
        Assert.Equal(["page.png"], first.AdditionalTexturePaths);
        Assert.Equal(1.0, first.AnimationSpeed);
        Assert.Equal(1.0, first.Scale);
        Assert.True(double.IsFinite(first.PositionX));
        Assert.True(double.IsFinite(first.PositionY));
    }

    [Theory]
    [InlineData(30, 30)]
    [InlineData(60, 60)]
    [InlineData(120, 120)]
    [InlineData(0, 60)]
    [InlineData(144, 60)]
    public void NormalizeRestrictsFrameRateToSupportedValues(
        int configuredFrameRate,
        int expectedFrameRate)
    {
        AppConfig config = new()
        {
            Global = new GlobalConfig
            {
                TargetFrameRate = configuredFrameRate
            }
        };

        ConfigService.Normalize(config);

        Assert.Equal(expectedFrameRate, config.Global.TargetFrameRate);
    }

    [Fact]
    public void NormalizeDoesNotApplyLegacyPositionMigrationToFutureVersions()
    {
        CharacterConfig character = new()
        {
            PositionX = 321,
            PositionY = 654,
            Visible = true
        };
        AppConfig config = new()
        {
            Version = "1.9",
            Characters = [character]
        };

        ConfigService.Normalize(config);

        Assert.Equal("1.9", config.Version);
        Assert.Equal(321, character.PositionX);
        Assert.Equal(654, character.PositionY);
    }

    [Fact]
    public void NormalizeDoesNotMigrateLegacyEffectsInFutureVersions()
    {
        CharacterBattleConfig battle = CreateBattleConfig(
            "future-effects");
        battle.Animations.BattleEffects = null;
        battle.Animations.AimFireEffects = ["future_fire_effect"];
        AppConfig config = new()
        {
            Version = "1.9",
            Characters =
            [
                new CharacterConfig
                {
                    Battle = battle
                }
            ]
        };

        ConfigService.Normalize(config, TestWorkArea);

        Assert.Equal("1.9", config.Version);
        Assert.Equal(
            ["future_fire_effect"],
            battle.Animations.AimFireEffects);
        Assert.Null(battle.Animations.BattleEffects);
    }

    [Fact]
    public void LoadUpgradesVersion15AndAddsBattleRulesWithoutLosingSettings()
    {
        string configPath = Path.Combine(
            _temporaryDirectory,
            "version-15.json");
        File.WriteAllText(
            configPath,
            """
            {
              "Version": "1.5",
              "Global": {
                "AllowRenderDrag": false,
                "TargetFrameRate": 120
              },
              "Characters": [
                {
                  "Id": "legacy",
                  "PositionX": 321,
                  "PositionY": 654,
                  "Scale": 0.7,
                  "AnimationSpeed": 1.4,
                  "CurrentAnimation": "idle",
                  "Visible": true
                }
              ]
            }
            """);
        ConfigService service = new(configPath, TestWorkArea);

        AppConfig config = service.Load();

        Assert.Equal(AppConfig.CurrentVersion, config.Version);
        Assert.False(config.Global.AllowRenderDrag);
        Assert.Equal(120, config.Global.TargetFrameRate);
        Assert.Equal(CharacterDisplayModes.Normal,
            config.Global.BattleRules.StartupMode);
        Assert.Equal(CharacterBattleStates.Cover,
            config.Global.BattleRules.DefaultBattleState);
        Assert.Equal(300,
            config.Global.BattleRules.RightHoldThresholdMs);
        Assert.True(config.Global.BattleRules.ContinuousFireWhileHeld);
        Assert.True(config.Global.BattleRules.ReloadOnRelease);
        Assert.True(config.Global.BattleRules.ShortRightClickOpensPanel);
        CharacterConfig character = Assert.Single(config.Characters);
        Assert.Equal(321, character.PositionX);
        Assert.Equal(654, character.PositionY);
        Assert.Equal(0.7, character.Scale);
        Assert.Equal(1.4, character.AnimationSpeed);
        Assert.Equal("idle", character.ConfiguredAnimation);
        Assert.True(character.Visible);
    }

    [Fact]
    public void SaveOmitsMissingBattleAndKeepsCompleteBattle()
    {
        string configPath = Path.Combine(
            _temporaryDirectory,
            "battle-json.json");
        ConfigService service = new(configPath, TestWorkArea);
        CharacterConfig standingOnly = new()
        {
            Id = "standing-only",
            Name = "Standing"
        };
        CharacterConfig complete = new()
        {
            Id = "complete",
            Name = "Complete",
            Battle = CreateBattleConfig("complete-battle")
        };

        service.Save(new AppConfig
        {
            Characters = [standingOnly, complete]
        });
        string json = File.ReadAllText(configPath);
        AppConfig loaded = service.Load();

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement characters = document.RootElement.GetProperty(
            "Characters");
        Assert.False(characters[0].TryGetProperty("Battle", out _));
        JsonElement battle = characters[1].GetProperty("Battle");
        JsonElement effects = battle
            .GetProperty("Animations")
            .GetProperty("BattleEffects");
        Assert.Equal(
            ["aim_fire_hair", "aim_fire_hip"],
            effects.EnumerateArray()
                .Select(item => item.GetProperty("Animation").GetString()!)
                .ToArray());
        Assert.Null(loaded.Characters[0].Battle);
        Assert.Equal(
            ["aim_fire_hair", "aim_fire_hip"],
            Assert.IsType<CharacterBattleConfig>(
                    loaded.Characters[1].Battle)
                .Animations.BattleEffects!
                .Select(effect => effect.Animation));
    }

    [Fact]
    public void NormalizeRemovesIncompleteBattleAsOneUnit()
    {
        CharacterConfig character = new()
        {
            Battle = new CharacterBattleConfig
            {
                Aim = CreateBattleResource("aim-valid"),
                Cover = new CharacterBattleResourceConfig()
            }
        };
        AppConfig config = new()
        {
            Version = "1.5",
            Characters = [character]
        };

        ConfigService.Normalize(config, TestWorkArea);

        Assert.Null(character.Battle);
        Assert.True(config.RequiresRewrite);
    }

    [Fact]
    public void NormalizeMigratesLegacyEffectsThroughAuditedProfile()
    {
        CharacterBattleConfig battle = CreateBattleConfig(
            "legacy-effects");
        string legacySkeleton = battle.Aim.SkeletonPath;
        string auditedSkeleton = Path.Combine(
            Path.GetDirectoryName(legacySkeleton)!,
            "c103_aim_00.skel");
        File.Move(legacySkeleton, auditedSkeleton);
        battle.Aim.SkeletonPath = auditedSkeleton;
        battle.Animations.BattleEffects = null;
        battle.Animations.AimFireEffects =
            ["aim_fire_hair", "aim_fire_hip"];
        AppConfig config = new()
        {
            Version = "1.7",
            Characters =
            [
                new CharacterConfig
                {
                    Battle = battle
                }
            ]
        };

        ConfigService.Normalize(config, TestWorkArea);

        CharacterBattleAnimationsConfig animations =
            Assert.Single(config.Characters).Battle!.Animations;
        CharacterBattleEffectConfig effect = Assert.Single(
            animations.BattleEffects!);
        Assert.Equal("aim_fire_hair", effect.Animation);
        Assert.Equal(
            CharacterBattleEffectBlendModes.Replace,
            effect.Blend);
        Assert.Null(animations.AimFireEffects);
        Assert.True(config.RequiresRewrite);
    }

    [Fact]
    public void NormalizeIsStableWhenRunTwice()
    {
        AppConfig config = new()
        {
            Version = "1.0",
            Characters =
            [
                new CharacterConfig
                {
                    Id = string.Empty,
                    AdditionalTexturePaths =
                        ["page.png", "PAGE.png", ""],
                    PositionX = double.NaN,
                    PositionY = double.PositiveInfinity
                }
            ]
        };

        ConfigService.Normalize(config, TestWorkArea);
        string first = JsonSerializer.Serialize(config);
        string firstId = Assert.Single(config.Characters).Id;
        ConfigService.Normalize(config, TestWorkArea);
        string second = JsonSerializer.Serialize(config);

        Assert.Equal(first, second);
        Assert.Equal(firstId, Assert.Single(config.Characters).Id);
    }

    [Fact]
    public async Task SyncAndAsyncSavesShareCommitAndClearRewriteState()
    {
        string configPath = Path.Combine(
            _temporaryDirectory,
            "sync-async.json");
        ConfigService service = new(configPath, TestWorkArea);
        AppConfig config = new()
        {
            Version = "1.4",
            Characters = [new CharacterConfig { Id = "one" }]
        };

        service.Save(config);
        Assert.False(config.RequiresRewrite);
        int replacementsAfterSync = service.DiskReplacementCount;

        config.RequiresRewrite = true;
        await service.SaveAsync(config);

        Assert.False(config.RequiresRewrite);
        Assert.Equal(
            replacementsAfterSync,
            service.DiskReplacementCount);
        Assert.Empty(Directory.EnumerateFiles(
            _temporaryDirectory,
            "sync-async.json.tmp.*"));
    }

    [Fact]
    public void OlderCommitCannotOverwriteNewerContent()
    {
        string configPath = Path.Combine(
            _temporaryDirectory,
            "ordered.json");
        ConfigFileCommitter committer = new(configPath);

        Assert.True(committer.Commit("new", version: 2));
        Assert.True(committer.Commit("old", version: 1));

        Assert.Equal("new", File.ReadAllText(configPath));
        Assert.Equal(1, committer.ReplacementCount);
    }

    private CharacterBattleConfig CreateBattleConfig(string directoryName) =>
        new()
        {
            Aim = CreateBattleResource(Path.Combine(directoryName, "aim")),
            Cover = CreateBattleResource(Path.Combine(directoryName, "cover")),
            Animations = new CharacterBattleAnimationsConfig
            {
                AimIdle = "aim_idle",
                ToAim = "to_aim",
                AimFire = "aim_fire",
                BattleEffects =
                [
                    new CharacterBattleEffectConfig
                    {
                        Animation = "aim_fire_hair",
                        Blend = "invalid",
                        Alpha = 2
                    },
                    new CharacterBattleEffectConfig
                    {
                        Animation = "",
                        Alpha = 1
                    },
                    new CharacterBattleEffectConfig
                    {
                        Animation = "AIM_FIRE_HAIR",
                        Blend = CharacterBattleEffectBlendModes.Add,
                        Alpha = 0.25f
                    },
                    new CharacterBattleEffectConfig
                    {
                        Animation = "aim_fire_hip",
                        Blend = CharacterBattleEffectBlendModes.Add,
                        Alpha = 0.5f,
                        Loop = false
                    }
                ],
                CoverIdle = "cover_idle",
                ToCover = "to_cover",
                ReloadSequence = ["cover_reload"]
            }
        };

    private CharacterBattleResourceConfig CreateBattleResource(
        string directoryName)
    {
        string directory = Path.Combine(
            _temporaryDirectory,
            directoryName);
        Directory.CreateDirectory(directory);
        string skeleton = Path.Combine(directory, "resource.skel");
        string atlas = Path.Combine(directory, "resource.atlas");
        string texture = Path.Combine(directory, "resource.png");
        File.WriteAllBytes(skeleton, []);
        File.WriteAllText(atlas, string.Empty);
        File.WriteAllBytes(texture, []);
        return new CharacterBattleResourceConfig
        {
            SkeletonPath = skeleton,
            AtlasPath = atlas,
            TexturePath = texture
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
