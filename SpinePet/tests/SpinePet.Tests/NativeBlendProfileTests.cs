using System.Numerics;
using Spine;
using SpinePet.Rendering.Native;
using Vortice.Direct3D11;

namespace SpinePet.Tests;

public sealed class NativeBlendProfileTests
{
    [Theory]
    [InlineData(
        BlendMode.Normal,
        Blend.One,
        Blend.InverseSourceAlpha,
        Blend.One,
        Blend.InverseSourceAlpha)]
    [InlineData(
        BlendMode.Additive,
        Blend.One,
        Blend.One,
        Blend.One,
        Blend.One)]
    [InlineData(
        BlendMode.Multiply,
        Blend.DestinationColor,
        Blend.InverseSourceAlpha,
        Blend.One,
        Blend.InverseSourceAlpha)]
    [InlineData(
        BlendMode.Screen,
        Blend.One,
        Blend.InverseSourceColor,
        Blend.One,
        Blend.InverseSourceAlpha)]
    public void BlendProfilesSeparateColorAndCoverageAlpha(
        BlendMode blendMode,
        Blend sourceColor,
        Blend destinationColor,
        Blend sourceAlpha,
        Blend destinationAlpha)
    {
        NativeBlendProfile profile = NativeBlendProfiles.Get(blendMode);

        Assert.Equal(sourceColor, profile.SourceColor);
        Assert.Equal(destinationColor, profile.DestinationColor);
        Assert.Equal(sourceAlpha, profile.SourceAlpha);
        Assert.Equal(destinationAlpha, profile.DestinationAlpha);
    }

    [Theory]
    [InlineData(BlendMode.Normal, 0.5, 0.325, 0.2, 0.625)]
    [InlineData(BlendMode.Additive, 0.6, 0.4, 0.25, 0.75)]
    [InlineData(BlendMode.Multiply, 0.38, 0.255, 0.16, 0.625)]
    [InlineData(BlendMode.Screen, 0.52, 0.37, 0.24, 0.625)]
    public void PremultipliedCompositingMatchesSpineBlendRules(
        BlendMode blendMode,
        double red,
        double green,
        double blue,
        double alpha)
    {
        Vector4 result = NativeBlendProfiles.CompositePremultiplied(
            blendMode,
            new Vector4(0.2f, 0.1f, 0.05f, 0.25f),
            new Vector4(0.4f, 0.3f, 0.2f, 0.5f));

        Assert.Equal(red, result.X, precision: 5);
        Assert.Equal(green, result.Y, precision: 5);
        Assert.Equal(blue, result.Z, precision: 5);
        Assert.Equal(alpha, result.W, precision: 5);
    }

    [Fact]
    public void AdditiveParticlesKeepPremultipliedSurfaceCoverage()
    {
        Vector4 destination = new(0.1f, 0.2f, 0.3f, 0.4f);
        Vector4 source = new(0.3f, 0.2f, 0.1f, 0.75f);

        Vector4 first = NativeBlendProfiles.CompositePremultiplied(
            BlendMode.Additive,
            source,
            destination);
        Vector4 second = NativeBlendProfiles.CompositePremultiplied(
            BlendMode.Additive,
            source,
            first);

        Assert.True(second.X > destination.X);
        Assert.Equal(1, second.W);
        Assert.True(first.W > destination.W);
        Assert.True(first.X <= first.W);
        Assert.True(first.Y <= first.W);
        Assert.True(first.Z <= first.W);
        Assert.True(second.X <= second.W);
        Assert.True(second.Y <= second.W);
        Assert.True(second.Z <= second.W);
    }
}
