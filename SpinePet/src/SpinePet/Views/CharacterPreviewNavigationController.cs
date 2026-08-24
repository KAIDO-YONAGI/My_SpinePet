using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using SpinePet.ViewModels;
using ICollectionView = System.ComponentModel.ICollectionView;
using ListBox = System.Windows.Controls.ListBox;

namespace SpinePet.Views;

internal sealed class CharacterPreviewNavigationController
{
    private const int PreviewColumnCount = 2;
    private const double PreviewItemHeight = 128;

    private readonly ListBox _cards;
    private readonly MainViewModel _viewModel;
    private readonly ICollectionView _view;
    private readonly Dispatcher _dispatcher;
    private readonly Func<bool> _isConfigMode;
    private readonly Func<bool> _isDisposed;
    private readonly Func<bool> _isLoaded;
    private readonly PreviewNavigationCoordinator _session = new();
    private bool _suppressFollowScroll;

    public CharacterPreviewNavigationController(
        ListBox cards,
        MainViewModel viewModel,
        Dispatcher dispatcher,
        Func<bool> isConfigMode,
        Func<bool> isDisposed,
        Func<bool> isLoaded)
    {
        _cards = cards;
        _viewModel = viewModel;
        _view = viewModel.CharacterView;
        _dispatcher = dispatcher;
        _isConfigMode = isConfigMode;
        _isDisposed = isDisposed;
        _isLoaded = isLoaded;
    }

    public bool IsApplyingScrollSelection =>
        _session.IsApplyingScrollSelection;

    public void ClearReveal()
    {
        _session.ClearReveal();
    }

    public void SelectAndReveal(CharacterViewModel character)
    {
        _cards.SelectedItem = character;
        Reveal(character);
    }

    public void SelectWithoutReveal(CharacterViewModel character)
    {
        _session.ClearReveal();
        _cards.SelectedItem = character;
    }

    public void Reveal(CharacterViewModel character)
    {
        if (_isDisposed() ||
            !_isLoaded() ||
            !_view.Contains(character))
        {
            return;
        }

        _cards.UpdateLayout();
        ScrollViewer? scrollViewer = GetScrollViewer();
        if (scrollViewer != null &&
            IsItemVisible(character, scrollViewer))
        {
            _session.HandleRevealState(character.Id, true);
            return;
        }

        if (string.Equals(
                _session.RevealTargetId,
                character.Id,
                StringComparison.Ordinal))
        {
            return;
        }

        _session.BeginReveal(character.Id);
        _cards.ScrollIntoView(character);
        ScheduleRevealCompletion(character);
    }

    public void HandleScrollChanged(
        ScrollChangedEventArgs e,
        bool isUpdatingSelection)
    {
        if (!_isConfigMode() ||
            isUpdatingSelection ||
            !_cards.HasItems ||
            _session.IsRevealing ||
            _suppressFollowScroll ||
            (e.VerticalChange == 0 && e.HorizontalChange == 0))
        {
            return;
        }

        ScrollViewer? scrollViewer = GetScrollViewer();
        if (scrollViewer == null)
        {
            return;
        }

        try
        {
            double maximumOffset = Math.Max(
                0,
                scrollViewer.ExtentHeight - scrollViewer.ViewportHeight);
            (int columns, double itemHeight) = GetPanelMetrics();
            int? targetIndex = PreviewNavigationRules.FindScrollSelectionIndex(
                _cards.Items.Count,
                scrollViewer.VerticalOffset,
                maximumOffset,
                scrollViewer.ViewportHeight,
                itemHeight,
                columns,
                _cards.SelectedIndex >= 0
                    ? _cards.SelectedIndex
                    : null);
            if (targetIndex is int index &&
                _cards.Items[index] is CharacterViewModel target &&
                !ReferenceEquals(target, _cards.SelectedItem))
            {
                _session.ApplyScrollSelection(
                    () => _cards.SelectedItem = target);
            }
        }
        catch (Exception exception)
        {
            Infrastructure.AppLogger.Write(
                nameof(CharacterPreviewNavigationController),
                $"scroll-follow-failed message={exception.Message}");
        }
    }

