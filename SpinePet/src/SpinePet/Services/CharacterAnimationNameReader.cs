using System.IO;
using Spine;
using SpinePet.Infrastructure;
using SpinePet.Infrastructure.Import;
using SpinePet.Models;

namespace SpinePet.Services;

internal static class CharacterAnimationNameReader
{
    public static IReadOnlyList<string> Read(CharacterConfig character)
    {
        ArgumentNullException.ThrowIfNull(character);
        if (!File.Exists(character.SkeletonPath) ||
            !File.Exists(character.AtlasPath))
        {
            return [];
        }

        try
        {
            SpineSkeletonCompatibility.EnsureSupported(
                character.SkeletonPath);
            Atlas atlas = new(
                character.AtlasPath,
                new NoopTextureLoader());
            try
            {
                SkeletonData data = Path.GetExtension(character.SkeletonPath)
                    .Equals(".json", StringComparison.OrdinalIgnoreCase)
                        ? new SkeletonJson(atlas).ReadSkeletonData(
                            character.SkeletonPath)
                        : new SkeletonBinary(atlas).ReadSkeletonData(
                            character.SkeletonPath);
                return data.Animations
                    .Select(animation => animation.Name)
                    .ToArray();
            }
            finally
            {
                atlas.Dispose();
            }
        }
        catch (Exception exception)
        {
            AppLogger.Write(
                nameof(CharacterAnimationNameReader),
                $"animation-metadata-read-failed id={character.Id} " +
                $"message={exception.Message}");
            return [];
        }
    }

    private sealed class NoopTextureLoader : TextureLoader
    {
        public void Load(AtlasPage page, string path) { }
        public void Unload(object texture) { }
    }
}
