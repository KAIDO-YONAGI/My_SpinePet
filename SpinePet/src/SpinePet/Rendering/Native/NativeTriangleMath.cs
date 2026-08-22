using System.Numerics;

namespace SpinePet.Rendering.Native;

internal static class NativeTriangleMath
{
    public static bool TryGetBarycentric(
        float x,
        float y,
        Vector2 first,
        Vector2 second,
        Vector2 third,
        out float firstWeight,
        out float secondWeight,
        out float thirdWeight)
    {
        float denominator =
            (second.Y - third.Y) * (first.X - third.X) +
            (third.X - second.X) * (first.Y - third.Y);
        if (MathF.Abs(denominator) < 0.00001f)
        {
            firstWeight = 0;
            secondWeight = 0;
            thirdWeight = 0;
            return false;
        }

        firstWeight =
            ((second.Y - third.Y) * (x - third.X) +
             (third.X - second.X) * (y - third.Y)) /
            denominator;
        secondWeight =
            ((third.Y - first.Y) * (x - third.X) +
             (first.X - third.X) * (y - third.Y)) /
            denominator;
        thirdWeight = 1 - firstWeight - secondWeight;
        const float tolerance = -0.0001f;
        return firstWeight >= tolerance &&
               secondWeight >= tolerance &&
               thirdWeight >= tolerance;
    }
}
