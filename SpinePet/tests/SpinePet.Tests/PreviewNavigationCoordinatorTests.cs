using SpinePet.Views;

namespace SpinePet.Tests;

public sealed class PreviewNavigationCoordinatorTests
{
    [Theory]
    [InlineData(120, -1)]
    [InlineData(-120, 1)]
    [InlineData(240, -2)]
    [InlineData(-360, 3)]
    [InlineData(60, 0)]
    [InlineData(-60, 0)]
    public void WheelStepsMoveOneItemPerNotch(int wheelDelta, int expectedSteps)
    {
        int residual = 0;

        int steps = PreviewNavigationRules.AccumulateWheelSteps(
            wheelDelta,
            ref residual);

        Assert.Equal(expectedSteps, steps);
    }

    [Fact]
    public void WheelStepsAccumulateSubNotchTrackPadDeltas()
    {
        int residual = 0;

        Assert.Equal(
            0,
            PreviewNavigationRules.AccumulateWheelSteps(40, ref residual));
        Assert.Equal(
            0,
            PreviewNavigationRules.AccumulateWheelSteps(40, ref residual));
        Assert.Equal(
            -1,
            PreviewNavigationRules.AccumulateWheelSteps(40, ref residual));
        Assert.Equal(
            0,
            residual);
    }

    [Fact]
    public void WheelStepsKeepResidualAcrossNotches()
    {
        int residual = 0;

        PreviewNavigationRules.AccumulateWheelSteps(150, ref residual);
        int secondSteps = PreviewNavigationRules.AccumulateWheelSteps(
            150,
            ref residual);

        Assert.Equal(-1, secondSteps);
        Assert.Equal(60, residual);
    }

    [Theory]
    [InlineData(-5, 8, 0)]
    [InlineData(0, 8, 0)]
    [InlineData(3, 8, 3)]
    [InlineData(9, 8, 7)]
    [InlineData(int.MaxValue, 8, 7)]
    public void ClampIndexStaysInsideItemRange(
        int index,
        int itemCount,
        int expectedIndex)
    {
        Assert.Equal(
            expectedIndex,
            PreviewNavigationRules.ClampIndex(index, itemCount));
    }

    [Fact]
    public void CenteredOffsetPutsTheSelectedSlotOnTheViewportMidline()
    {
        double offset = PreviewNavigationRules.GetSelectionCenteredOffset(
            index: 9,
            columnCount: 2,
            itemHeight: 128,
            viewportHeight: 600,
            extentHeight: 2560);

        Assert.Equal(9 * 64 + 32 - 300, offset);
    }

    [Fact]
    public void CenteredOffsetAdvancesHalfARowPerItem()
    {
        const double itemHeight = 128;
        const double viewportHeight = 600;
        const double extentHeight = 2560;
        double left = PreviewNavigationRules.GetSelectionCenteredOffset(
            8, 2, itemHeight, viewportHeight, extentHeight);
        double right = PreviewNavigationRules.GetSelectionCenteredOffset(
            9, 2, itemHeight, viewportHeight, extentHeight);

        Assert.Equal(itemHeight / 2, right - left);
    }

    [Fact]
    public void CenteredOffsetClampsAtTheHeadAndTail()
    {
        Assert.Equal(
            0,
            PreviewNavigationRules.GetSelectionCenteredOffset(
                index: 0,
                columnCount: 2,
                itemHeight: 128,
                viewportHeight: 600,
                extentHeight: 2560));
        Assert.Equal(
            2560 - 600,
            PreviewNavigationRules.GetSelectionCenteredOffset(
                index: 39,
                columnCount: 2,
                itemHeight: 128,
                viewportHeight: 600,
                extentHeight: 2560));
    }

    [Fact]
    public void CenteredOffsetReturnsNaNForInvalidInputs()
    {
        Assert.True(double.IsNaN(
            PreviewNavigationRules.GetSelectionCenteredOffset(
                -1, 2, 128, 600, 2560)));
        Assert.True(double.IsNaN(
            PreviewNavigationRules.GetSelectionCenteredOffset(
                4, 0, 128, 600, 2560)));
        Assert.True(double.IsNaN(
            PreviewNavigationRules.GetSelectionCenteredOffset(
                4, 2, 0, 600, 2560)));
        Assert.True(double.IsNaN(
            PreviewNavigationRules.GetSelectionCenteredOffset(
                4, 2, 128, 0, 2560)));
    }

    [Fact]
    public void ScrollFollowAgreesWithEveryUnclampedCenteredOffset()
    {
        const int itemCount = 40;
        const double itemHeight = 128;
        const double viewportHeight = 600;
        const double extentHeight = 2560;

        for (int index = 5; index <= 34; index++)
        {
            double offset = PreviewNavigationRules.GetSelectionCenteredOffset(
                index, 2, itemHeight, viewportHeight, extentHeight);
            int? followed = PreviewNavigationRules.FindScrollSelectionIndex(
                itemCount,
                offset,
                extentHeight - viewportHeight,
                viewportHeight,
                itemHeight,
                columnCount: 2);
            Assert.Equal(index, followed);
        }
    }

