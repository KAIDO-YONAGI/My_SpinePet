using System.Globalization;
using Spine;

namespace SpinePet.Infrastructure;

internal static class SpineTimelineKey
{
    public static string Resolve(Timeline timeline, SkeletonData skeletonData)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        ArgumentNullException.ThrowIfNull(skeletonData);

        string type = timeline.GetType().Name;
        return timeline switch
        {
            DeformTimeline deform =>
                $"{type}@slot:{ResolveSlot(skeletonData, deform.SlotIndex)}/" +
                deform.Attachment.Name,
            SequenceTimeline sequence =>
                $"{type}@slot:{ResolveSlot(skeletonData, sequence.SlotIndex)}/" +
                sequence.Attachment.Name,
            IBoneTimeline bone =>
                $"{type}@bone:{ResolveBone(skeletonData, bone.BoneIndex)}",
            ISlotTimeline slot =>
                $"{type}@slot:{ResolveSlot(skeletonData, slot.SlotIndex)}",
            IkConstraintTimeline ik =>
                $"{type}@ik:{ResolveIk(skeletonData, ik.IkConstraintIndex)}",
            TransformConstraintTimeline transform =>
                $"{type}@transform:{ResolveTransform(
                    skeletonData,
                    transform.TransformConstraintIndex)}",
            PathConstraintPositionTimeline path =>
                $"{type}@path:{ResolvePath(
                    skeletonData,
                    path.PathConstraintIndex)}",
            PathConstraintSpacingTimeline path =>
                $"{type}@path:{ResolvePath(
                    skeletonData,
                    path.PathConstraintIndex)}",
            PathConstraintMixTimeline path =>
                $"{type}@path:{ResolvePath(
                    skeletonData,
                    path.PathConstraintIndex)}",
            EventTimeline => $"{type}@global",
            DrawOrderTimeline => $"{type}@global",
            _ => $"{type}@property:{string.Join(",", timeline.PropertyIds)}"
        };
    }

    private static string ResolveBone(SkeletonData data, int index) =>
        index >= 0 && index < data.Bones.Count
            ? data.Bones.Items[index].Name
            : index.ToString(CultureInfo.InvariantCulture);

    private static string ResolveSlot(SkeletonData data, int index) =>
        index >= 0 && index < data.Slots.Count
            ? data.Slots.Items[index].Name
            : index.ToString(CultureInfo.InvariantCulture);

    private static string ResolveIk(SkeletonData data, int index) =>
        index >= 0 && index < data.IkConstraints.Count
            ? data.IkConstraints.Items[index].Name
            : index.ToString(CultureInfo.InvariantCulture);

    private static string ResolveTransform(SkeletonData data, int index) =>
        index >= 0 && index < data.TransformConstraints.Count
            ? data.TransformConstraints.Items[index].Name
            : index.ToString(CultureInfo.InvariantCulture);

    private static string ResolvePath(SkeletonData data, int index) =>
        index >= 0 && index < data.PathConstraints.Count
            ? data.PathConstraints.Items[index].Name
            : index.ToString(CultureInfo.InvariantCulture);
}
