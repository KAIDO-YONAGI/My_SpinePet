using SpinePet.Views;

namespace SpinePet.Tests;

public sealed class PreviewNavigationCoordinatorTests
{
    [Theory]
    [InlineData(4, 3)]
    [InlineData(1, 0)]
    public void BoundaryWheelStepsOneItemTowardTheTop(
        int selectedIndex,
        int expectedIndex)
    {
        Assert.Equal(
            expectedIndex,
            PreviewNavigationRules.FindBoundaryWheelSelectionIndex(
                itemCount: 8,
                selectedIndex,
                wheelDelta: 120,
                verticalOffset: 0,
                maximumOffset: 640));
    }

    [Theory]
    [InlineData(3, 4)]
    [InlineData(6, 7)]
    public void BoundaryWheelStepsOneItemTowardTheBottom(
        int selectedIndex,
        int expectedIndex)
    {
        Assert.Equal(
            expectedIndex,
            PreviewNavigationRules.FindBoundaryWheelSelectionIndex(
                itemCount: 8,
                selectedIndex,
                wheelDelta: -120,
                verticalOffset: 640,
                maximumOffset: 640));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(7, 6)]
    public void BoundaryWheelStepsOneItemBackIntoContent(
        int selectedIndex,
        int expectedIndex)
    {
        Assert.Equal(
            expectedIndex,
            PreviewNavigationRules.FindBoundaryWheelSelectionIndex(
                itemCount: 8,
                selectedIndex,
                wheelDelta: selectedIndex == 0 ? -120 : 120,
                verticalOffset: selectedIndex == 0 ? 0 : 640,
                maximumOffset: 640));
    }

    [Theory]
    [InlineData(0, 120, 0, 640)]
    [InlineData(7, -120, 640, 640)]
    [InlineData(3, 120, 320, 640)]
    [InlineData(3, -120, 320, 640)]
    public void BoundaryWheelDoesNotJumpOrReverseDirection(
        int selectedIndex,
        int wheelDelta,
        double verticalOffset,
        double maximumOffset)
    {
        Assert.Null(
            PreviewNavigationRules.FindBoundaryWheelSelectionIndex(
                itemCount: 8,
                selectedIndex,
                wheelDelta,
                verticalOffset,
                maximumOffset));
    }

    [Fact]
    public void ScrollSelectionUsesTheVisibleCenterAtTheTopBoundary()
    {
        Assert.Equal(
            1,
            PreviewNavigationRules.FindScrollSelectionIndex(
                itemCount: 8,
                verticalOffset: 0,
                maximumOffset: 640,
                visibleItems:
                [
                    new(0, 0, 80),
                    new(1, 0, 80),
                    new(2, 80, 160),
                    new(3, 80, 160)
                ],
                viewportHeight: 160));
    }

    [Fact]
    public void ScrollSelectionUsesTheVisibleCenterAtTheBottomBoundary()
    {
        Assert.Equal(
            5,
            PreviewNavigationRules.FindScrollSelectionIndex(
                itemCount: 8,
                verticalOffset: 640,
                maximumOffset: 640,
                visibleItems:
                [
                    new(4, 0, 80),
                    new(5, 0, 80),
                    new(6, 80, 160),
                    new(7, 80, 160)
                ],
                viewportHeight: 160));
    }

    [Fact]
    public void BoundaryRuleSelectsFirstItemAtTop()
    {
        Assert.Equal(
            0,
            PreviewNavigationRules.FindBoundaryItemIndex(
                itemCount: 8,
                verticalOffset: 0,
                maximumOffset: 640));
    }

    [Fact]
    public void BoundaryRuleSelectsLastItemAtBottom()
    {
        Assert.Equal(
            7,
            PreviewNavigationRules.FindBoundaryItemIndex(
                itemCount: 8,
                verticalOffset: 639.5,
                maximumOffset: 640));
    }

    [Fact]
    public void BoundaryRuleKeepsPreferredItemWhenContentDoesNotScroll()
    {
        Assert.Equal(
            3,
            PreviewNavigationRules.FindBoundaryItemIndex(
                itemCount: 8,
                verticalOffset: 0,
                maximumOffset: 0,
                preferredIndex: 3));
    }

