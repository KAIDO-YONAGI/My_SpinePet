using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
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
    private const double MinimumScaleBasePercent = 0;
    private const double MaximumScaleBasePercent = 100;
    private const double MinimumScaleMultiplier = 1;
    private const double MaximumScaleMultiplier = 5;
    // 基础滑条 0–100 只映射 0–20% 的实际缩放。
    private const double MaximumBaseScale = 0.2;

    private readonly CharacterManager _characterManager;
    private readonly CharacterResourceDiscoveryService _resourceDiscovery;
    private readonly UnityBundleImportService _bundleImporter;
    private readonly CharacterIconDownloadService _characterIconDownloader;
    private readonly CharacterResourceStorageService _resourceStorage = new();
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly DispatcherTimer _searchAnnouncementTimer;
    private Dictionary<string, CharacterResourceFiles> _knownResources =
        new(StringComparer.OrdinalIgnoreCase);
    private bool _isRefreshingSelection;
    private bool _isUpdatingCharacterSelection;
    private bool _isDeletingSkin;
    private bool _isConfigMode = true;
    private CharacterViewModel? _selectedCharacter;
    private string _selectedAnimation = string.Empty;
    private double _selectedScale = DefaultScale;
    private double _selectedScaleMax = DefaultMaxScale;
    private double _selectedScaleBasePercent = MaximumScaleBasePercent;
    private double _selectedScaleMultiplier = 1;
    private double _selectedSpeed = 100;
    private bool _allowRenderDrag;
    private int _targetFrameRate = GlobalConfig.DefaultTargetFrameRate;
    private int _thumbnailScalePercent = 100;
    private int _matchingCharacterCount;
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
        _thumbnailScalePercent =
            characterManager.LibraryThumbnailScalePercent;
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
        _characterManager.CharacterRightClicked += OnCharacterRightClicked;
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

    public double SelectedScaleBasePercent
    {
        get => _selectedScaleBasePercent;
        set
        {
            double normalized = Math.Clamp(
                value,
                MinimumScaleBasePercent,
                MaximumScaleBasePercent);
            if (NearlyEquals(_selectedScaleBasePercent, normalized))
            {
                return;
            }

            _selectedScaleBasePercent = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedScaleBasePercentDisplay));
            CommitSelectedScale();
        }
    }

    public double SelectedScaleMultiplier
    {
        get => _selectedScaleMultiplier;
        set
        {
            double normalized = Math.Clamp(
                value,
                MinimumScaleMultiplier,
                MaximumScaleMultiplier);
            if (NearlyEquals(_selectedScaleMultiplier, normalized))
            {
                return;
            }

            _selectedScaleMultiplier = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedScaleMultiplierDisplay));
            CommitSelectedScale();
        }
    }

    public string SelectedScaleBasePercentDisplay =>
        $"{SelectedScaleBasePercent:F0}%";

    public string SelectedScaleMultiplierDisplay =>
        $"×{SelectedScaleMultiplier:F1}";

    // 双滑条模型：实际缩放 = 基础百分比(10–20%) × 乘数(1–5)。
    // 外部给定的 scale 值拆解回这两个分量。
    private void SetSelectedScaleFromValue(double scale)
    {
        double clamped = Math.Clamp(
            scale,
            MinimumScale,
            MaximumBaseScale * MaximumScaleMultiplier);
        double basePercent;
        double multiplier;
        if (clamped <= MaximumBaseScale)
        {
            basePercent = Math.Clamp(
                (clamped / MaximumBaseScale) * 100,
                MinimumScaleBasePercent,
                MaximumScaleBasePercent);
            multiplier = 1;
        }
        else
        {
            basePercent = MaximumScaleBasePercent;
            multiplier = Math.Clamp(
                clamped / MaximumBaseScale,
                MinimumScaleMultiplier,
                MaximumScaleMultiplier);
        }

        _selectedScaleBasePercent = basePercent;
        _selectedScaleMultiplier = multiplier;
        _selectedScale = (basePercent / 100.0) * MaximumBaseScale * multiplier;
        OnPropertyChanged(nameof(SelectedScaleBasePercent));
        OnPropertyChanged(nameof(SelectedScaleMultiplier));
        OnPropertyChanged(nameof(SelectedScaleBasePercentDisplay));
        OnPropertyChanged(nameof(SelectedScaleMultiplierDisplay));
        OnPropertyChanged(nameof(SelectedScale));
    }

    private void CommitSelectedScale()
    {
        if (_isRefreshingSelection || SelectedCharacter == null)
        {
            return;
        }

        CharacterConfig? character = FindSelectedCharacterConfig();
        if (character == null)
        {
            return;
        }

        double effectiveMaximumScale =
            _characterManager.RenderHost.IsCharacterVisible(character.Id)
                ? _characterManager.RenderHost.GetMaxScale(character.Id)
                : SelectedScaleMax;
        SelectedScaleMax = effectiveMaximumScale;

        double scale = Math.Clamp(
            (SelectedScaleBasePercent / 100.0) *
                MaximumBaseScale *
                SelectedScaleMultiplier,
            MinimumScale,
            effectiveMaximumScale);
        SelectedScale = scale;
        character.Scale = scale;
        SelectedCharacter.Scale = scale;
        _characterManager.RenderHost.SetCharacterScale(
            character.Id,
            scale);
    }

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
            ApplyThumbnailScale();
            _characterManager.SetLibraryThumbnailScale(normalized);
        }
    }

    public string ThumbnailScaleDisplay => $"{ThumbnailScalePercent}%";

    // 只缩放左侧预览列表：对列表整体挂 LayoutTransform，
    // 条目内图片框/skin 标签/边框/间距全部严格等比，不会互相撑大。
    private void ApplyThumbnailScale()
    {
        double scale = _thumbnailScalePercent / 100.0;
        CharacterCards.LayoutTransform = scale == 1.0
            ? Transform.Identity
            : new ScaleTransform(scale, scale);

        // 列表整体被 LayoutTransform 缩放，滚动条也被一起缩了；
        // 反方向放大滚动条宽度，使视觉宽度恒定（约 10px）。
        double scrollBarWidth = 10.0 / scale;
        foreach (System.Windows.Controls.Primitives.ScrollBar scrollBar
                 in FindVisualChildren<System.Windows.Controls.Primitives.ScrollBar>(
                     CharacterCards))
        {
            scrollBar.Width = scrollBarWidth;
            scrollBar.MinWidth = scrollBarWidth;
        }
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
        double width = _characterManager.ConfigPanelWidth > 0
            ? _characterManager.ConfigPanelWidth
            : 820;
        double height = _characterManager.ConfigPanelHeight > 0
            ? _characterManager.ConfigPanelHeight
            : workArea.Height * 0.6;
        Width = Math.Clamp(width, MinWidth, workArea.Width);
        Height = Math.Clamp(height, MinHeight, workArea.Height);
        Left = workArea.Right - Width;
        Top = workArea.Top;
        ApplyThumbnailScale();
        ApplyConfigMode();
    }

    // Win 风格无边框缩放：WM_NCHITTEST 把边缘/四角映射为系统
    // HTLEFT/HTRIGHT/... 命中，由系统接管拖拽与双向光标。
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
            hit = 13;          // HTTOPLEFT
        else if (right && bottom)
            hit = 17;          // HTBOTTOMRIGHT
        else if (right && top)
            hit = 14;          // HTTOPRIGHT
        else if (left && bottom)
            hit = 16;          // HTBOTTOMLEFT
        else if (left)
            hit = 10;          // HTLEFT
        else if (right)
            hit = 11;          // HTRIGHT
        else if (top)
            hit = 12;          // HTTOP
        else if (bottom)
            hit = 15;          // HTBOTTOM

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
            Topmost = true;
            Show();
            Activate();
        }
        else
        {
            Hide();
        }

        _characterManager.SetConfigMode(_isConfigMode);
    }

    private void OnDragToggleLoaded(object sender, RoutedEventArgs e)
    {
        SetDragToggleKnob(sender, animate: false);
    }

    private void OnDragToggleStateChanged(
        object sender,
        RoutedEventArgs e)
    {
        SetDragToggleKnob(sender, animate: true);
    }

    private static void SetDragToggleKnob(object sender, bool animate)
    {
        if (sender is not System.Windows.Controls.CheckBox checkBox ||
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

    private void OnCharacterRightClicked(string characterId)
    {
        if (IsDisposed)
        {
            return;
        }

        // 面板已打开时再次右键角色 → 关闭面板（保存状态后隐藏）。
        if (_isConfigMode)
        {
            _characterManager.SaveAllState();
            _isConfigMode = false;
            ApplyConfigMode();
            return;
        }

        CharacterViewModel? viewModel = Characters.FirstOrDefault(
            item => item.Id == characterId);
        if (viewModel != null)
        {
            if (!CharacterView.Contains(viewModel))
            {
                CharacterSearchText = string.Empty;
            }

            SelectedCharacter = viewModel;
        }

        SwitchToConfigMode();
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
            SetSelectedScaleFromValue(DefaultScale);
            SelectedSpeed = 100;
            _isRefreshingSelection = false;
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
        SetSelectedScaleFromValue(
            SelectedCharacter.Scale > 0
                ? SelectedCharacter.Scale
                : DefaultScale);
        SelectedAnimation =
            !string.IsNullOrEmpty(SelectedCharacter.ConfiguredAnimation)
                ? SelectedCharacter.ConfiguredAnimation
                : SelectedAnimationNames.FirstOrDefault() ?? string.Empty;
        SelectedSpeed = Math.Clamp(
            SelectedCharacter.AnimationSpeed * 100.0,
            10,
            200);

        _isRefreshingSelection = false;
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
        SavePanelLayout();
        _characterManager.SaveAllState();
        _isConfigMode = false;
        ApplyConfigMode();
    }

    private void SavePanelLayout()
    {
        _characterManager.SetConfigPanelSize(
            ActualWidth,
            ActualHeight);
    }

    protected override void OnClosed(EventArgs e)
    {
        Dispose();
        _characterManager.CharactersChanged -= RefreshCharacterList;
        _characterManager.CharacterScaleChanged -= OnCharacterScaleChanged;
        _characterManager.CharacterRightClicked -= OnCharacterRightClicked;

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
