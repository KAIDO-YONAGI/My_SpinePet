using SpinePet.Views;

namespace SpinePet.Tests;

public sealed class PreviewNavigationCoordinatorTests
{
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
}
