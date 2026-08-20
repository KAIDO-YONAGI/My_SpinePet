using SpinePet.Rendering.Native;

namespace SpinePet.Tests;

public sealed class NativeCharacterZOrderTests
{
    [Fact]
    public void NewestCharacterIsHitTestedFirst()
    {
        NativeCharacterZOrder order = new();
        order.MoveToTop("bottom");
        order.MoveToTop("middle");
        order.MoveToTop("top");

        Assert.Equal(
            ["top", "middle", "bottom"],
            order.TopToBottom);
    }

    [Fact]
    public void ReattachedCharacterMovesToTop()
    {
        NativeCharacterZOrder order = new();
        order.MoveToTop("first");
        order.MoveToTop("second");
        order.MoveToTop("first");

        Assert.Equal(
            ["first", "second"],
            order.TopToBottom);
    }

    [Fact]
    public void RemovedCharacterLeavesHitTestOrder()
    {
        NativeCharacterZOrder order = new();
        order.MoveToTop("first");
        order.MoveToTop("second");
        order.Remove("second");

        Assert.Equal(
            ["first"],
            order.TopToBottom);
    }
}