    [Fact]
    public void BoundaryRuleReturnsNoSelectionAwayFromEdges()
    {
        Assert.Null(
            PreviewNavigationRules.FindBoundaryItemIndex(
                itemCount: 8,
                verticalOffset: 120,
                maximumOffset: 640));
    }

    [Fact]
    public void FindCenterItemConsidersEveryVisibleRowMajorItem()
    {
        PreviewItemGeometry[] items =
        [
            new(0, 0, 30),
            new(1, 0, 70),
            new(2, 70, 110),
            new(3, 70, 130)
        ];

        int? selected = PreviewNavigationRules.FindCenterItemIndex(
            items,
            viewportHeight: 70);

        Assert.Equal(1, selected);
    }

    [Fact]
    public void FindCenterItemTraversesTwoColumnsFromLeftToRight()
    {
        PreviewItemGeometry[] items =
        [
            new(0, 0, 70),
            new(1, 0, 70),
            new(2, 70, 140),
            new(3, 70, 140)
        ];

        Assert.Equal(
            0,
            PreviewNavigationRules.FindCenterItemIndex(
                items,
                viewportHeight: 70));
        Assert.Equal(
            1,
            PreviewNavigationRules.FindCenterItemIndex(
                [
                    new(0, -5, 65),
                    new(1, -5, 65),
                    new(2, 65, 135),
                    new(3, 65, 135)
                ],
                viewportHeight: 70));
        Assert.Equal(
            2,
            PreviewNavigationRules.FindCenterItemIndex(
                [
                    new(0, -40, 30),
                    new(1, -40, 30),
                    new(2, 30, 100),
                    new(3, 30, 100)
                ],
                viewportHeight: 70));
    }

    [Fact]
    public void FindCenterItemUsesRowMajorOrderWhenCardsShareTheCenter()
    {
        PreviewItemGeometry[] items =
        [
            new(3, 20, 40),
            new(1, 0, 20),
            new(2, 20, 40),
            new(0, 0, 20)
        ];

        int? selected = PreviewNavigationRules.FindCenterItemIndex(
            items,
            viewportHeight: 40,
            columnCount: 1);

        Assert.Equal(0, selected);
    }

    [Fact]
    public void FindCenterItemKeepsTheRightColumnWhenItIsTheCurrentTie()
    {
        PreviewItemGeometry[] items =
        [
            new(0, 0, 40),
            new(1, 0, 40)
        ];

        int? selected = PreviewNavigationRules.FindCenterItemIndex(
            items,
            viewportHeight: 40,
            preferredIndex: 1);

        Assert.Equal(1, selected);
    }

    [Fact]
    public void FindCenterItemReturnsNullForEmptyOrInvalidViewports()
    {
        PreviewItemGeometry[] items = [new(0, 0, 40)];

        Assert.Null(
            PreviewNavigationRules.FindCenterItemIndex([], 40));
        Assert.Null(
            PreviewNavigationRules.FindCenterItemIndex(items, 0));
        Assert.Null(
            PreviewNavigationRules.FindCenterItemIndex(items, -1));
    }

    [Fact]
    public void CenteredOffsetClampsToScrollableBounds()
    {
        Assert.Equal(
            0,
            PreviewNavigationRules.GetCenteredVerticalOffset(
                currentOffset: 10,
                itemTop: -5,
                itemHeight: 20,
                viewportHeight: 100,
                extentHeight: 300));
        Assert.Equal(
            200,
            PreviewNavigationRules.GetCenteredVerticalOffset(
                currentOffset: 180,
                itemTop: 160,
                itemHeight: 20,
                viewportHeight: 100,
                extentHeight: 300));
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
    public void PendingScrollSelectionIsConsumedOnce()
    {
        PreviewNavigationCoordinator coordinator = new();
        coordinator.QueuePendingScrollSelection(4);

        Assert.Equal(4, coordinator.ConsumePendingScrollSelection());
        Assert.Null(coordinator.ConsumePendingScrollSelection());
    }

    [Fact]
    public void ClearRevealCancelsRevealAndPendingScrollSelection()
    {
        PreviewNavigationCoordinator coordinator = new();
        coordinator.BeginReveal("character-1");
        coordinator.QueuePendingScrollSelection(4);

        coordinator.ClearReveal();

        Assert.False(coordinator.IsRevealing);
        Assert.Null(coordinator.ConsumePendingScrollSelection());
    }
}
