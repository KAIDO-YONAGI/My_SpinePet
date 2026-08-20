using System.IO;
using Spine;

namespace SpinePet.Infrastructure.Import;

internal static class SpineSkeletonCompatibility
{
    public const string SupportedVersion = "4.1";

    public static string EnsureSupported(string skeletonPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(skeletonPath);

        string version;
        try
        {
            using FileStream stream = new(
                skeletonPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            version = SkeletonBinary.GetVersionString(stream);
        }
        catch (Exception exception) when (
            exception is ArgumentException or
                IOException or
                UnauthorizedAccessException)
        {
            throw new InvalidDataException(
                "The selected file is not valid Spine binary skeleton data.",
                exception);
        }

        if (!version.StartsWith(
                $"{SupportedVersion}.",
                StringComparison.Ordinal) &&
            !string.Equals(
                version,
                SupportedVersion,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Skeleton version {version} is not compatible with the " +
                $"Spine runtime {SupportedVersion}. Import a bundle exported " +
                $"by Spine {SupportedVersion}.xx.");
        }

        return version;
    }
}
