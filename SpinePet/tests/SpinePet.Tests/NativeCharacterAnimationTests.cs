using SpinePet.Rendering.Native;

namespace SpinePet.Tests;

public sealed class NativeCharacterAnimationTests
{
    [Fact]
    public void ClickAnimationPrefersConventionalActionName()
    {
        string? animation =
            NativeCharacterRenderHost.SelectClickAnimationName(
                ["idle", "skillcut_1", "action"]);

        Assert.Equal("action", animation);
    }

    [Fact]
    public void ClickAnimationSupportsBurstSkillcutNames()
    {
        string? animation =
            NativeCharacterRenderHost.SelectClickAnimationName(
                ["idle", "idle2", "skillcut_1", "skillcut_2"]);

        Assert.Equal("skillcut_1", animation);
    }

    [Fact]
    public void IdleAnimationUsesIdleVariantWhenExactIdleIsMissing()
    {
        string? animation =
            NativeCharacterRenderHost.SelectIdleAnimationName(
                ["walk", "idle2", "skillcut_1"]);

        Assert.Equal("idle2", animation);
    }

    [Fact]
    public void ConfiguredAnimationIsKeptWhenItExists()
    {
        string? animation =
            NativeCharacterRenderHost.SelectConfiguredOrIdleAnimationName(
                "action",
                ["idle", "action"]);

        Assert.Equal("action", animation);
    }

    [Fact]
    public void MissingConfiguredAnimationFallsBackToIdle()
    {
        string? animation =
            NativeCharacterRenderHost.SelectConfiguredOrIdleAnimationName(
                string.Empty,
                ["action", "idle"]);

        Assert.Equal("idle", animation);
    }

    [Fact]
    public void InvalidConfiguredAnimationFallsBackToIdleVariant()
    {
        string? animation =
            NativeCharacterRenderHost.SelectConfiguredOrIdleAnimationName(
                "missing",
                ["action", "idle_loop"]);

        Assert.Equal("idle_loop", animation);
    }
}
