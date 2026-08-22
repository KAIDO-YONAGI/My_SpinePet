using System.IO;
using Spine;
using SpinePet.Infrastructure.Import;
using SpinePet.Models;

namespace SpinePet.Rendering.Native;

internal sealed class NativeSpineResource : IDisposable
{
    private NativeSpineResource(
        Atlas atlas,
        NativeAtlasTextureLoader textureLoader,
        SkeletonData skeletonData,
        int excludedAttachmentCount)
    {
        Atlas = atlas;
        TextureLoader = textureLoader;
        SkeletonData = skeletonData;
        ExcludedAttachmentCount = excludedAttachmentCount;
        Skeleton = new Skeleton(skeletonData);
        IncludedSkinCount = NativeSpineSkinIncludes.Apply(
            skeletonData,
            Skeleton);
        AnimationStateData = new AnimationStateData(skeletonData)
        {
            // Input-driven state changes must start from the selected state.
            DefaultMix = 0
        };
        AnimationState = new AnimationState(AnimationStateData);
        AnimationNames = skeletonData.Animations
            .Select(animation => animation.Name)
            .ToArray();

        Skeleton.SetToSetupPose();
        Skeleton.UpdateWorldTransform();
    }

    public Atlas Atlas { get; }
    public NativeAtlasTextureLoader TextureLoader { get; }
    public SkeletonData SkeletonData { get; }
    public int ExcludedAttachmentCount { get; }
    public int IncludedSkinCount { get; }
    public Skeleton Skeleton { get; }
    public AnimationStateData AnimationStateData { get; }
    public AnimationState AnimationState { get; }
    public IReadOnlyList<string> AnimationNames { get; }

    public static NativeSpineResource Load(CharacterConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (!File.Exists(config.AtlasPath))
            throw new FileNotFoundException("Spine atlas not found.", config.AtlasPath);
        if (!File.Exists(config.SkeletonPath))
            throw new FileNotFoundException("Spine skeleton not found.", config.SkeletonPath);

        SpineSkeletonCompatibility.EnsureSupported(config.SkeletonPath);

        Bone.yDown = true;
        NativeAtlasTextureLoader textureLoader = new();
        Atlas? atlas = null;
        try
        {
            atlas = new Atlas(config.AtlasPath, textureLoader);
            SkeletonData data = LoadSkeletonData(config.SkeletonPath, atlas);
            int excludedAttachmentCount =
                NativeSpineAttachmentExclusions.Apply(
                    data,
                    config.SkeletonPath);
            return new NativeSpineResource(
                atlas,
                textureLoader,
                data,
                excludedAttachmentCount);
        }
        catch
        {
            atlas?.Dispose();
            throw;
        }
    }

    public void SetAnimation(string? animationName, bool repeat)
    {
        string? selected = ResolveAnimationName(animationName);

        if (selected != null)
            AnimationState.SetAnimation(0, selected, repeat);
    }

    public void SetAnimationIfNeeded(string? animationName, bool repeat)
    {
        string? selected = ResolveAnimationName(animationName);
        if (selected == null)
            return;

        TrackEntry? current = AnimationState.GetCurrent(0);
        if (current?.Animation?.Name.Equals(
                selected,
                StringComparison.OrdinalIgnoreCase) == true &&
            current.Loop == repeat)
        {
            return;
        }

        AnimationState.SetAnimation(0, selected, repeat);
    }

    public void Update(float elapsedSeconds)
    {
        AnimationState.Update(Math.Max(0, elapsedSeconds));
        AnimationState.Apply(Skeleton);
        Skeleton.UpdateWorldTransform();
    }

    public void Dispose()
    {
        Atlas.Dispose();
    }

    private string? ResolveAnimationName(string? animationName)
    {
        if (!string.IsNullOrWhiteSpace(animationName) &&
            SkeletonData.FindAnimation(animationName) != null)
        {
            return animationName;
        }

        return AnimationNames.Count > 0 ? AnimationNames[0] : null;
    }

    private static SkeletonData LoadSkeletonData(string path, Atlas atlas)
    {
        string extension = System.IO.Path.GetExtension(path);
        if (extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
            return new SkeletonJson(atlas).ReadSkeletonData(path);

        return new SkeletonBinary(atlas).ReadSkeletonData(path);
    }
}
