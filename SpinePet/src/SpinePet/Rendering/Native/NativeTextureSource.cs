using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Spine;

namespace SpinePet.Rendering.Native;

internal sealed class NativeTextureSource
{
    private readonly uint[] _visiblePixels;
    private readonly bool _requiresPremultiplication;
    private byte[]? _uploadPixels;

    public NativeTextureSource(
        string path,
        BitmapSource bitmap,
        bool sourcePremultipliedAlpha,
        byte[] pixels)
    {
        Path = path;
        // 只缓存宽高：完整 WIC 位图在像素交给 GPU/mask 后就不再保留，
        // 否则每张纹理会多占一份 ~W*H*4 的非托管内存。
        Width = bitmap.PixelWidth;
        Height = bitmap.PixelHeight;
        _requiresPremultiplication =
            !sourcePremultipliedAlpha &&
            bitmap.Format != PixelFormats.Pbgra32;
        if (_requiresPremultiplication)
            PremultiplyBgraPixels(pixels);
        _uploadPixels = pixels;
        _visiblePixels = BuildVisiblePixelMask(pixels);
    }

    public string Path { get; }
    public int Width { get; }
    public int Height { get; }

    public byte[] CopyBgraPixels()
    {
        byte[]? uploadPixels = Interlocked.Exchange(
            ref _uploadPixels,
            null);
        if (uploadPixels != null)
            return uploadPixels;

        // 像素只能被消费一次：资源在则 GPU 纹理必然存在，
        // 资源被卸载后会整体重新加载，不存在二次上传的场景。
        throw new InvalidOperationException(
            $"Texture pixels for '{Path}' were already consumed.");
    }

    public bool IsVisiblePixel(float u, float v, float opacity)
    {
        if (opacity < 16f / 255f)
            return false;

        int x = Math.Clamp((int)(u * Width), 0, Width - 1);
        int y = Math.Clamp((int)(v * Height), 0, Height - 1);
        int pixelIndex = checked(y * Width + x);
        return (_visiblePixels[pixelIndex >> 5] &
                (1u << (pixelIndex & 31))) != 0;
    }

    public static NativeTextureSource Load(string path, bool premultipliedAlpha)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Atlas texture not found.", path);

        using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        BitmapDecoder decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        BitmapSource source = decoder.Frames[0];
        if (source.Format != PixelFormats.Bgra32 &&
            source.Format != PixelFormats.Pbgra32)
        {
            source = new FormatConvertedBitmap(
                source,
                PixelFormats.Bgra32,
                null,
                0);
        }

        source.Freeze();
        int stride = checked(source.PixelWidth * 4);
        byte[] pixels =
            new byte[checked(stride * source.PixelHeight)];
        source.CopyPixels(pixels, stride, 0);
        return new NativeTextureSource(
            path,
            source,
            premultipliedAlpha,
            pixels);
    }

    private uint[] BuildVisiblePixelMask(byte[] pixels)
    {
        int pixelCount = checked(Width * Height);
        uint[] mask = new uint[(pixelCount + 31) / 32];
        for (int pixelIndex = 0;
             pixelIndex < pixelCount;
             pixelIndex++)
        {
            if (pixels[pixelIndex * 4 + 3] >= 16)
            {
                mask[pixelIndex >> 5] |=
                    1u << (pixelIndex & 31);
            }
        }

        return mask;
    }

    internal static void PremultiplyBgraPixels(Span<byte> pixels)
    {
        if (pixels.Length % 4 != 0)
        {
            throw new ArgumentException(
                "BGRA pixel data must contain complete pixels.",
                nameof(pixels));
        }

        for (int offset = 0; offset < pixels.Length; offset += 4)
        {
            int alpha = pixels[offset + 3];
            pixels[offset] =
                (byte)((pixels[offset] * alpha + 127) / 255);
            pixels[offset + 1] =
                (byte)((pixels[offset + 1] * alpha + 127) / 255);
            pixels[offset + 2] =
                (byte)((pixels[offset + 2] * alpha + 127) / 255);
        }
    }
}

internal sealed class NativeAtlasTextureLoader : TextureLoader
{
    private readonly Dictionary<string, NativeTextureSource> _textures =
        new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<NativeTextureSource> Textures => _textures.Values;

    public void Load(AtlasPage page, string path)
    {
        string fullPath = System.IO.Path.GetFullPath(path);
        NativeTextureSource texture = NativeTextureSource.Load(
            fullPath,
            page.pma);
        page.width = texture.Width;
        page.height = texture.Height;
        page.rendererObject = texture;
        _textures[fullPath] = texture;
    }

    public void Unload(object texture)
    {
        if (texture is NativeTextureSource source)
            _textures.Remove(source.Path);
    }
}