    public bool HandlePreviewMouseWheel(
        int wheelDelta,
        bool isUpdatingSelection)
    {
        if (!_isConfigMode() ||
            isUpdatingSelection ||
            !_cards.HasItems ||
            wheelDelta == 0)
        {
            return false;
        }

        ScrollViewer? scrollViewer = GetScrollViewer();
        if (scrollViewer == null)
        {
            return false;
        }

        if (_session.IsRevealing)
        {
            _session.ClearReveal();
        }

        int steps = _session.AccumulateWheelSteps(wheelDelta);
        int targetIndex = PreviewNavigationRules.ClampIndex(
            Math.Max(0, _cards.SelectedIndex) + steps,
            _cards.Items.Count);
        if (_cards.Items[targetIndex] is CharacterViewModel target &&
            !ReferenceEquals(target, _cards.SelectedItem))
        {
            _session.ApplyScrollSelection(
                () => _cards.SelectedItem = target);
        }

        (int columns, double itemHeight) = GetPanelMetrics();
        double centeredOffset =
            PreviewNavigationRules.GetSelectionCenteredOffset(
                targetIndex,
                columns,
                itemHeight,
                scrollViewer.ViewportHeight,
                scrollViewer.ExtentHeight);
        ScrollToCenteredOffset(scrollViewer, centeredOffset);
        return true;
    }

    private void ScheduleRevealCompletion(CharacterViewModel character)
    {
        _dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(() => CompleteReveal(character)));
    }

    private void CompleteReveal(CharacterViewModel character)
    {
        if (!string.Equals(
                _session.RevealTargetId,
                character.Id,
                StringComparison.Ordinal))
        {
            return;
        }

        if (_isDisposed() ||
            !ReferenceEquals(_viewModel.SelectedCharacter, character))
        {
            _session.ClearReveal();
            return;
        }

        _cards.UpdateLayout();
        ScrollViewer? scrollViewer = GetScrollViewer();
        int index = _cards.Items.IndexOf(character);
        if (scrollViewer == null || index < 0)
        {
            _session.ClearReveal();
            return;
        }

        (int columns, double itemHeight) = GetPanelMetrics();
        double centeredOffset =
            PreviewNavigationRules.GetSelectionCenteredOffset(
                index,
                columns,
                itemHeight,
                scrollViewer.ViewportHeight,
                scrollViewer.ExtentHeight);
        if (double.IsFinite(centeredOffset) &&
            Math.Abs(centeredOffset - scrollViewer.VerticalOffset) > 0.001)
        {
            ScrollToCenteredOffset(scrollViewer, centeredOffset);
            ScheduleRevealCompletion(character);
            return;
        }

        _session.HandleRevealState(
            character.Id,
            IsItemVisible(character, scrollViewer));
    }

    private void ScrollToCenteredOffset(
        ScrollViewer scrollViewer,
        double offset)
    {
        if (!double.IsFinite(offset))
        {
            return;
        }

        _suppressFollowScroll = true;
        try
        {
            scrollViewer.ScrollToVerticalOffset(offset);
        }
        finally
        {
            _dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(() => _suppressFollowScroll = false));
        }
    }

    private bool IsItemVisible(
        CharacterViewModel character,
        ScrollViewer scrollViewer)
    {
        if (_cards.ItemContainerGenerator.ContainerFromItem(character)
                is not ListBoxItem item ||
            item.ActualHeight <= 0 ||
            scrollViewer.ViewportHeight <= 0)
        {
            return false;
        }

        System.Windows.Point location = item.TranslatePoint(
            new System.Windows.Point(0, 0),
            scrollViewer);
        return location.Y < scrollViewer.ViewportHeight &&
            location.Y + item.ActualHeight > 0;
    }

    private (int Columns, double ItemHeight) GetPanelMetrics()
    {
        VirtualizingUniformGrid? panel =
            FindVisualChildren<VirtualizingUniformGrid>(_cards)
                .FirstOrDefault();
        if (panel != null &&
            panel.Columns > 0 &&
            panel.ItemHeight > 0)
        {
            return (panel.Columns, panel.ItemHeight);
        }

        return (PreviewColumnCount, PreviewItemHeight);
    }

    private ScrollViewer? GetScrollViewer() =>
        FindVisualChildren<ScrollViewer>(_cards).FirstOrDefault();

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
}
