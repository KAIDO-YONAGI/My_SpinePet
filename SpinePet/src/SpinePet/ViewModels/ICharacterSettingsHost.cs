using System.Collections.ObjectModel;

namespace SpinePet.ViewModels;

internal interface ICharacterSettingsHost
{
    CharacterViewModel? SelectedCharacter { get; }
    bool IsRefreshingSelection { get; set; }
    string SelectedAnimation { get; set; }
    double SelectedScale { get; set; }
    double SelectedScaleMax { get; set; }
    double SelectedScaleBasePercent { get; set; }
    double SelectedScaleMultiplier { get; set; }
    double SelectedSpeed { get; set; }
    ObservableCollection<string> SelectedAnimationNames { get; }
}
