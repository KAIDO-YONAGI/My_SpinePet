namespace SpinePet.Views;

internal readonly record struct PreviewItemGeometry(
    int Index,
    double Top,
    double Bottom);

internal static class PreviewNavigationRules
{
    public static int? FindCenterItemIndex(
        IEnumerable<PreviewItemGeometry> items,
        double viewportHeight,
        int? preferredIndex = null,
        int columnCount = 2)
    {
        if (viewportHeight <= 0 || columnCount <= 0)
        {
            return null;
        }

        double viewportCenter = viewportHeight / 2;
        List<(PreviewItemGeometry Item, double Distance)> visibleItems = items
            .Where(item =>
                item.Bottom > 0 &&
                item.Top < viewportHeight &&
                item.Bottom >= item.Top)
            .Select(item =>
                (
                    Item: item,
                    Distance: Math.Abs(
                        GetTraversalCenter(item, columnCount) -
                        viewportCenter)
                ))
            .OrderBy(candidate => candidate.Item.Index)
            .ToList();
        if (visibleItems.Count == 0)
        {
            return null;
        }

        double closestDistance = visibleItems.Min(
            candidate => candidate.Distance);
        const double tieTolerance = 0.001;
        if (preferredIndex is int preferred &&
            visibleItems.Any(candidate =>
                candidate.Item.Index == preferred &&
                Math.Abs(candidate.Distance - closestDistance) <=
                tieTolerance))
        {
            return preferred;
        }

        return visibleItems
            .Where(candidate =>
                Math.Abs(candidate.Distance - closestDistance) <=
                tieTolerance)
            .Select(candidate => (int?)candidate.Item.Index)
            .FirstOrDefault();
    }

    private static double GetTraversalCenter(
        PreviewItemGeometry item,
        int columnCount)
    {
        double itemHeight = item.Bottom - item.Top;
        double columnCenter = (columnCount - 1) / 2.0;
        double columnBias =
            (item.Index % columnCount - columnCenter) *
            itemHeight /
            columnCount;
        return (item.Top + item.Bottom) / 2 + columnBias;
    }

    public static double GetCenteredVerticalOffset(
        double currentOffset,
        double itemTop,
        double itemHeight,
        double viewportHeight,
        double extentHeight)
    {
        if (!double.IsFinite(currentOffset) ||
            !double.IsFinite(itemTop) ||
            !double.IsFinite(itemHeight) ||
            !double.IsFinite(viewportHeight) ||
            !double.IsFinite(extentHeight) ||
            itemHeight <= 0 ||
            viewportHeight <= 0 ||
            extentHeight <= 0)
        {
            return currentOffset;
        }

        double maximumOffset = Math.Max(0, extentHeight - viewportHeight);
        double centeredOffset = currentOffset +
            itemTop +
            itemHeight / 2 -
            viewportHeight / 2;
        return Math.Clamp(centeredOffset, 0, maximumOffset);
    }
}

internal sealed class PreviewNavigationCoordinator
{
    private string? _revealTargetId;

    public bool IsApplyingScrollSelection { get; private set; }

    public bool IsRevealing =>
        !string.IsNullOrEmpty(_revealTargetId);

    public string? RevealTargetId => _revealTargetId;

    public static int? FindCenterItemIndex(
        IEnumerable<PreviewItemGeometry> items,
        double viewportHeight)
    {
        if (viewportHeight <= 0)
        {
            return null;
        }

        double viewportCenter = viewportHeight / 2;
        return items
            .Where(item =>
                item.Bottom > 0 &&
                item.Top < viewportHeight &&
                item.Bottom >= item.Top)
            .OrderBy(item =>
                Math.Abs(((item.Top + item.Bottom) / 2) - viewportCenter))
            .ThenBy(item => item.Index)
            .Select(item => (int?)item.Index)
            .FirstOrDefault();
    }

    public void BeginReveal(string characterId)
    {
        if (!string.IsNullOrWhiteSpace(characterId))
        {
            _revealTargetId = characterId;
        }
    }

    public bool HandleRevealState(
        string characterId,
        bool targetIsVisible)
    {
        if (!string.Equals(
                _revealTargetId,
                characterId,
                StringComparison.Ordinal))
        {
            return false;
        }

        if (targetIsVisible)
        {
            _revealTargetId = null;
        }

        return true;
    }

    public void ClearReveal()
    {
        _revealTargetId = null;
    }

    public void ApplyScrollSelection(Action selection)
    {
        IsApplyingScrollSelection = true;
        try
        {
            selection();
        }
        finally
        {
            IsApplyingScrollSelection = false;
        }
    }
}
