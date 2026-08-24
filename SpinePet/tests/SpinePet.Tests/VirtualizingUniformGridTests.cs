using SpinePet.Views;

namespace SpinePet.Tests;

public sealed class VirtualizingUniformGridTests
{
    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    public void DetachedRecycledContainersAreInsertedBackIntoVisualTree(
        bool newlyRealized,
        bool isAttached,
        bool expected)
    {
        Assert.Equal(
            expected,
            VirtualizingUniformGridLayout.RequiresVisualInsertion(
                newlyRealized,
                isAttached));
    }

    [Fact]
    public void LargeLibraryCalculatesOnlyViewportAndBufferRows()
    {
        VirtualizedItemRange range =
            VirtualizingUniformGridLayout.CalculateRange(
                itemCount: 1000,
                columns: 2,
                itemHeight: 128,
                verticalOffset: 1280,
                viewportHeight: 384,
                bufferRows: 1);

        Assert.Equal(18, range.FirstIndex);
        Assert.Equal(29, range.LastIndex);
        Assert.Equal(12, range.LastIndex - range.FirstIndex + 1);
    }

    [Fact]
    public void EmptyLibraryHasNoRealizationRange()
    {
        VirtualizedItemRange range =
            VirtualizingUniformGridLayout.CalculateRange(
                0,
                2,
                128,
                0,
                384,
                1);

        Assert.Equal(0, range.FirstIndex);
        Assert.Equal(-1, range.LastIndex);
    }
}
