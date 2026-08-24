namespace SpinePet.Views;

internal static class PreviewNavigationRules
{
    public const int WheelNotchDelta = 120;
    private const double TieTolerance = 0.001;

    public static int AccumulateWheelSteps(int wheelDelta, ref int residual)
    {
        if (wheelDelta == 0)
        {
            return 0;
        }

        long accumulated = (long)residual + wheelDelta;
        int notches = (int)(accumulated / WheelNotchDelta);
        residual = (int)(accumulated - (long)notches * WheelNotchDelta);
        return -notches;
    }

    public static int ClampIndex(int index, int itemCount) =>
        itemCount <= 0 ? 0 : Math.Clamp(index, 0, itemCount - 1);

    public static double GetSelectionCenteredOffset(
        int index,
        int columnCount,
        double itemHeight,
        double viewportHeight,
        double extentHeight)
    {
        if (index < 0 ||
            columnCount <= 0 ||
            !double.IsFinite(itemHeight) ||
            !double.IsFinite(viewportHeight) ||
            !double.IsFinite(extentHeight) ||
            itemHeight <= 0 ||
            viewportHeight <= 0 ||
            extentHeight <= 0)
        {
            return double.NaN;
        }

        double slotPitch = itemHeight / columnCount;
        double slotCenter = index * slotPitch + slotPitch / 2;
        double maximumOffset = Math.Max(0, extentHeight - viewportHeight);
        return Math.Clamp(
            slotCenter - viewportHeight / 2,
            0,
            maximumOffset);
    }

    public static int? FindScrollSelectionIndex(
        int itemCount,
        double verticalOffset,
        double maximumOffset,
        double viewportHeight,
        double itemHeight,
        int columnCount,
        int? preferredIndex = null,
        double tolerance = 1.0)
    {
        if (itemCount <= 0 ||
            columnCount <= 0 ||
            !double.IsFinite(verticalOffset) ||
            !double.IsFinite(maximumOffset) ||
            !double.IsFinite(viewportHeight) ||
            !double.IsFinite(itemHeight) ||
            maximumOffset < 0 ||
            viewportHeight <= 0 ||
            itemHeight <= 0 ||
            tolerance < 0)
        {
            return null;
        }

        int? preferred = preferredIndex is int preferredValue &&
            preferredValue >= 0 &&
            preferredValue < itemCount
                ? preferredIndex
                : null;

        if (maximumOffset <= tolerance)
        {
            return preferred ?? 0;
        }

        if (verticalOffset <= tolerance)
        {
            return 0;
        }

        if (verticalOffset >= maximumOffset - tolerance)
        {
            return itemCount - 1;
        }

        double slotPitch = itemHeight / columnCount;
        double viewportCenter = verticalOffset + viewportHeight / 2;
        int first = Math.Clamp(
            (int)Math.Floor((viewportCenter - slotPitch / 2) / slotPitch),
            0,
            itemCount - 1);
        int second = Math.Clamp(first + 1, 0, itemCount - 1);
        double firstDistance = Math.Abs(
            first * slotPitch + slotPitch / 2 - viewportCenter);
        double secondDistance = Math.Abs(
            second * slotPitch + slotPitch / 2 - viewportCenter);

        if (Math.Abs(firstDistance - secondDistance) <= TieTolerance)
        {
            if (preferred is int tiePreferred)
            {
                if (tiePreferred == first)
                {
                    return first;
                }

                if (tiePreferred == second)
                {
                    return second;
                }
            }

            return Math.Min(first, second);
        }

        return firstDistance < secondDistance ? first : second;
    }
}

internal sealed class PreviewNavigationCoordinator
{
    private string? _revealTargetId;
    private int _wheelResidual;

    public bool IsApplyingScrollSelection { get; private set; }

    public bool IsRevealing =>
        !string.IsNullOrEmpty(_revealTargetId);

    public string? RevealTargetId => _revealTargetId;

    public int AccumulateWheelSteps(int wheelDelta) =>
        PreviewNavigationRules.AccumulateWheelSteps(
            wheelDelta,
            ref _wheelResidual);

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
