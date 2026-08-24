using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using System.Windows.Input;
using SpinePet.Models;
using SpinePet.Services;
using ICollectionView = System.ComponentModel.ICollectionView;

namespace SpinePet.ViewModels;

/// <summary>
/// Binds as the MainWindow DataContext and owns every INPC state that the
/// configuration panel displays. View-side reactions (thumbnail layout,
/// UIA announcements) and controller reactions (scale commit, settings
/// sync, filter refresh) subscribe to the exposed events instead of being
/// called from property setters.
/// </summary>
public sealed class MainViewModel : INotifyPropertyChanged,
    ICharacterSettingsHost
{
    private readonly CharacterManager _characterManager;
    private readonly ObservableCollection<CharacterViewModel> _characters = new();
    private readonly ObservableCollection<string> _selectedAnimationNames = new();
    private readonly ObservableCollection<string> _displaySelectionOptions = new();
    private bool _isRefreshingSelection;
    private bool _isUpdatingDisplaySelection;
    private CharacterViewModel? _selectedCharacter;
    private string _selectedAnimation = string.Empty;
    private string _selectedDisplayMode = CharacterDisplayModes.Normal;
    private string _selectedBattleState = CharacterBattleStates.Cover;
    private double _selectedScale =
        CharacterSettingsDefaults.DefaultScale;
    private double _selectedScaleMax =
        CharacterSettingsDefaults.DefaultMaxScale;
    private double _selectedScaleBasePercent =
        CharacterSettingsDefaults.MaximumScaleBasePercent;
    private double _selectedScaleMultiplier =
        CharacterSettingsDefaults.MinimumScaleMultiplier;
    private double _selectedSpeed = 100;
    private bool _allowRenderDrag;
    private int _targetFrameRate = GlobalConfig.DefaultTargetFrameRate;
    private int _thumbnailScalePercent = 100;
    private int _matchingCharacterCount;
    private string _characterSearchText = string.Empty;
    private string? _selectionBeforeSearchId;

    public MainViewModel(CharacterManager characterManager)
    {
        _characterManager = characterManager;
        _allowRenderDrag = characterManager.AllowRenderDrag;
        _targetFrameRate = characterManager.TargetFrameRate;
        _thumbnailScalePercent =
            characterManager.LibraryThumbnailScalePercent;
        CharacterView = CollectionViewSource.GetDefaultView(Characters);
        CharacterView.Filter = item =>
            item is CharacterViewModel character &&
            CharacterSearchMatcher.Matches(character, CharacterSearchText);
    }

    /// <summary>Raised by scale component setters; commits the scale.</summary>
    public event Action? ScaleComponentsChanged;

    /// <summary>Raised before a settings sync; refreshes host panel state.</summary>
    public event Action? SettingsSyncRequested;

    /// <summary>Raised by the search text setter; refreshes the filter.</summary>
    public event Action<CharacterViewModel?>? SearchFilterRequested;

    /// <summary>Raised after the thumbnail scale changed; reapplies layout.</summary>
    public event Action? ThumbnailScaleApplied;

    public ObservableCollection<CharacterViewModel> Characters =>
        _characters;

    public bool HasCharacters => Characters.Count > 0;

    public ICollectionView CharacterView { get; }

    public ObservableCollection<string> SelectedAnimationNames =>
        _selectedAnimationNames;

    public ObservableCollection<string> DisplaySelectionOptions =>
        _displaySelectionOptions;

    public string CharacterSearchText
    {
        get => _characterSearchText;
        set
        {
            string normalized = value ?? string.Empty;
            if (_characterSearchText == normalized)
            {
                return;
            }

            bool hadSearch = HasCharacterSearch;
            bool willSearch =
                !string.IsNullOrWhiteSpace(normalized);
            CharacterViewModel? preferredSelection =
                SelectedCharacter;
            if (!hadSearch && willSearch)
            {
                _selectionBeforeSearchId =
                    SelectedCharacter?.Id;
            }
            else if (hadSearch && !willSearch)
            {
                preferredSelection = Characters.FirstOrDefault(
                    character =>
                        character.Id == _selectionBeforeSearchId) ??
                    SelectedCharacter;
                _selectionBeforeSearchId = null;
            }

            _characterSearchText = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasCharacterSearch));
            OnPropertyChanged(nameof(HasCharacterSearchInput));
            SearchFilterRequested?.Invoke(preferredSelection);
        }
    }

    public bool HasCharacterSearch =>
        !string.IsNullOrWhiteSpace(CharacterSearchText);

    public bool HasCharacterSearchInput =>
        CharacterSearchText.Length > 0;

    public int MatchingCharacterCount => _matchingCharacterCount;

    public string CharacterCountDisplay =>
        HasCharacterSearch
            ? $"{MatchingCharacterCount} of {Characters.Count}"
            : $"{Characters.Count} loaded";

    public string CharacterSearchStatus =>
        HasCharacterSearch
            ? $"{MatchingCharacterCount} matching characters out of " +
              $"{Characters.Count} loaded"
            : $"{Characters.Count} characters loaded";

    public CharacterViewModel? SelectedCharacter
    {
        get => _selectedCharacter;
        set
        {
            if (ReferenceEquals(_selectedCharacter, value))
            {
                return;
            }

            _selectedCharacter = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedCharacter));
        }
    }

    public bool HasSelectedCharacter => SelectedCharacter != null;

    public IReadOnlyList<string> DisplayModeOptions { get; } =
        [CharacterDisplayModes.Normal, CharacterDisplayModes.Battle];

    public IReadOnlyList<string> BattleStateOptions { get; } =
        [CharacterBattleStates.Cover, CharacterBattleStates.Aim];

    public bool HasSelectedBattle =>
        FindSelectedCharacterConfig()?.Battle != null;

    public bool IsDisplaySelectionEnabled =>
        HasSelectedCharacter &&
        (SelectedDisplayMode == CharacterDisplayModes.Battle
            ? HasSelectedBattle
            : _displaySelectionOptions.Count > 0);

    public bool IsUpdatingDisplaySelection =>
        _isUpdatingDisplaySelection;

    public string SelectedAnimation
    {
        get => _selectedAnimation;
        set
        {
            if (_selectedAnimation == value)
            {
                return;
            }

            _selectedAnimation = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedDisplaySelection));
        }
    }

    public string SelectedDisplayMode
    {
        get => _selectedDisplayMode;
        set
        {
            if (_selectedDisplayMode == value)
                return;
            _selectedDisplayMode = value;
            OnPropertyChanged();
            _isUpdatingDisplaySelection = true;
            try
            {
                RefreshDisplaySelectionOptions();
            }
            finally
            {
                _isUpdatingDisplaySelection = false;
            }
        }
    }

    public string SelectedBattleState
    {
        get => _selectedBattleState;
        set
        {
            if (_selectedBattleState == value)
                return;
            _selectedBattleState = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedDisplaySelection));
        }
    }

    public string SelectedDisplaySelection
    {
        get => SelectedDisplayMode == CharacterDisplayModes.Battle
            ? SelectedBattleState
            : SelectedAnimation;
        set
        {
            if (string.IsNullOrWhiteSpace(value) ||
                !_displaySelectionOptions.Contains(value))
            {
                return;
            }

            if (SelectedDisplayMode == CharacterDisplayModes.Battle)
            {
                SelectedBattleState = value;
            }
            else
            {
                SelectedAnimation = value;
            }

            OnPropertyChanged();
        }
    }

    public double SelectedScale
    {
        get => _selectedScale;
        set
        {
            if (NearlyEquals(_selectedScale, value))
            {
                return;
            }

            _selectedScale = value;
            OnPropertyChanged();
        }
    }

    public double SelectedScaleMax
    {
        get => _selectedScaleMax;
        set
        {
            double normalized = Math.Clamp(
                value,
                CharacterSettingsDefaults.MinimumMaximumScale,
                CharacterSettingsDefaults.DefaultMaxScale);
            if (NearlyEquals(_selectedScaleMax, normalized))
            {
                return;
            }

            _selectedScaleMax = normalized;
            OnPropertyChanged();
        }
    }

    public double SelectedScaleBasePercent
    {
        get => _selectedScaleBasePercent;
        set
        {
            double normalized = Math.Clamp(
                value,
                CharacterSettingsDefaults.MinimumScaleBasePercent,
                CharacterSettingsDefaults.MaximumScaleBasePercent);
            if (NearlyEquals(_selectedScaleBasePercent, normalized))
            {
                return;
            }

            _selectedScaleBasePercent = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedScaleBasePercentDisplay));
            ScaleComponentsChanged?.Invoke();
        }
    }

    public double SelectedScaleMultiplier
    {
        get => _selectedScaleMultiplier;
        set
        {
            double normalized = Math.Clamp(
                value,
                CharacterSettingsDefaults.MinimumScaleMultiplier,
                CharacterSettingsDefaults.MaximumScaleMultiplier);
            if (NearlyEquals(_selectedScaleMultiplier, normalized))
            {
                return;
            }

            _selectedScaleMultiplier = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedScaleMultiplierDisplay));
            ScaleComponentsChanged?.Invoke();
        }
    }

    public string SelectedScaleBasePercentDisplay =>
        $"{SelectedScaleBasePercent:F0}%";

    public string SelectedScaleMultiplierDisplay =>
        $"x{SelectedScaleMultiplier:F1}";

    public double SelectedSpeed
    {
        get => _selectedSpeed;
        set
        {
            double normalized = Math.Clamp(value, 10, 200);
            if (NearlyEquals(_selectedSpeed, normalized))
            {
                return;
            }

            _selectedSpeed = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedSpeedDisplay));
        }
    }

    public string SelectedSpeedDisplay =>
        $"{SelectedSpeed / 100.0:F2}x";

    public bool AllowRenderDrag
    {
        get => _allowRenderDrag;
        set
        {
            if (_allowRenderDrag == value)
            {
                return;
            }

            _allowRenderDrag = value;
            OnPropertyChanged();
            _characterManager.SetAllowRenderDrag(value);
        }
    }

    public IReadOnlyList<int> FrameRateOptions { get; } =
    [
        GlobalConfig.PowerSavingTargetFrameRate,
        GlobalConfig.DefaultTargetFrameRate,
        GlobalConfig.HighRefreshTargetFrameRate
    ];

    public int TargetFrameRate
    {
        get => _targetFrameRate;
        set
        {
            int normalized = GlobalConfig.NormalizeTargetFrameRate(value);
            if (_targetFrameRate == normalized)
            {
                return;
            }

            _targetFrameRate = normalized;
            OnPropertyChanged();
            _characterManager.SetTargetFrameRate(normalized);
        }
    }

    public int ThumbnailScalePercent
    {
        get => _thumbnailScalePercent;
        set
        {
            int normalized =
                GlobalConfig.NormalizeLibraryThumbnailScale(value);
            if (_thumbnailScalePercent == normalized)
            {
                return;
            }

            _thumbnailScalePercent = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ThumbnailScaleDisplay));
            ThumbnailScaleApplied?.Invoke();
            _characterManager.SetLibraryThumbnailScale(normalized);
        }
    }

    public string ThumbnailScaleDisplay => $"{ThumbnailScalePercent}%";

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsRefreshingSelection
    {
        get => _isRefreshingSelection;
        set => _isRefreshingSelection = value;
    }

    public void OnSelectionChangedByView(CharacterViewModel? selectedItem)
    {
        SelectedCharacter = selectedItem;
        if (HasCharacterSearch)
        {
            _selectionBeforeSearchId = SelectedCharacter?.Id;
        }
    }

    public void ClearSearch() =>
        CharacterSearchText = string.Empty;

    public void NotifySearchResultsChanged()
    {
        _matchingCharacterCount =
            CharacterView.Cast<object>().Count();
        OnPropertyChanged(nameof(MatchingCharacterCount));
        OnPropertyChanged(nameof(CharacterCountDisplay));
        OnPropertyChanged(nameof(CharacterSearchStatus));
        CommandManager.InvalidateRequerySuggested();
    }

    public void SyncSelectedCharacterSettings()
    {
        SettingsSyncRequested?.Invoke();
        _isRefreshingSelection = true;
        try
        {
            CharacterConfig? character = FindSelectedCharacterConfig();
            SelectedDisplayMode = character == null
                ? CharacterDisplayModes.Normal
                : _characterManager.GetCharacterDisplayMode(character.Id);
            SelectedBattleState = character == null
                ? CharacterBattleStates.Cover
                : _characterManager.GetCharacterBattleState(character.Id);
            OnPropertyChanged(nameof(HasSelectedBattle));
            RefreshDisplaySelectionOptions();
        }
        finally
        {
            _isRefreshingSelection = false;
        }
    }

    public void ApplyRuntimeBattleState(
        string characterId,
        string mode,
        string battleState)
    {
        if (SelectedCharacter?.Id != characterId)
            return;

        _isRefreshingSelection = true;
        try
        {
            SelectedDisplayMode = mode;
            SelectedBattleState = battleState;
            RefreshDisplaySelectionOptions();
        }
        finally
        {
            _isRefreshingSelection = false;
        }
    }

    public CharacterConfig? FindSelectedCharacterConfig() =>
        SelectedCharacter == null
            ? null
            : _characterManager.Characters.FirstOrDefault(
                character => character.Id == SelectedCharacter.Id);

    private void RefreshDisplaySelectionOptions()
    {
        _displaySelectionOptions.Clear();
        IEnumerable<string> options =
            SelectedDisplayMode == CharacterDisplayModes.Battle
                ? BattleStateOptions
                : SelectedAnimationNames;
        foreach (string option in options)
        {
            _displaySelectionOptions.Add(option);
        }

        OnPropertyChanged(nameof(SelectedDisplaySelection));
        OnPropertyChanged(nameof(IsDisplaySelectionEnabled));
    }

    private static bool NearlyEquals(double left, double right) =>
        Math.Abs(left - right) < 0.0001;

    private void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
}
