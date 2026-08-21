using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using SpinePet.ViewModels;
using ICollectionView = System.ComponentModel.ICollectionView;
using ListBox = System.Windows.Controls.ListBox;

namespace SpinePet.Views;

internal sealed class CharacterPreviewNavigationController
{
    private const int PreviewColumnCount = 2;

    private readonly ListBox _cards;
    private readonly ICollectionView _view;
    private readonly Dispatcher _dispatcher;
    private readonly Func<bool> _isConfigMode;
    private readonly Func<bool> _isDisposed;
    private readonly Func<bool> _isLoaded;
    private readonly Func<CharacterViewModel?> _getSelectedCharacter;
    private readonly PreviewNavigationCoordinator _session = new();

    public CharacterPreviewNavigationController(
        ListBox cards,
        ICollectionView view,
        Dispatcher dispatcher,
        Func<bool> isConfigMode,
        Func<bool> isDisposed,
        Func<bool> isLoaded,
        Func<CharacterViewModel?> getSelectedCharacter)
    {
        _cards = cards;
        _view = view;
        _dispatcher = dispatcher;
        _isConfigMode = isConfigMode;
        _isDisposed = isDisposed;
        _isLoaded = isLoaded;
        _getSelectedCharacter = getSelectedCharacter;
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
            int? targetIndex = PreviewNavigationRules.FindScrollSelectionIndex(
                _cards.Items.Count,
                scrollViewer.VerticalOffset,
                maximumOffset,
                GetItemGeometries(scrollViewer),
                scrollViewer.ViewportHeight,
                _cards.SelectedIndex >= 0
                    ? _cards.SelectedIndex
                    : null,
                PreviewColumnCount);
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
            _session.IsRevealing ||
            wheelDelta == 0)
        {
            return false;
        }

        ScrollViewer? scrollViewer = GetScrollViewer();
        if (scrollViewer == null)
        {
            return false;
        }

        double maximumOffset = Math.Max(
            0,
            scrollViewer.ExtentHeight - scrollViewer.ViewportHeight);
        int? targetIndex =
            PreviewNavigationRules.FindBoundaryWheelSelectionIndex(
                _cards.Items.Count,
                _cards.SelectedIndex,
                wheelDelta,
                scrollViewer.VerticalOffset,
                maximumOffset);
        if (targetIndex is not int index ||
            _cards.Items[index] is not CharacterViewModel target ||
            ReferenceEquals(target, _cards.SelectedItem))
        {
            return false;
        }

        _session.ApplyScrollSelection(
            () => _cards.SelectedItem = target);
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
            !ReferenceEquals(_getSelectedCharacter(), character))
        {
            _session.ClearReveal();
            return;
        }

        _cards.UpdateLayout();
        ScrollViewer? scrollViewer = GetScrollViewer();
        if (scrollViewer == null ||
            _cards.ItemContainerGenerator.ContainerFromItem(character)
                is not ListBoxItem item)
        {
            _session.ClearReveal();
            return;
        }

        System.Windows.Point location = item.TranslatePoint(
            new System.Windows.Point(0, 0),
            scrollViewer);
        double centeredOffset =
            PreviewNavigationRules.GetCenteredVerticalOffset(
                scrollViewer.VerticalOffset,
                location.Y,
                item.ActualHeight,
                scrollViewer.ViewportHeight,
                scrollViewer.ExtentHeight);
        if (Math.Abs(centeredOffset - scrollViewer.VerticalOffset) > 0.001)
        {
            scrollViewer.ScrollToVerticalOffset(centeredOffset);
            ScheduleRevealCompletion(character);
            return;
        }

        _session.HandleRevealState(
            character.Id,
            IsItemVisible(character, scrollViewer));
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

    private List<PreviewItemGeometry> GetItemGeometries(
        ScrollViewer scrollViewer)
    {
        List<PreviewItemGeometry> geometries = new();
        for (int index = 0; index < _cards.Items.Count; index++)
        {
            if (_cards.ItemContainerGenerator.ContainerFromIndex(index)
                is not ListBoxItem item ||
                item.ActualHeight <= 0)
            {
                continue;
            }

            System.Windows.Point location = item.TranslatePoint(
                new System.Windows.Point(0, 0),
                scrollViewer);
            geometries.Add(new PreviewItemGeometry(
                index,
                location.Y,
                location.Y + item.ActualHeight));
        }

        return geometries;
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
