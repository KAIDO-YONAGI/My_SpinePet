using SpinePet.Models;
using SpinePet.Rendering.Native;

namespace SpinePet.Tests;

public sealed class NativeCharacterRenderHostPerformanceTests
{
    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(120)]
    public void FrameLoopStartsIdleAndAcceptsSupportedRate(
        int targetFrameRate)
    {
        using NativeCharacterRenderHost renderHost = new();

        Assert.False(renderHost.IsFrameLoopRunning);

        renderHost.SetTargetFrameRate(targetFrameRate);

        Assert.Equal(targetFrameRate, renderHost.TargetFrameRate);
        Assert.Equal(
            TimeSpan.FromSeconds(1.0 / targetFrameRate),
            renderHost.FrameInterval);
        Assert.False(renderHost.IsFrameLoopRunning);
    }
}
