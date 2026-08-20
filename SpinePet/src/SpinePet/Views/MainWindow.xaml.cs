using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using SpinePet.Infrastructure;
using SpinePet.Models;
using SpinePet.Services;
using SpinePet.ViewModels;

namespace SpinePet.Views;

public partial class MainWindow : Window, INotifyPropertyChanged, IDisposable
{
    private const double DefaultMaxScale = 2.0;
    private const double DefaultScale = 0.2;
    private const double MinimumScale = 0.05;
    private const double MinimumMaximumScale = 0.2;

    private readonly CharacterManager _characterManager;
    private readonly CharacterResourceDiscoveryService _resourceDiscovery;
    private readonly UnityBundleImportService _bundleImporter;
    private readonly CharacterIconDownloadService _characterIconDownloader;
    private readonly CharacterResourceStorageService _resourceStorage = new();
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly DispatcherTimer _searchAnnouncementTimer;
    private Dictionary<string, CharacterResourceFiles> _knownResources =
        new(StringComparer.OrdinalIgnoreCase);
    private OverlayWindow? _overlayWindow;
    private bool _isRefreshingSelection;
    private bool _isUpdatingCharacterSelection;
    private bool _isDeletingSkin;
    private bool _isConfigMode = true;
    private CharacterViewModel? _selectedCharacter;
    private string _selectedAnimation = string.Empty;
    private double _selectedScale = DefaultScale;
    private double _selectedScaleMax = DefaultMaxScale;
    private double _selectedScalePercent;
    private double _selectedSpeed = 100;
    private bool _allowRenderDrag;
    private int _targetFrameRate = GlobalConfig.DefaultTargetFrameRate;
    private string _characterSearchText = string.Empty;
    private string? _selectionBeforeSearchId;
    private int _allowClose;
    private int _disposeState;

    public static RoutedUICommand SwitchSkinCommand { get; } =
        new(
            "Switch character skin",
            nameof(SwitchSkinCommand),
            typeof(MainWindow));

    public static RoutedUICommand FocusCharacterSearchCommand { get; } =
        new(
            "Focus character search",
            nameof(FocusCharacterSearchCommand),
            typeof(MainWindow),
            new InputGestureCollection
            {
                new KeyGesture(Key.F, ModifierKeys.Control)
            });

    public static RoutedUICommand ClearCharacterSearchCommand { get; } =
        new(
            "Clear character search",
            nameof(ClearCharacterSearchCommand),
            typeof(MainWindow));

    public static RoutedUICommand FocusCharacterResultsCommand { get; } =
        new(
            "Focus character results",
            nameof(FocusCharacterResultsCommand),
            typeof(MainWindow));

