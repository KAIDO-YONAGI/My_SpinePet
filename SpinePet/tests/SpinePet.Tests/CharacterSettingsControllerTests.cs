using System.Collections.ObjectModel;
using System.Windows.Threading;
using SpinePet.Models;
using SpinePet.Services;
using SpinePet.Tests.TestDoubles;
using SpinePet.ViewModels;
using SpinePet.Views;

namespace SpinePet.Tests;

public sealed class CharacterSettingsControllerTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SpinePet-CharacterSettings-{Guid.NewGuid():N}");

    [Fact]
    public void ScaleMultiplierCommitDoesNotRewriteBaseScale()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        CharacterConfig character = new()
        {
            Id = "scale-character",
            Name = "Scale Character",
            Scale = 0.1
        };
        ConfigService configService = new(Path.Combine(
            _temporaryDirectory,
            "config.json"));
        configService.Save(new AppConfig
        {
            Characters = [character]
        });

        FakeCharacterRenderHost renderHost = new();
        CharacterManager characterManager = new(
            configService,
            identityService: null,
            renderHost);
        CharacterViewModel selectedCharacter = new()
        {
            Id = character.Id,
            Name = character.Name,
            Scale = character.Scale,
            MaxScale = 2
        };
        TestSettingsHost host = new()
        {
            SelectedCharacter = selectedCharacter,
            SelectedScale = character.Scale,
            SelectedScaleMax = 2,
            SelectedScaleBasePercent = 50,
            SelectedScaleMultiplier = 2
        };
        CharacterSettingsController controller = new(
            characterManager,
            new CharacterResourceStorageService(),
            new ObservableCollection<CharacterViewModel>
            {
                selectedCharacter
            },
            host,
            owner: null!,
            Dispatcher.CurrentDispatcher,
            getKnownResources: () =>
                new Dictionary<string, CharacterResourceFiles>(),
            refreshKnownResources: () => { },
            synchronizeKnownResources: () => new(0, 0, 0, 0),
            refreshCharacterList: () => { });
        characterManager.CharacterScaleChanged +=
            controller.HandleCharacterScaleChanged;
        renderHost.SetCharacterScaleHandler = (characterId, scale) =>
            renderHost.RaiseScaleChanged(characterId, 2, scale);

        controller.CommitSelectedScale();
        renderHost.RaiseScaleChanged(character.Id, 2, 0.2);

        Assert.Equal(50, host.SelectedScaleBasePercent);
        Assert.Equal(2, host.SelectedScaleMultiplier);
        Assert.Equal(0.2, host.SelectedScale, precision: 10);
        Assert.Equal(0.2, selectedCharacter.Scale, precision: 10);
        Assert.Equal(
            50,
            characterManager.Characters[0].ScaleBasePercent);
        Assert.Equal(
            2,
            characterManager.Characters[0].ScaleMultiplier);

        host.SelectedScaleBasePercent = 100;
        host.SelectedScaleMultiplier = 1;
        controller.SyncSelectedCharacterSettings();

        Assert.Equal(50, host.SelectedScaleBasePercent);
        Assert.Equal(2, host.SelectedScaleMultiplier);
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private sealed class TestSettingsHost : ICharacterSettingsHost
    {
        public CharacterViewModel? SelectedCharacter { get; set; }
        public bool IsRefreshingSelection { get; set; }
        public string SelectedAnimation { get; set; } = string.Empty;
        public double SelectedScale { get; set; }
        public double SelectedScaleMax { get; set; }
        public double SelectedScaleBasePercent { get; set; }
        public double SelectedScaleMultiplier { get; set; }
        public double SelectedSpeed { get; set; }
        public ObservableCollection<string> SelectedAnimationNames { get; } = [];
    }
}
