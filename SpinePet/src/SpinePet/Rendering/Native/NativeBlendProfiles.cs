using System.Numerics;
using Spine;
using Vortice.Direct3D11;

namespace SpinePet.Rendering.Native;

internal readonly record struct NativeBlendProfile(
    Blend SourceColor,
    Blend DestinationColor,
    Blend SourceAlpha,
    Blend DestinationAlpha)
{
    public BlendDescription CreateDescription() =>
        new(
            SourceColor,
            DestinationColor,
            SourceAlpha,
            DestinationAlpha);
}

internal static class NativeBlendProfiles
{
    public static NativeBlendProfile Get(BlendMode blendMode) =>
        blendMode switch
        {
            BlendMode.Additive => new NativeBlendProfile(
                Blend.One,
                Blend.One,
                Blend.One,
                Blend.One),
            BlendMode.Multiply => new NativeBlendProfile(
                Blend.DestinationColor,
                Blend.InverseSourceAlpha,
                Blend.One,
                Blend.InverseSourceAlpha),
            BlendMode.Screen => new NativeBlendProfile(
                Blend.One,
                Blend.InverseSourceColor,
                Blend.One,
                Blend.InverseSourceAlpha),
            _ => new NativeBlendProfile(
                Blend.One,
                Blend.InverseSourceAlpha,
                Blend.One,
                Blend.InverseSourceAlpha)
        };

    public static Vector4 CompositePremultiplied(
        BlendMode blendMode,
        Vector4 source,
        Vector4 destination)
    {
        Vector3 sourceColor = new(source.X, source.Y, source.Z);
        Vector3 destinationColor = new(
            destination.X,
            destination.Y,
            destination.Z);
        Vector3 color = blendMode switch
        {
            BlendMode.Additive => sourceColor + destinationColor,
            BlendMode.Multiply =>
                sourceColor * destinationColor +
                destinationColor * (1 - source.W),
            BlendMode.Screen =>
                sourceColor +
                destinationColor * (Vector3.One - sourceColor),
            _ => sourceColor + destinationColor * (1 - source.W)
        };
        float alpha = blendMode == BlendMode.Additive
            ? source.W + destination.W
            : source.W + destination.W * (1 - source.W);

        return Vector4.Clamp(
            new Vector4(color, alpha),
            Vector4.Zero,
            Vector4.One);
    }
}