    public MainWindow(
        CharacterManager characterManager,
        CharacterResourceDiscoveryService resourceDiscovery,
        UnityBundleImportService bundleImporter,
        CharacterIconDownloadService? characterIconDownloader = null)
    {
        InitializeComponent();
        _searchAnnouncementTimer = new DispatcherTimer(
            DispatcherPriority.Background,
            Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _searchAnnouncementTimer.Tick +=
            OnCharacterSearchAnnouncementTick;
        _characterManager = characterManager;
        _resourceDiscovery = resourceDiscovery;
        _bundleImporter = bundleImporter;
        _characterIconDownloader = characterIconDownloader ?? new();
        _allowRenderDrag = characterManager.AllowRenderDrag;
        _targetFrameRate = characterManager.TargetFrameRate;
        CharacterView = CollectionViewSource.GetDefaultView(Characters);
        CharacterView.Filter = item =>
            item is CharacterViewModel character &&
            CharacterSearchMatcher.Matches(character, CharacterSearchText);
        CommandBindings.Add(new CommandBinding(
            SwitchSkinCommand,
            OnCharacterSkinExecuted));
        CommandBindings.Add(new CommandBinding(
            FocusCharacterSearchCommand,
            OnFocusCharacterSearchExecuted));
        CommandBindings.Add(new CommandBinding(
            ClearCharacterSearchCommand,
            OnClearCharacterSearchExecuted,
            OnCanClearCharacterSearchExecuted));
        CommandBindings.Add(new CommandBinding(
            FocusCharacterResultsCommand,
            OnFocusCharacterResultsExecuted,
            OnCanFocusCharacterResultsExecuted));
        DataContext = this;

        RefreshKnownResources();
        RefreshCharacterList();
        _characterManager.CharactersChanged += RefreshCharacterList;
        _characterManager.CharacterScaleChanged += OnCharacterScaleChanged;

        LocationChanged += (_, _) => UpdateOverlayBounds();
        SizeChanged += (_, _) => UpdateOverlayBounds();
    }

    public ObservableCollection<CharacterViewModel> Characters { get; } = new();

    public bool HasCharacters => Characters.Count > 0;

    public ICollectionView CharacterView { get; }

    public ObservableCollection<string> SelectedAnimationNames { get; } = new();

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
            RefreshCharacterFilter(preferredSelection);
        }
    }

    public bool HasCharacterSearch =>
        !string.IsNullOrWhiteSpace(CharacterSearchText);

    public bool HasCharacterSearchInput =>
        CharacterSearchText.Length > 0;

    public int MatchingCharacterCount =>
        CharacterView.Cast<object>().Count();

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
                MinimumMaximumScale,
                DefaultMaxScale);
            if (NearlyEquals(_selectedScaleMax, normalized))
            {
                return;
            }

            _selectedScaleMax = normalized;
            OnPropertyChanged();
        }
    }

    public double SelectedScalePercent
    {
        get => _selectedScalePercent;
        set
        {
            double normalized = Math.Clamp(value, 0, 100);
            if (NearlyEquals(_selectedScalePercent, normalized))
            {
                return;
            }

            _selectedScalePercent = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedScaleDisplay));
        }
    }

    public string SelectedScaleDisplay => $"{SelectedScalePercent:F0}%";

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

    public string SelectedSpeedDisplay => $"{SelectedSpeed / 100.0:F2}x";

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

    public event PropertyChangedEventHandler? PropertyChanged;

    internal bool IsDisposed =>
        Volatile.Read(ref _disposeState) != 0;

    public void SwitchToConfigMode()
    {
        if (IsDisposed ||
            Volatile.Read(ref _allowClose) != 0 ||
            Dispatcher.HasShutdownStarted ||
            Dispatcher.HasShutdownFinished)
        {
            return;
        }

        AppLogger.Write(
            nameof(MainWindow),
            "configuration-panel-opened");
        _isConfigMode = true;
        ApplyConfigMode();
        Show();
        Activate();
    }

    internal void PrepareForShutdown()
    {
        Interlocked.Exchange(ref _allowClose, 1);
        Dispose();
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        Rect workArea = SystemParameters.WorkArea;
        Width = 468;
        Left = workArea.Right - Width;
        Top = workArea.Top;
        Height = workArea.Height;
        EnsureOverlayWindow();
        ApplyConfigMode();
    }

    private void OnWindowChromeMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed)
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // The button can be released between the event and DragMove.
        }
    }

    private void ApplyConfigMode()
    {
        if (_isConfigMode)
        {
            ModeLabel.Text = "Configuration Mode";
            ShowOverlay();
            Topmost = true;
            Show();
            Activate();
        }
        else
        {
            HideOverlay();
            Hide();
        }

        _characterManager.SetConfigMode(_isConfigMode);
    }

    private void EnsureOverlayWindow()
    {
        if (_overlayWindow != null)
        {
            return;
        }

        _overlayWindow = new OverlayWindow(_characterManager);
        _overlayWindow.CharacterMoved += OnOverlayCharacterMoved;
        UpdateOverlayBounds();
        UpdateOverlayState();
    }

    private void ShowOverlay()
    {
        EnsureOverlayWindow();
        UpdateOverlayBounds();
        UpdateOverlayState();
        _overlayWindow?.ShowOverlay();
    }

    private void HideOverlay()
    {
        _overlayWindow?.HideOverlay();
    }

    private void UpdateOverlayBounds()
    {
        if (_overlayWindow == null)
        {
            return;
        }

        Rect workArea = SystemParameters.WorkArea;
        double previewWidth = Math.Max(0, Left - workArea.Left);
        _overlayWindow.SetPreviewBounds(
            new Rect(workArea.Left, workArea.Top, previewWidth, workArea.Height));
    }

    private void UpdateOverlayState()
    {
        if (_overlayWindow == null)
        {
            return;
        }

        _overlayWindow.SelectedCharacterId = SelectedCharacter?.Id;
        _overlayWindow.MoveSelectedCharacter =
            MoveSelectedCheckBox?.IsChecked == true;
        if (!_overlayWindow.MoveSelectedCharacter)
        {
            _overlayWindow.CancelDrag();
        }
    }

    private void SyncSelectedCharacterSettings()
    {
        _isRefreshingSelection = true;
        SelectedAnimationNames.Clear();

        if (SelectedCharacter == null)
        {
            SelectedAnimation = string.Empty;
            SelectedScale = DefaultScale;
            SelectedScaleMax = DefaultMaxScale;
            SelectedScalePercent = 0;
            SelectedSpeed = 100;
            _isRefreshingSelection = false;
            UpdateOverlayState();
            return;
        }

        foreach (string animation in SelectedCharacter.AnimationNames)
        {
            SelectedAnimationNames.Add(animation);
        }

        SelectedScaleMax = Math.Clamp(
            SelectedCharacter.MaxScale > 0
                ? SelectedCharacter.MaxScale
                : DefaultMaxScale,
            MinimumMaximumScale,
            DefaultMaxScale);
        SelectedScale = Math.Clamp(
            SelectedCharacter.Scale > 0
                ? SelectedCharacter.Scale
                : DefaultScale,
            MinimumScale,
            SelectedScaleMax);
        SelectedScalePercent = ConvertScaleToPercent(
            SelectedScale,
            SelectedScaleMax);
        SelectedAnimation =
            !string.IsNullOrEmpty(SelectedCharacter.ConfiguredAnimation)
                ? SelectedCharacter.ConfiguredAnimation
                : SelectedAnimationNames.FirstOrDefault() ?? string.Empty;
        SelectedSpeed = Math.Clamp(
            SelectedCharacter.AnimationSpeed * 100.0,
            10,
            200);

        _isRefreshingSelection = false;
        UpdateOverlayState();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);

        if (Dispatcher.HasShutdownStarted ||
            Dispatcher.HasShutdownFinished)
        {
            return;
        }

        // Keep the tray-owned window reusable for Close/Alt+F4. During a
        // genuine Application.Shutdown WPF ignores cancellation and still
        // continues through OnClosed, so the cleanup below remains reachable.
        AppLogger.Write(
            nameof(MainWindow),
            "configuration-panel-close-intercepted");
        e.Cancel = true;
        _characterManager.SaveAllState();
        _isConfigMode = false;
        ApplyConfigMode();
    }

    protected override void OnClosed(EventArgs e)
    {
        Dispose();
        _characterManager.CharactersChanged -= RefreshCharacterList;
        _characterManager.CharacterScaleChanged -= OnCharacterScaleChanged;

        if (_overlayWindow != null)
        {
            _overlayWindow.CharacterMoved -= OnOverlayCharacterMoved;
            _overlayWindow.Close();
            _overlayWindow = null;
        }

        base.OnClosed(e);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        _searchAnnouncementTimer.Stop();
        _searchAnnouncementTimer.Tick -=
            OnCharacterSearchAnnouncementTick;
        try
        {
            _lifetimeCancellation.Cancel();
        }
        catch (Exception exception)
        {
            AppLogger.Write(
                nameof(MainWindow),
                $"lifetime-cancellation-failed message={exception.Message}");
        }
        finally
        {
            _lifetimeCancellation.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    private static bool NearlyEquals(double left, double right) =>
        Math.Abs(left - right) < 0.0001;

    private void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
