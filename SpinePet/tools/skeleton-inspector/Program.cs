using Spine;

if (args.Length != 2)
{
    Console.Error.WriteLine(
        "Usage: SkeletonInspector <skeleton.skel|json> <texture.atlas>");
    return 1;
}

string skeletonPath = Path.GetFullPath(args[0]);
string atlasPath = Path.GetFullPath(args[1]);
Atlas atlas = new(atlasPath, new NoopTextureLoader());
SkeletonData data = Path.GetExtension(skeletonPath)
    .Equals(".json", StringComparison.OrdinalIgnoreCase)
    ? new SkeletonJson(atlas).ReadSkeletonData(skeletonPath)
    : new SkeletonBinary(atlas).ReadSkeletonData(skeletonPath);

Console.WriteLine(
    $"version={data.Version} skins={data.Skins.Count} " +
    $"slots={data.Slots.Count} animations={data.Animations.Count}");
foreach (Animation animation in data.Animations)
    Console.WriteLine($"animation\t{animation.Name}");
Console.WriteLine(
    "skin\tslot\tplaceholder\ttype\tattachment\tpath\tregion");

foreach (Skin skin in data.Skins)
{
    foreach (Skin.SkinEntry entry in skin.Attachments
                 .OrderBy(item => item.SlotIndex)
                 .ThenBy(item => item.Name, StringComparer.Ordinal))
    {
        Attachment attachment = entry.Attachment;
        string path = attachment switch
        {
            RegionAttachment regionAttachment => regionAttachment.Path,
            MeshAttachment meshAttachment => meshAttachment.Path,
            _ => string.Empty
        };
        string region = attachment switch
        {
            RegionAttachment regionAttachment =>
                (regionAttachment.Region as AtlasRegion)?.name ?? string.Empty,
            MeshAttachment meshAttachment =>
                (meshAttachment.Region as AtlasRegion)?.name ?? string.Empty,
            _ => string.Empty
        };
        string slot = data.Slots.Items[entry.SlotIndex].Name;
        Console.WriteLine(
            string.Join(
                '\t',
                skin.Name,
                slot,
                entry.Name,
                attachment.GetType().Name,
                attachment.Name,
                path,
                region));
    }
}

atlas.Dispose();
return 0;

internal sealed class NoopTextureLoader : TextureLoader
{
    public void Load(AtlasPage page, string path)
    {
    }

    public void Unload(object texture)
    {
    }
}
