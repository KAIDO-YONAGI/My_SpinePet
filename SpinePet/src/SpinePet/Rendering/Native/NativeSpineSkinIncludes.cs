using Spine;

namespace SpinePet.Rendering.Native;

internal static class NativeSpineSkinIncludes
{
    public static int Apply(
        SkeletonData data,
        Skeleton skeleton)
    {
        Skin[] additionalSkins = data.Skins
            .Where(skin => !ReferenceEquals(skin, data.DefaultSkin))
            .ToArray();
        if (additionalSkins.Length == 0)
            return 0;

        Skin combinedSkin = new("SpinePet combined skin");
        if (data.DefaultSkin != null)
            combinedSkin.AddSkin(data.DefaultSkin);

        foreach (Skin skin in additionalSkins)
            combinedSkin.AddSkin(skin);

        skeleton.SetSkin(combinedSkin);
        skeleton.SetSlotsToSetupPose();
        return additionalSkins.Length;
    }
}
