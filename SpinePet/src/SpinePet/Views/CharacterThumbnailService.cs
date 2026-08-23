using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SpinePet.Views;

internal sealed record CharacterThumbnail(
    ImageSource? Image,
    double FrameWidth,
    double FrameHeight);

internal sealed class CharacterThumbnailService
{
    private const int ThumbnailBoxSize = 64;
    private const int MaximumConcurrentDecodes = 2;
    private const int DefaultMaximumCacheEntries = 256;
    private static readonly CharacterThumbnail Empty =
        new(null, ThumbnailBoxSize, ThumbnailBoxSize);

    private static readonly SemaphoreSlim DecodeSlots =
        new(MaximumConcurrentDecodes, MaximumConcurrentDecodes);
    private readonly ConcurrentDictionary<
        ThumbnailCacheKey,
        Lazy<Task<CharacterThumbnail>>> _cache = new();
    private readonly ConcurrentQueue<ThumbnailCacheKey> _cacheOrder = new();
    private readonly int _maximumCacheEntries;
    private int _decodeCount;

    internal CharacterThumbnailService(
        int maximumCacheEntries = DefaultMaximumCacheEntries)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maximumCacheEntries);
        _maximumCacheEntries = maximumCacheEntries;
    }

    internal int DecodeCount => Volatile.Read(ref _decodeCount);

    internal int CacheEntryCount => _cache.Count;

    public async Task<CharacterThumbnail> LoadAsync(
        string? path,
        CancellationToken cancellationToken)
    {
        if (!TryCreateCacheKey(path, out ThumbnailCacheKey key))
        {
            return Empty;
        }

        Lazy<Task<CharacterThumbnail>> candidate =
            new(
                () => DecodeAsync(key),
                LazyThreadSafetyMode.ExecutionAndPublication);
        Lazy<Task<CharacterThumbnail>> pending =
            _cache.GetOrAdd(key, candidate);
        if (ReferenceEquals(candidate, pending))
        {
            _cacheOrder.Enqueue(key);
            TrimCache();
        }

        return await pending.Value.WaitAsync(cancellationToken);
    }

    private void TrimCache()
    {
        while (_cache.Count > _maximumCacheEntries &&
               _cacheOrder.TryDequeue(out ThumbnailCacheKey oldest))
        {
            _cache.TryRemove(oldest, out _);
        }
    }

    private async Task<CharacterThumbnail> DecodeAsync(
        ThumbnailCacheKey key)
    {
        await DecodeSlots.WaitAsync();
        try
        {
            Interlocked.Increment(ref _decodeCount);
            return await Task.Run(() => Decode(key.Path));
        }
        catch
        {
            return Empty;
        }
        finally
        {
            DecodeSlots.Release();
        }
    }

    private static CharacterThumbnail Decode(string path)
    {
        using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        BitmapDecoder metadataDecoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.DelayCreation,
            BitmapCacheOption.None);
        BitmapFrame metadata = metadataDecoder.Frames[0];
        if (metadata.PixelWidth <= 0 || metadata.PixelHeight <= 0)
        {
            return Empty;
        }

        (double frameWidth, double frameHeight) =
            GetFrameSize(metadata.PixelWidth, metadata.PixelHeight);
        stream.Position = 0;

        BitmapImage image = new();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
        image.StreamSource = stream;
        if (metadata.PixelWidth >= metadata.PixelHeight)
        {
            image.DecodePixelWidth = ThumbnailBoxSize;
        }
        else
        {
            image.DecodePixelHeight = ThumbnailBoxSize;
        }

        image.EndInit();
        image.Freeze();
        return new CharacterThumbnail(image, frameWidth, frameHeight);
    }

    private static (double Width, double Height) GetFrameSize(
        int pixelWidth,
        int pixelHeight)
    {
        double aspect = pixelHeight / (double)pixelWidth;
        return aspect >= 1
            ? (
                Math.Clamp(ThumbnailBoxSize / aspect, 20, ThumbnailBoxSize),
                ThumbnailBoxSize)
            : (
                ThumbnailBoxSize,
                Math.Clamp(ThumbnailBoxSize * aspect, 20, ThumbnailBoxSize));
    }

    private static bool TryCreateCacheKey(
        string? path,
        out ThumbnailCacheKey key)
    {
        key = default;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            string fullPath = Path.GetFullPath(path);
            FileInfo file = new(fullPath);
            if (!file.Exists)
            {
                return false;
            }

            key = new ThumbnailCacheKey(
                fullPath,
                file.LastWriteTimeUtc.Ticks,
                file.Length);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private readonly record struct ThumbnailCacheKey(
        string Path,
        long LastWriteTicks,
        long Length);
}
