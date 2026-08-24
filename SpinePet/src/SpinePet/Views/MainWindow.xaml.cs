using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using SpinePet.Infrastructure;
using SpinePet.Models;
using SpinePet.Services;
using SpinePet.ViewModels;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using ListBox = System.Windows.Controls.ListBox;
using Slider = System.Windows.Controls.Slider;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = System.Windows.Controls.TextBox;

namespace SpinePet.Views;

public class MainWindow : Window, IDisposable
{
    private readonly CharacterManager _characterManager;
    private readonly DispatcherTimer _searchAnnouncementTimer;
    private readonly MainWindowLifecycleController _lifecycle;
    private readonly CharacterResourceStorageService _resourceStorage = new();
    private readonly MainViewModel _viewModel;
    private CharacterPreviewNavigationController _previewNavigation = null!;
    private CharacterLibraryController _libraryController = null!;
    private CharacterSettingsController _settingsController = null!;
    private CharacterPanelActivationController _panelActivation = null!;
    private TextBlock _characterCountText = null!;
    private TextBox _characterSearchBox = null!;
    private ListBox _characterCards = null!;
    private ComboBox _animationCombo = null!;
    private ComboBox _displayModeCombo = null!;
    private Slider _speedSlider = null!;
    private Border _windowChrome = null!;
    private Button _addCharacterButton = null!;
    private Button _importNikkeDbButton = null!;
    private Button _scanResourcesButton = null!;
    private Button _openResourceFolderButton = null!;
    private ComboBox _batchProcessingCombo = null!;
    private Button _resetScaleButton = null!;
    private Button _resetSpeedButton = null!;
    private Button _deleteSkinButton = null!;
    private Button _finishConfigurationButton = null!;
    private Button _exitButton = null!;
    private CheckBox _allowDraggingToggle = null!;

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
        NikkeDbResourceImportService nikkeDbImporter,
        CharacterIconDownloadService? characterIconDownloader = null)
    {
        LoadView();
        _characterManager = characterManager;
        _lifecycle = new MainWindowLifecycleController(this, characterManager);
        _searchAnnouncementTimer = new DispatcherTimer(
            DispatcherPriority.Background,
            Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _searchAnnouncementTimer.Tick +=
            OnCharacterSearchAnnouncementTick;

        _characterCountText =
            RequireNamedElement<TextBlock>("CharacterCountText");
        _characterSearchBox =
            RequireNamedElement<TextBox>("CharacterSearchBox");
        _characterCards =
            RequireNamedElement<ListBox>("CharacterCards");
        _animationCombo =
            RequireNamedElement<ComboBox>("AnimationCombo");
        _displayModeCombo =
            RequireNamedElement<ComboBox>("DisplayModeCombo");
        _speedSlider =
            RequireNamedElement<Slider>("SpeedSlider");
        _windowChrome =
            RequireNamedElement<Border>("WindowChrome");
        _addCharacterButton =
            RequireNamedElement<Button>("AddCharacterButton");
        _importNikkeDbButton =
            RequireNamedElement<Button>("ImportNikkeDbButton");
        _scanResourcesButton =
            RequireNamedElement<Button>("ScanResourcesButton");
        _openResourceFolderButton =
            RequireNamedElement<Button>("OpenResourceFolderButton");
        _batchProcessingCombo =
            RequireNamedElement<ComboBox>("BatchProcessingCombo");
        _resetScaleButton =
            RequireNamedElement<Button>("ResetScaleButton");
        _resetSpeedButton =
            RequireNamedElement<Button>("ResetSpeedButton");
        _deleteSkinButton =
            RequireNamedElement<Button>("DeleteSkinButton");
        _finishConfigurationButton =
            RequireNamedElement<Button>("FinishConfigurationButton");
        _exitButton =
            RequireNamedElement<Button>("ExitButton");
        _allowDraggingToggle =
            RequireNamedElement<CheckBox>("AllowDraggingToggle");

        _viewModel = new MainViewModel(characterManager);
        _previewNavigation = new CharacterPreviewNavigationController(
            _characterCards,
            _viewModel,
            Dispatcher,
            () => _lifecycle.IsConfigMode,
            () => IsDisposed,
            () => IsLoaded);
        _libraryController = new CharacterLibraryController(
            characterManager,
            resourceDiscovery,
            bundleImporter,
            nikkeDbImporter,
            characterIconDownloader ?? new(),
            _viewModel,
            _characterCards,
            _previewNavigation,
            Dispatcher,
            this,
            _viewModel.SyncSelectedCharacterSettings,
            AnnounceCharacterSearchStatus,
            () => _lifecycle.IsConfigMode,
            _lifecycle.LifetimeToken);
        _settingsController = new CharacterSettingsController(
            characterManager,
            _resourceStorage,
            _viewModel.Characters,
            _viewModel,
            this,
            Dispatcher,
            () => _libraryController.KnownResources,
            _libraryController.RefreshKnownResources,
            _libraryController.SynchronizeKnownResources,
            _libraryController.RefreshCharacterList);
        _panelActivation = new CharacterPanelActivationController(
            () => IsDisposed,
            () => _lifecycle.IsConfigMode,
            _lifecycle.ExitConfiguration,
            _libraryController.FindCharacter,
            _viewModel.CharacterView.Contains,
            _viewModel.ClearSearch,
            _lifecycle.SwitchToConfigMode,
            _previewNavigation.SelectAndReveal);

        _viewModel.SettingsSyncRequested +=
            _settingsController.SyncSelectedCharacterSettings;
        _viewModel.ScaleComponentsChanged +=
            _settingsController.CommitSelectedScale;
        _viewModel.ThumbnailScaleApplied += ApplyThumbnailScale;
        _viewModel.SearchFilterRequested +=
            _libraryController.RefreshCharacterFilter;
        _lifecycle.ConfigModeEntered += OnConfigModeEntered;

        AttachViewEvents();
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
        DataContext = _viewModel;

        _libraryController.RefreshKnownResources();
        _libraryController.RefreshCharacterList();
        _characterManager.CharactersChanged +=
            _libraryController.RefreshCharacterList;
        _characterManager.CharacterStateChanged +=
            _libraryController.UpdateCharacterState;
        _characterManager.CharacterScaleChanged +=
            _settingsController.HandleCharacterScaleChanged;
        _characterManager.CharacterPositionChanged +=
            OnCharacterPositionChanged;
        _characterManager.CharacterRightClicked +=
            OnCharacterRightClicked;
        _characterManager.CharacterBattleStateChanged +=
            OnCharacterBattleStateChanged;
    }

    internal bool IsDisposed => _lifecycle.IsDisposed;

    public void SwitchToConfigMode() =>
        _lifecycle.SwitchToConfigMode();

    internal void PrepareForShutdown() =>
        _lifecycle.PrepareForShutdown();

    private void LoadView()
    {
        System.Windows.Application.LoadComponent(
            this,
            new Uri(
                "/SpinePet;component/Views/MainWindow.xaml",
                UriKind.Relative));
    }

    private void AttachViewEvents()
    {
        Loaded += OnWindowLoaded;
        Closing += OnWindowClosing;
        Closed += OnWindowClosed;
        _windowChrome.MouseLeftButtonDown +=
            OnWindowChromeMouseLeftButtonDown;
        _addCharacterButton.Click += OnAddCharacter;
        _importNikkeDbButton.Click += OnImportNikkeDb;
        _scanResourcesButton.Click += OnScanResources;
        _openResourceFolderButton.Click += OnOpenResourceFolder;
        _batchProcessingCombo.SelectionChanged += OnBatchProcessingChanged;
        _characterCards.SelectionChanged += OnCharacterSelectionChanged;
        _characterCards.AddHandler(
            ScrollViewer.ScrollChangedEvent,
            new ScrollChangedEventHandler(OnCharacterCardsScrollChanged));
        _characterCards.PreviewMouseWheel +=
            OnCharacterCardsPreviewMouseWheel;
        _characterCards.PreviewKeyDown +=
            OnCharacterCardsPreviewKeyDown;
        _characterCards.AddHandler(
            Button.ClickEvent,
            new RoutedEventHandler(OnCharacterCardButtonClick));
        _animationCombo.SelectionChanged += OnDisplaySelectionChanged;
        _displayModeCombo.SelectionChanged += OnDisplayModeChanged;
        _speedSlider.ValueChanged += OnSpeedChanged;
        _allowDraggingToggle.Loaded += OnDragToggleLoaded;
        _allowDraggingToggle.Checked += OnDragToggleStateChanged;
        _allowDraggingToggle.Unchecked += OnDragToggleStateChanged;
        _resetScaleButton.Click += OnResetScale;
        _resetSpeedButton.Click += OnResetSpeed;
        _deleteSkinButton.Click += OnDeleteSelectedSkin;
        _finishConfigurationButton.Click += OnExitConfiguration;
        _exitButton.Click += OnExitApplication;
    }

    private void OnConfigModeEntered()
    {
        // Rebuild the library view models from current configuration and
        // runtime state so the panel never shows stale startup content.
        _libraryController.RefreshCharacterList();
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e) =>
        _lifecycle.HandleLoaded(ApplyThumbnailScale);

    private void OnCharacterSelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_libraryController.IsUpdatingSelection)
        {
            return;
        }

        _viewModel.OnSelectionChangedByView(
            _characterCards.SelectedItem as CharacterViewModel);
        _viewModel.SyncSelectedCharacterSettings();
        if (_lifecycle.IsConfigMode &&
            _viewModel.SelectedCharacter != null &&
            !_previewNavigation.IsApplyingScrollSelection)
        {
            _previewNavigation.Reveal(_viewModel.SelectedCharacter);
        }
    }

    private void OnCharacterCardsScrollChanged(
        object sender,
        ScrollChangedEventArgs e) =>
        _previewNavigation.HandleScrollChanged(
            e,
            _libraryController.IsUpdatingSelection);

    private void OnCharacterCardsPreviewMouseWheel(
        object sender,
        MouseWheelEventArgs e)
    {
        if (_previewNavigation.HandlePreviewMouseWheel(
                e.Delta,
                _libraryController.IsUpdatingSelection))
        {
            e.Handled = true;
        }
    }

    private async void OnCharacterCardsPreviewKeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (await _libraryController.HandlePreviewKeyDownAsync(e))
        {
            e.Handled = true;
        }
    }

    private void OnCharacterCardButtonClick(
        object sender,
        RoutedEventArgs e)
    {
        if (e.OriginalSource is not Button button)
        {
            return;
        }

        string? action =
            System.Windows.Automation.AutomationProperties.GetName(button);
        if (string.Equals(
                action,
                "Reset character position",
                StringComparison.Ordinal))
        {
            if (button.Tag is CharacterViewModel positionCharacter)
            {
                _previewNavigation.SelectWithoutReveal(positionCharacter);
            }

            _settingsController.ResetCharacterPosition(button);
            return;
        }

        if (button.Tag is CharacterViewModel character)
        {
            _previewNavigation.SelectWithoutReveal(character);
            _ = _libraryController.ToggleCharacterAsync(character);
        }
    }

    private void OnFocusCharacterSearchExecuted(
        object sender,
        ExecutedRoutedEventArgs e)
    {
        _characterSearchBox.Focus();
        _characterSearchBox.SelectAll();
        _characterSearchBox.BringIntoView();
        e.Handled = true;
    }

    private void OnClearCharacterSearchExecuted(
        object sender,
        ExecutedRoutedEventArgs e)
    {
        _viewModel.ClearSearch();
        _characterSearchBox.Focus();
        e.Handled = true;
    }

    private void OnCanClearCharacterSearchExecuted(
        object sender,
        CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = _viewModel.HasCharacterSearchInput;
        e.Handled = true;
    }

    private void OnFocusCharacterResultsExecuted(
        object sender,
        ExecutedRoutedEventArgs e)
    {
        _libraryController.FocusCharacterResults();
        e.Handled = true;
    }

    private void OnCanFocusCharacterResultsExecuted(
        object sender,
        CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = _viewModel.MatchingCharacterCount > 0;
        e.Handled = true;
    }

    private void OnAddCharacter(object sender, RoutedEventArgs e) =>
        _ = _libraryController.AddCharacterAsync(
            sender as Button);

    private void OnImportNikkeDb(object sender, RoutedEventArgs e) =>
        _libraryController.ImportFromNikkeDb();

    private void OnScanResources(object sender, RoutedEventArgs e) =>
        _ = _libraryController.ScanResourcesAsync(
            sender as Button);

    private void OnOpenResourceFolder(object sender, RoutedEventArgs e) =>
        _libraryController.OpenResourceFolder();

    private void OnBatchProcessingChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_batchProcessingCombo.SelectedIndex <= 0)
        {
            return;
        }

        switch (_batchProcessingCombo.SelectedIndex)
        {
            case 1:
                OnHideAll(sender, e);
                break;
            case 2:
                OnShowAll(sender, e);
                break;
            case 3:
                OnResetAllSettings(sender, e);
                break;
        }

        _batchProcessingCombo.SelectedIndex = 0;
    }

    private void OnHideAll(object sender, RoutedEventArgs e) =>
        _characterManager.HideAll();

    private async void OnShowAll(object sender, RoutedEventArgs e)
    {
        try
        {
            await _characterManager.ShowAllAsync();
        }
        catch (Exception exception)
        {
            AppLogger.Write(
                nameof(MainWindow),
                $"batch-show-all-failed message={exception.Message}");
        }
    }

    private void OnResetAllSettings(object sender, RoutedEventArgs e)
    {
        _characterManager.ResetAllSettings();
        _libraryController.RefreshCharacterList();
        _viewModel.SyncSelectedCharacterSettings();
    }

    private async void OnCharacterSkinExecuted(
        object sender,
        ExecutedRoutedEventArgs e) =>
        await _libraryController.HandleCharacterSkinAsync(
            e.Parameter as CharacterSkinOptionViewModel);

    private void OnSpeedChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e) =>
        _settingsController.HandleSpeedChanged(sender, e);

    private void OnResetScale(object sender, RoutedEventArgs e) =>
        _settingsController.ResetScale();

    private void OnResetSpeed(object sender, RoutedEventArgs e) =>
        _settingsController.ResetSpeed();

    private async void OnDisplaySelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_viewModel.IsRefreshingSelection ||
            _viewModel.IsUpdatingDisplaySelection ||
            _viewModel.FindSelectedCharacterConfig() is not { } character)
        {
            return;
        }

        string? selection = e.AddedItems
            .OfType<string>()
            .LastOrDefault() ??
            _animationCombo.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(selection) ||
            !_viewModel.DisplaySelectionOptions.Contains(selection))
        {
            return;
        }

        if (_viewModel.SelectedDisplayMode == CharacterDisplayModes.Normal)
        {
            if (string.Equals(
                    character.ConfiguredAnimation,
                    selection,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _viewModel.SelectedAnimation = selection;
            _settingsController.HandleAnimationChanged(sender, e);
            return;
        }

        if (!_viewModel.BattleStateOptions.Contains(selection) ||
            string.Equals(
                _characterManager.GetCharacterBattleState(character.Id),
                selection,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _viewModel.SelectedBattleState = selection;
        try
        {
            bool switched =
                await _characterManager.SetCharacterBattleStateAsync(
                    character,
                    selection);
            if (!switched)
                _viewModel.SyncSelectedCharacterSettings();
        }
        catch (Exception exception)
        {
            AppLogger.Write(
                nameof(MainWindow),
                $"battle-state-switch-failed id={character.Id} " +
                $"message={exception.Message}");
            _viewModel.SyncSelectedCharacterSettings();
        }
    }

    private async void OnDisplayModeChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_viewModel.IsRefreshingSelection ||
            _viewModel.FindSelectedCharacterConfig() is not { } character)
        {
            return;
        }

        string? mode = e.AddedItems
            .OfType<string>()
            .LastOrDefault() ??
            _displayModeCombo.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(mode) ||
            !_viewModel.DisplayModeOptions.Contains(mode) ||
            string.Equals(
                _characterManager.GetCharacterDisplayMode(character.Id),
                mode,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            bool switched =
                await _characterManager.SetCharacterDisplayModeAsync(
                    character,
                    mode);
            if (!switched)
                _viewModel.SyncSelectedCharacterSettings();
        }
        catch (Exception exception)
        {
            AppLogger.Write(
                nameof(MainWindow),
                $"display-mode-switch-failed id={character.Id} " +
                $"message={exception.Message}");
            _viewModel.SyncSelectedCharacterSettings();
        }
    }

    private async void OnDeleteSelectedSkin(
        object sender,
        RoutedEventArgs e) =>
        await _settingsController.DeleteSelectedSkinAsync();

    private void OnExitConfiguration(object sender, RoutedEventArgs e) =>
        _lifecycle.ExitConfiguration();

    private void OnExitApplication(object sender, RoutedEventArgs e) =>
        System.Windows.Application.Current?.Shutdown();

    private void OnCharacterRightClicked(string characterId) =>
        _panelActivation.HandleRightClick(characterId);

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

    private void OnDragToggleLoaded(object sender, RoutedEventArgs e) =>
        SetDragToggleKnob(sender, animate: false);

    private void OnDragToggleStateChanged(
        object sender,
        RoutedEventArgs e) =>
        SetDragToggleKnob(sender, animate: true);

    private void OnWindowClosing(object? sender, CancelEventArgs e) =>
        _lifecycle.HandleClosing(e);

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        _characterManager.CharactersChanged -=
            _libraryController.RefreshCharacterList;
        _characterManager.CharacterStateChanged -=
            _libraryController.UpdateCharacterState;
        _characterManager.CharacterScaleChanged -=
            _settingsController.HandleCharacterScaleChanged;
        _characterManager.CharacterPositionChanged -=
            OnCharacterPositionChanged;
        _characterManager.CharacterRightClicked -=
            OnCharacterRightClicked;
        _characterManager.CharacterBattleStateChanged -=
            OnCharacterBattleStateChanged;
        Dispose();
    }

    private void OnCharacterPositionChanged(
        string characterId,
        double left,
        double top)
    {
        CharacterViewModel? character = _viewModel.Characters.FirstOrDefault(
            item => item.Id == characterId);
        if (character == null)
        {
            return;
        }

        character.PositionX = (int)left;
        character.PositionY = (int)top;
    }

    private void OnCharacterBattleStateChanged(
        string characterId,
        string mode,
        string battleState) =>
        _viewModel.ApplyRuntimeBattleState(characterId, mode, battleState);

    private void AnnounceCharacterSearchStatus()
    {
        if (!IsLoaded || !_characterCountText.IsVisible)
        {
            return;
        }

        _searchAnnouncementTimer.Stop();
        _searchAnnouncementTimer.Start();
    }

    private void OnCharacterSearchAnnouncementTick(
        object? sender,
        EventArgs e)
    {
        _searchAnnouncementTimer.Stop();
        if (!_characterCountText.IsVisible)
        {
            return;
        }

        AutomationPeer? peer =
            UIElementAutomationPeer.FromElement(_characterCountText) ??
            UIElementAutomationPeer.CreatePeerForElement(_characterCountText);
        peer?.RaiseAutomationEvent(
            AutomationEvents.LiveRegionChanged);
    }

    private void ApplyThumbnailScale()
    {
        double scale = _viewModel.ThumbnailScalePercent / 100.0;
        _characterCards.LayoutTransform = scale == 1.0
            ? Transform.Identity
            : new ScaleTransform(scale, scale);

        double scrollBarWidth = 10.0 / scale;
        foreach (System.Windows.Controls.Primitives.ScrollBar scrollBar
                 in FindVisualChildren<System.Windows.Controls.Primitives.ScrollBar>(
                     _characterCards))
        {
            scrollBar.Width = scrollBarWidth;
            scrollBar.MinWidth = scrollBarWidth;
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (PresentationSource.FromVisual(this) is HwndSource source)
        {
            source.AddHook(OnWindowMessage);
        }
    }

    private IntPtr OnWindowMessage(
        IntPtr hwnd,
        int msg,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        const int wmNcHitTest = 0x0084;
        const int htClient = 1;
        if (msg != wmNcHitTest)
        {
            return IntPtr.Zero;
        }

        IntPtr defaultResult = DefWindowProc(hwnd, msg, wParam, lParam);
        if (defaultResult != new IntPtr(htClient))
        {
            return IntPtr.Zero;
        }

        const double edge = 6;
        long packed = lParam.ToInt64();
        var screenPoint = new System.Windows.Point(
            (short)(packed & 0xffff),
            (short)((packed >> 16) & 0xffff));
        System.Windows.Point point = PointFromScreen(screenPoint);
        bool left = point.X <= edge;
        bool right = point.X >= ActualWidth - edge;
        bool top = point.Y <= edge;
        bool bottom = point.Y >= ActualHeight - edge;

        int hit = htClient;
        if (left && top)
            hit = 13;
        else if (right && bottom)
            hit = 17;
        else if (right && top)
            hit = 14;
        else if (left && bottom)
            hit = 16;
        else if (left)
            hit = 10;
        else if (right)
            hit = 11;
        else if (top)
            hit = 12;
        else if (bottom)
            hit = 15;

        if (hit == htClient)
        {
            return IntPtr.Zero;
        }

        handled = true;
        return new IntPtr(hit);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(
        IntPtr window,
        int message,
        IntPtr wParam,
        IntPtr lParam);

    private static void SetDragToggleKnob(
        object sender,
        bool animate)
    {
        if (sender is not CheckBox checkBox ||
            checkBox.Template?.FindName(
                "KnobTranslate",
                checkBox) is not TranslateTransform transform)
        {
            return;
        }

        double target = checkBox.IsChecked == true ? 16 : 0;
        if (!animate)
        {
            transform.BeginAnimation(
                TranslateTransform.XProperty,
                null);
            transform.X = target;
            return;
        }

        transform.BeginAnimation(
            TranslateTransform.XProperty,
            new DoubleAnimation(
                target,
                TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new QuadraticEase
                {
                    EasingMode = EasingMode.EaseOut
                }
            });
    }

    private static IEnumerable<T> FindVisualChildren<T>(
        DependencyObject parent)
        where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int index = 0; index < count; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, index);
            if (child is T typed)
            {
                yield return typed;
            }

            foreach (T descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private T RequireNamedElement<T>(string name)
        where T : FrameworkElement =>
        FindName(name) as T ??
        throw new InvalidOperationException(
            $"MainWindow.xaml did not define named element '{name}'.");

    public void Dispose()
    {
        _searchAnnouncementTimer.Stop();
        _searchAnnouncementTimer.Tick -=
            OnCharacterSearchAnnouncementTick;
        _lifecycle.Dispose();
        GC.SuppressFinalize(this);
    }
}
