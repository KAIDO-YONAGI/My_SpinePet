using System.Drawing;
using System.Diagnostics.CodeAnalysis;

namespace SpinePet.Rendering.Native;

internal static class NativeCharacterHitTester
{
    public static bool TryHit(
        NativeCharacterScene scene,
        NativeCompositionWindow? window,
        int screenX,
        int screenY,
        [NotNullWhen(true)] out NativeCharacterState? result)
    {
        if (window == null)
        {
            result = null;
            return false;
        }

        foreach (string characterId in scene.TopToBottom)
        {
            if (!scene.TryGet(
                    characterId,
                    out NativeCharacterState? state))
            {
                continue;
            }

            if (state.IsVisible &&
                !state.ScreenBounds.IsEmpty &&
                state.ScreenBounds.Contains(screenX, screenY) &&
                HitTestGeometry(state, window, screenX, screenY))
            {
                result = state;
                return true;
            }
        }

        result = null;
        return false;
    }

    private static bool HitTestGeometry(
        NativeCharacterState state,
        NativeCompositionWindow window,
        int screenX,
        int screenY)
    {
        if (state.LastBatches.Count == 0)
            return false;

        float pixelScale =
            (float)state.CurrentScale * window.DpiScale;
        if (pixelScale <= 0)
            return false;

        float anchorX =
            window.Left +
            NativeInputRegionCoordinator.ToClientPixelX(
                window,
                state.Config.PositionX);
        float anchorY =
            window.Top +
            NativeInputRegionCoordinator.ToClientPixelY(
                window,
                state.Config.PositionY);
        float x = state.PivotX + (screenX - anchorX) / pixelScale;
        float y = state.PivotY + (screenY - anchorY) / pixelScale;

        for (int batchIndex = state.LastBatches.Count - 1;
             batchIndex >= 0;
             batchIndex--)
        {
            NativeSpineDrawBatch batch = state.LastBatches[batchIndex];
            for (int triangle = batch.IndexCount - 3;
                 triangle >= 0;
                 triangle -= 3)
            {
                NativeSpineVertex first =
                    batch.Vertices[batch.Indices[triangle]];
                NativeSpineVertex second =
                    batch.Vertices[batch.Indices[triangle + 1]];
                NativeSpineVertex third =
                    batch.Vertices[batch.Indices[triangle + 2]];
                if (!NativeTriangleMath.TryGetBarycentric(
                        x,
                        y,
                        first.Position,
                        second.Position,
                        third.Position,
                        out float firstWeight,
                        out float secondWeight,
                        out float thirdWeight))
                {
                    continue;
                }

                float u =
                    first.TextureCoordinate.X * firstWeight +
                    second.TextureCoordinate.X * secondWeight +
                    third.TextureCoordinate.X * thirdWeight;
                float v =
                    first.TextureCoordinate.Y * firstWeight +
                    second.TextureCoordinate.Y * secondWeight +
                    third.TextureCoordinate.Y * thirdWeight;
                float opacity =
                    first.LightColor.W * firstWeight +
                    second.LightColor.W * secondWeight +
                    third.LightColor.W * thirdWeight;
                if (batch.Texture.IsVisiblePixel(u, v, opacity))
                    return true;
            }
        }

        return false;
    }
}
