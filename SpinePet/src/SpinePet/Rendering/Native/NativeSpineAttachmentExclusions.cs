using System.IO;
using Spine;

namespace SpinePet.Rendering.Native;

internal readonly record struct NativeSpineAttachmentExclusionRule(
    bool IsPrefix,
    string Value)
{
    public bool Matches(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            return false;

        return IsPrefix
            ? candidate.StartsWith(Value, StringComparison.OrdinalIgnoreCase)
            : candidate.Equals(Value, StringComparison.OrdinalIgnoreCase);
    }
}

internal static class NativeSpineAttachmentExclusions
{
    internal const string FileSuffix = ".attachments.exclude";

    public static int Apply(SkeletonData data, string skeletonPath)
    {
        string exclusionPath = GetPath(skeletonPath);
        if (!File.Exists(exclusionPath))
            return 0;

        IReadOnlyList<NativeSpineAttachmentExclusionRule> rules =
            Parse(File.ReadLines(exclusionPath));
        if (rules.Count == 0)
            return 0;

        int removedCount = 0;
        foreach (Skin skin in data.Skins)
        {
            Skin.SkinEntry[] excluded = skin.Attachments
                .Where(entry => Matches(data, entry, rules))
                .ToArray();
            foreach (Skin.SkinEntry entry in excluded)
            {
                skin.RemoveAttachment(entry.SlotIndex, entry.Name);
                removedCount++;
            }
        }

        return removedCount;
    }

    internal static string GetPath(string skeletonPath)
    {
        string directory = Path.GetDirectoryName(skeletonPath) ?? string.Empty;
        string stem = Path.GetFileNameWithoutExtension(skeletonPath);
        return Path.Combine(directory, stem + FileSuffix);
    }

    internal static IReadOnlyList<NativeSpineAttachmentExclusionRule> Parse(
        IEnumerable<string> lines)
    {
        List<NativeSpineAttachmentExclusionRule> rules = [];
        int lineNumber = 0;
        foreach (string sourceLine in lines)
        {
            lineNumber++;
            string line = sourceLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            bool isPrefix;
            string value;
            if (line.StartsWith("prefix:", StringComparison.OrdinalIgnoreCase))
            {
                isPrefix = true;
                value = line["prefix:".Length..].Trim();
            }
            else if (line.StartsWith(
                         "name:",
                         StringComparison.OrdinalIgnoreCase))
            {
                isPrefix = false;
                value = line["name:".Length..].Trim();
            }
            else
            {
                throw new FormatException(
                    $"Invalid attachment exclusion rule at line {lineNumber}: " +
                    $"'{sourceLine}'.");
            }

            if (value.Length == 0)
            {
                throw new FormatException(
                    $"Attachment exclusion rule at line {lineNumber} " +
                    "has no value.");
            }

            rules.Add(new NativeSpineAttachmentExclusionRule(
                isPrefix,
                value));
        }

        return rules;
    }

    private static bool Matches(
        SkeletonData data,
        Skin.SkinEntry entry,
        IReadOnlyList<NativeSpineAttachmentExclusionRule> rules)
    {
        Attachment attachment = entry.Attachment;
        string? path = attachment switch
        {
            RegionAttachment regionAttachment => regionAttachment.Path,
            MeshAttachment meshAttachment => meshAttachment.Path,
            _ => null
        };
        string? regionName = attachment switch
        {
            RegionAttachment regionAttachment =>
                (regionAttachment.Region as AtlasRegion)?.name,
            MeshAttachment meshAttachment =>
                (meshAttachment.Region as AtlasRegion)?.name,
            _ => null
        };
        string slotName = data.Slots.Items[entry.SlotIndex].Name;
        string?[] identifiers =
        [
            entry.Name,
            attachment.Name,
            path,
            regionName,
            slotName
        ];

        return rules.Any(
            rule => identifiers.Any(rule.Matches));
    }
}
