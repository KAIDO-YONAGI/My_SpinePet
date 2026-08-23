using System.Windows.Media;
using System.Windows.Media.Imaging;
using SpinePet.ViewModels;
using SpinePet.Views;

namespace SpinePet.Tests;

public sealed class CharacterViewModelTests
{
    [Fact]
    public void ThumbnailPathChangeUsesStablePlaceholderUntilDecodeCompletes()
    {
        CharacterViewModel viewModel = new()
        {
            Id = "thumbnail"
        };

        viewModel.ThumbnailPath = @"C:\images\thumbnail.png";

        Assert.Null(viewModel.ThumbnailImage);
        Assert.Equal(64, viewModel.ThumbnailFrameWidth);
        Assert.Equal(64, viewModel.ThumbnailFrameHeight);
    }

    [Fact]
    public void StaleThumbnailResultDoesNotReplaceCurrentPath()
    {
        CharacterViewModel viewModel = new()
        {
            Id = "thumbnail",
            ThumbnailPath = @"C:\images\current.png"
        };
        DrawingImage image = new();
        image.Freeze();

        viewModel.ApplyThumbnail(
            @"C:\images\old.png",
            image,
            32,
            64);

        Assert.Null(viewModel.ThumbnailImage);
        Assert.Equal(64, viewModel.ThumbnailFrameWidth);
        Assert.Equal(64, viewModel.ThumbnailFrameHeight);
    }

    [Fact]
    public async Task ThumbnailServiceCachesFrozenDecodeAndInvalidatesOnChange()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"spinepet-thumbnail-{Guid.NewGuid():N}.png");
        try
        {
            WritePng(path, width: 2, height: 4);
            CharacterThumbnailService service = new();

            CharacterThumbnail first = await service.LoadAsync(
                path,
                CancellationToken.None);
            CharacterThumbnail second = await service.LoadAsync(
                path,
                CancellationToken.None);

            Assert.NotNull(first.Image);
            Assert.True(first.Image.IsFrozen);
            Assert.Same(first.Image, second.Image);
            Assert.Equal(1, service.DecodeCount);
            Assert.Equal(32, first.FrameWidth);
            Assert.Equal(64, first.FrameHeight);

            await File.AppendAllTextAsync(path, " ");
            CharacterThumbnail changed = await service.LoadAsync(
                path,
                CancellationToken.None);

            Assert.NotNull(changed.Image);
            Assert.Equal(2, service.DecodeCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ThumbnailServiceUsesStableFallbackForDamagedImage()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"spinepet-thumbnail-{Guid.NewGuid():N}.png");
        try
        {
            await File.WriteAllTextAsync(path, "not an image");
            CharacterThumbnailService service = new();

            CharacterThumbnail thumbnail = await service.LoadAsync(
                path,
                CancellationToken.None);

            Assert.Null(thumbnail.Image);
            Assert.Equal(64, thumbnail.FrameWidth);
            Assert.Equal(64, thumbnail.FrameHeight);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ThumbnailServiceEvictsOldestEntryWhenCacheIsFull()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"spinepet-thumbnail-cache-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string firstPath = Path.Combine(directory, "first.png");
            string secondPath = Path.Combine(directory, "second.png");
            string thirdPath = Path.Combine(directory, "third.png");
            WritePng(firstPath, width: 2, height: 2);
            WritePng(secondPath, width: 2, height: 2);
            WritePng(thirdPath, width: 2, height: 2);
            CharacterThumbnailService service =
                new(maximumCacheEntries: 2);

            await service.LoadAsync(firstPath, CancellationToken.None);
            await service.LoadAsync(secondPath, CancellationToken.None);
            await service.LoadAsync(thirdPath, CancellationToken.None);

            Assert.Equal(2, service.CacheEntryCount);
            Assert.Equal(3, service.DecodeCount);

            await service.LoadAsync(firstPath, CancellationToken.None);

            Assert.Equal(2, service.CacheEntryCount);
            Assert.Equal(4, service.DecodeCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void WritePng(string path, int width, int height)
    {
        byte[] pixels = new byte[width * height * 4];
        Array.Fill(pixels, (byte)255);
        BitmapSource source = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            width * 4);
        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using FileStream stream = File.Create(path);
        encoder.Save(stream);
    }
}
