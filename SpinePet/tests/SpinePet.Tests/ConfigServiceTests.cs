using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using SpinePet.Models;
using SpinePet.Services;

namespace SpinePet.Tests;

public sealed class ConfigServiceTests : IDisposable
{
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
        ConfigService service = new(configPath);
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

        ConfigService service = new(configPath);
        AppConfig config = service.Load();

        Assert.Equal(AppConfig.CurrentVersion, config.Version);
        CharacterConfig character = Assert.Single(config.Characters);
        Assert.Equal(string.Empty, character.ConfiguredAnimation);
        Assert.Equal(1.0, character.AnimationSpeed);
        Assert.Empty(character.AdditionalTexturePaths);
        Assert.True(character.RequiresStandingMigration);
        Assert.Null(character.LegacyResourceType);
        Assert.Equal(
            SystemParameters.WorkArea.Left +
            SystemParameters.WorkArea.Width / 2,
            character.PositionX);
        Assert.Equal(
            SystemParameters.WorkArea.Bottom - 24,
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

        ConfigService service = new(configPath);
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

        ConfigService.Normalize(config);

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
            Version = "1.6",
            Characters = [character]
        };

        ConfigService.Normalize(config);

        Assert.Equal("1.6", config.Version);
        Assert.Equal(321, character.PositionX);
        Assert.Equal(654, character.PositionY);
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }
}