    [Fact]
    public void ScrollFollowTraversesItemsInRowMajorReadingOrder()
    {
        // viewport 160 with item height 128: the midline sweeps
        // 64px per item, alternating left/right columns.
        int? first = PreviewNavigationRules.FindScrollSelectionIndex(
            itemCount: 40,
            verticalOffset: 64 + 32 - 80,
            maximumOffset: 1960,
            viewportHeight: 160,
            itemHeight: 128,
            columnCount: 2);
        int? second = PreviewNavigationRules.FindScrollSelectionIndex(
            itemCount: 40,
            verticalOffset: 2 * 64 + 32 - 80,
            maximumOffset: 1960,
            viewportHeight: 160,
            itemHeight: 128,
            columnCount: 2);

        Assert.Equal(1, first);
        Assert.Equal(2, second);
    }

    [Fact]
    public void ScrollFollowSelectsTheFirstItemAtTheTopBoundary()
    {
        Assert.Equal(
            0,
            PreviewNavigationRules.FindScrollSelectionIndex(
                itemCount: 40,
                verticalOffset: 0,
                maximumOffset: 1960,
                viewportHeight: 600,
                itemHeight: 128,
                columnCount: 2));
    }

    [Fact]
    public void ScrollFollowSelectsTheLastItemAtTheBottomBoundary()
    {
        Assert.Equal(
            39,
            PreviewNavigationRules.FindScrollSelectionIndex(
                itemCount: 40,
                verticalOffset: 1959.5,
                maximumOffset: 1960,
                viewportHeight: 600,
                itemHeight: 128,
                columnCount: 2));
    }

    [Fact]
    public void ScrollFollowKeepsPreferredWhenContentDoesNotScroll()
    {
        Assert.Equal(
            3,
            PreviewNavigationRules.FindScrollSelectionIndex(
                itemCount: 8,
                verticalOffset: 0,
                maximumOffset: 0,
                viewportHeight: 1024,
                itemHeight: 128,
                columnCount: 2,
                preferredIndex: 3));
    }

    [Fact]
    public void ScrollFollowKeepsPreferredOnAnExactMidlineTie()
    {
        // Midline exactly between slot 4 and slot 5 centers.
        const double viewportHeight = 600;
        double midline = 5 * 64;
        double offset = midline - viewportHeight / 2;

        Assert.Equal(
            5,
            PreviewNavigationRules.FindScrollSelectionIndex(
                itemCount: 40,
                verticalOffset: offset,
                maximumOffset: 1960,
                viewportHeight: viewportHeight,
                itemHeight: 128,
                columnCount: 2,
                preferredIndex: 5));
        Assert.Equal(
            4,
            PreviewNavigationRules.FindScrollSelectionIndex(
                itemCount: 40,
                verticalOffset: offset,
                maximumOffset: 1960,
                viewportHeight: viewportHeight,
                itemHeight: 128,
                columnCount: 2,
                preferredIndex: 4));
        Assert.Equal(
            4,
            PreviewNavigationRules.FindScrollSelectionIndex(
                itemCount: 40,
                verticalOffset: offset,
                maximumOffset: 1960,
                viewportHeight: viewportHeight,
                itemHeight: 128,
                columnCount: 2));
    }

    [Fact]
    public void ScrollFollowReturnsNullForInvalidInputs()
    {
        Assert.Null(PreviewNavigationRules.FindScrollSelectionIndex(
            0, 0, 1960, 600, 128, 2));
        Assert.Null(PreviewNavigationRules.FindScrollSelectionIndex(
            40, 0, -1, 600, 128, 2));
        Assert.Null(PreviewNavigationRules.FindScrollSelectionIndex(
            40, double.NaN, 1960, 600, 128, 2));
    }

    [Fact]
    public void RevealStateIsIdempotentUntilTheTargetIsVisible()
    {
        PreviewNavigationCoordinator coordinator = new();
        coordinator.BeginReveal("character-1");
        coordinator.BeginReveal("character-1");

        Assert.True(coordinator.IsRevealing);
        Assert.True(coordinator.HandleRevealState("character-1", false));
        Assert.True(coordinator.IsRevealing);
        Assert.True(coordinator.HandleRevealState("character-1", true));
        Assert.False(coordinator.IsRevealing);
    }

    [Fact]
    public void ClearRevealCancelsAnActiveReveal()
    {
        PreviewNavigationCoordinator coordinator = new();
        coordinator.BeginReveal("character-1");

        coordinator.ClearReveal();

        Assert.False(coordinator.IsRevealing);
    }

    [Fact]
    public void SessionAccumulatesWheelStepsAcrossCalls()
    {
        PreviewNavigationCoordinator coordinator = new();

        Assert.Equal(0, coordinator.AccumulateWheelSteps(90));
        Assert.Equal(-1, coordinator.AccumulateWheelSteps(90));
        Assert.Equal(0, coordinator.AccumulateWheelSteps(-120));
        Assert.Equal(1, coordinator.AccumulateWheelSteps(-120));
    }

    [Fact]
    public void ApplyScrollSelectionFlagsOnlyDuringTheSelection()
    {
        PreviewNavigationCoordinator coordinator = new();
        bool observed = false;

        coordinator.ApplyScrollSelection(
            () => observed = coordinator.IsApplyingScrollSelection);

        Assert.True(observed);
        Assert.False(coordinator.IsApplyingScrollSelection);
    }
}
