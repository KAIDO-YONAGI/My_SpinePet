using System.Drawing;
using System.Reflection;
using System.Resources;

namespace SpinePet.Tests;

public sealed class BrandAssetTests
{
    private static readonly int[] ExpectedIconSizes =
        [16, 20, 24, 32, 40, 48, 64, 128, 256];

    [Fact]
    public void CompiledWpfResourcesContainBrandAssets()
    {
        Assembly applicationAssembly = typeof(App).Assembly;
        string resourceName = Assert.Single(
            applicationAssembly.GetManifestResourceNames(),
            name => name.EndsWith(".g.resources", StringComparison.Ordinal));

        using Stream? resourceStream =
            applicationAssembly.GetManifestResourceStream(resourceName);
        Assert.NotNull(resourceStream);

        using ResourceReader reader = new(resourceStream);
        HashSet<string> resourceKeys = reader
            .Cast<System.Collections.DictionaryEntry>()
            .Select(entry => Assert.IsType<string>(entry.Key))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("assets/brand/spinepet-avatar.png", resourceKeys);
        Assert.Contains("assets/brand/spinepet.ico", resourceKeys);
    }

    [Fact]
    public void WindowsIconContainsEverySupportedSize()
    {
        string iconPath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SpinePet",
            "Assets",
            "Brand",
            "SpinePet.ico");
        using FileStream iconStream = File.OpenRead(iconPath);
        using BinaryReader reader = new(iconStream);

        Assert.Equal((ushort)0, reader.ReadUInt16());
        Assert.Equal((ushort)1, reader.ReadUInt16());
        ushort frameCount = reader.ReadUInt16();
        Assert.Equal(ExpectedIconSizes.Length, frameCount);

        List<int> widths = [];
        for (int index = 0; index < frameCount; index++)
        {
            int width = reader.ReadByte();
            int height = reader.ReadByte();
            reader.BaseStream.Seek(14, SeekOrigin.Current);
            int normalizedWidth = width == 0 ? 256 : width;
            int normalizedHeight = height == 0 ? 256 : height;
            widths.Add(normalizedWidth);
            Assert.Equal(normalizedWidth, normalizedHeight);
        }

        Assert.Equal(ExpectedIconSizes, widths);

        iconStream.Position = 0;
        using Icon icon = new(iconStream, new Size(32, 32));
        Assert.Equal(new Size(32, 32), icon.Size);

        string executablePath = Path.Combine(
            Path.GetDirectoryName(typeof(App).Assembly.Location)!,
            "SpinePet.exe");
        Assert.True(File.Exists(executablePath));
        using Icon? executableIcon = Icon.ExtractAssociatedIcon(executablePath);
        Assert.NotNull(executableIcon);
        Assert.Equal(executableIcon.Width, executableIcon.Height);
        Assert.Contains(executableIcon.Width, ExpectedIconSizes);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SpinePet.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate SpinePet.sln.");
    }
}
