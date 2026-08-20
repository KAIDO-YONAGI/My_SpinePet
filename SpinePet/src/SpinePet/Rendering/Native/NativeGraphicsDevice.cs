using System.Numerics;
using System.Runtime.InteropServices;
using Spine;
using SpinePet.Infrastructure;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DirectComposition;
using Vortice.DXGI;
using Vortice.Mathematics;
using static Vortice.Direct3D11.D3D11;
using static Vortice.DXGI.DXGI;
using D3DMapFlags = Vortice.Direct3D11.MapFlags;
using DxgiFormat = Vortice.DXGI.Format;

namespace SpinePet.Rendering.Native;

internal sealed class NativeGraphicsDevice : IDisposable
{
    private const int VertexStride = 48;

    private const string ShaderSource = """
        cbuffer TransformBuffer : register(b0)
        {
            float4 Transform;
        };

        struct VertexInput
        {
            float2 Position : POSITION;
            float2 TextureCoordinate : TEXCOORD0;
            float4 LightColor : COLOR0;
            float4 DarkColor : COLOR1;
        };

        struct PixelInput
        {
            float4 Position : SV_POSITION;
            float2 TextureCoordinate : TEXCOORD0;
            float4 LightColor : COLOR0;
            float4 DarkColor : COLOR1;
        };

        PixelInput VertexMain(VertexInput input)
        {
            PixelInput output;
            output.Position = float4(
                input.Position.x * Transform.x + Transform.z,
                input.Position.y * Transform.y + Transform.w,
                0.0,
                1.0);
            output.TextureCoordinate = input.TextureCoordinate;
            output.LightColor = input.LightColor;
            output.DarkColor = input.DarkColor;
            return output;
        }

        Texture2D CharacterTexture : register(t0);
        SamplerState CharacterSampler : register(s0);

        float4 PixelMain(PixelInput input) : SV_TARGET
        {
            float4 sampled = CharacterTexture.Sample(
                CharacterSampler,
                input.TextureCoordinate);

            float3 color =
                sampled.rgb * input.LightColor.rgb +
                (sampled.a - sampled.rgb) *
                    input.DarkColor.rgb *
                    input.DarkColor.a;
            color *= input.LightColor.a;
            return float4(
                color,
                sampled.a * input.LightColor.a);
        }
        """;

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct ShaderConstants
    {
        public ShaderConstants(Vector4 transform)
        {
            Transform = transform;
        }

        public readonly Vector4 Transform;
    }

    private sealed class GpuTexture : IDisposable
    {
        public required ID3D11Texture2D Texture { get; init; }
        public required ID3D11ShaderResourceView View { get; init; }

        public void Dispose()
        {
            View.Dispose();
            Texture.Dispose();
        }
    }

    private readonly Dictionary<string, GpuTexture> _textures =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly IDXGIFactory2 _factory;
    private readonly IDXGIDevice _dxgiDevice;
    private readonly IDCompositionDevice _compositionDevice;
    private readonly IDCompositionTarget _compositionTarget;
    private readonly IDCompositionVisual _rootVisual;
    private readonly ID3D11VertexShader _vertexShader;
    private readonly ID3D11PixelShader _pixelShader;
    private readonly ID3D11InputLayout _inputLayout;
    private readonly ID3D11SamplerState _sampler;
    private readonly ID3D11RasterizerState _rasterizerState;
    private readonly ID3D11DepthStencilState _depthStencilState;
    private readonly ID3D11BlendState _normalBlend;
    private readonly ID3D11BlendState _additiveBlend;
    private readonly ID3D11BlendState _multiplyBlend;
    private readonly ID3D11BlendState _screenBlend;
    private readonly ID3D11Buffer _constantBuffer;
    private ID3D11Buffer? _vertexBuffer;
    private ID3D11Buffer? _indexBuffer;
    private int _vertexCapacity;
    private int _indexCapacity;
    private bool _disposed;

    public NativeGraphicsDevice(IntPtr windowHandle)
    {
        _device = D3D11CreateDevice(
            DriverType.Hardware,
            DeviceCreationFlags.BgraSupport,
            [
                FeatureLevel.Level_11_1,
                FeatureLevel.Level_11_0,
                FeatureLevel.Level_10_1,
                FeatureLevel.Level_10_0
            ]);
        AppLogger.Write(nameof(NativeGraphicsDevice), "device-created");
        _context = _device.ImmediateContext;
        _dxgiDevice = _device.QueryInterface<IDXGIDevice>();
        _dxgiDevice.GetAdapter(out IDXGIAdapter adapter).CheckError();
        using (adapter)
        {
            AdapterDescription description = adapter.Description;
            AppLogger.Write(
                nameof(NativeGraphicsDevice),
                $"adapter={description.Description} " +
                $"dedicated-video-mb={description.DedicatedVideoMemory / 1024 / 1024} " +
                $"shared-system-mb={description.SharedSystemMemory / 1024 / 1024}");
        }
        _factory = CreateDXGIFactory2<IDXGIFactory2>(debug: false);
        AppLogger.Write(nameof(NativeGraphicsDevice), "dxgi-created");
        _compositionDevice =
            DComp.DCompositionCreateDevice<IDCompositionDevice>(_dxgiDevice);
        AppLogger.Write(nameof(NativeGraphicsDevice), "composition-device-created");
        _compositionTarget = CreateCompositionTarget(windowHandle);
        _rootVisual = _compositionDevice.CreateVisual();
        _compositionTarget.SetRoot(_rootVisual).CheckError();
        AppLogger.Write(nameof(NativeGraphicsDevice), "composition-target-created");

        ReadOnlyMemory<byte> vertexBytecode = Compiler.Compile(
            ShaderSource,
            "VertexMain",
            "spine-native.hlsl",
            "vs_5_0",
            ShaderFlags.OptimizationLevel3,
            EffectFlags.None);
        ReadOnlyMemory<byte> pixelBytecode = Compiler.Compile(
            ShaderSource,
            "PixelMain",
            "spine-native.hlsl",
            "ps_5_0",
            ShaderFlags.OptimizationLevel3,
            EffectFlags.None);
        AppLogger.Write(nameof(NativeGraphicsDevice), "shaders-compiled");
        _vertexShader = _device.CreateVertexShader(
            vertexBytecode.Span,
            null);
        _pixelShader = _device.CreatePixelShader(
            pixelBytecode.Span,
            null);
        AppLogger.Write(nameof(NativeGraphicsDevice), "shader-objects-created");
        _inputLayout = _device.CreateInputLayout(
            [
                new InputElementDescription(
                    "POSITION",
                    0,
                    DxgiFormat.R32G32_Float,
                    0,
                    0),
                new InputElementDescription(
                    "TEXCOORD",
                    0,
                    DxgiFormat.R32G32_Float,
                    8,
                    0),
                new InputElementDescription(
                    "COLOR",
                    0,
                    DxgiFormat.R32G32B32A32_Float,
                    16,
                    0),
                new InputElementDescription(
                    "COLOR",
                    1,
                    DxgiFormat.R32G32B32A32_Float,
                    32,
                    0)
            ],
            vertexBytecode.Span);
        AppLogger.Write(nameof(NativeGraphicsDevice), "shader-pipeline-created");
        _sampler = _device.CreateSamplerState(
            new SamplerDescription(
                Filter.MinMagMipLinear,
                TextureAddressMode.Clamp,
                0,
                1,
                ComparisonFunction.Never,
                0,
                float.MaxValue));
        _rasterizerState = _device.CreateRasterizerState(
            RasterizerDescription.CullNone);
        _depthStencilState = _device.CreateDepthStencilState(
            DepthStencilDescription.None);
        _normalBlend = _device.CreateBlendState(
            new BlendDescription(
                Blend.One,
                Blend.InverseSourceAlpha));
        _additiveBlend = _device.CreateBlendState(
            new BlendDescription(Blend.One, Blend.One));
        _multiplyBlend = _device.CreateBlendState(
            new BlendDescription(
                Blend.DestinationColor,
                Blend.InverseSourceAlpha,
                Blend.One,
                Blend.InverseSourceAlpha));
        _screenBlend = _device.CreateBlendState(
            new BlendDescription(
                Blend.One,
                Blend.InverseSourceColor,
                Blend.One,
                Blend.InverseSourceAlpha));
        _constantBuffer = _device.CreateConstantBuffer<ShaderConstants>();
        _compositionDevice.Commit().CheckError();
        AppLogger.Write(nameof(NativeGraphicsDevice), "pipeline-created");
    }

    public NativeCompositionSurface CreateSurface() =>
        new(this, _compositionDevice.CreateVisual());

    public void Commit()
    {
        _compositionDevice.Commit().CheckError();
    }

    internal IDXGISwapChain1 CreateSwapChain(int width, int height)
    {
        SwapChainDescription1 description = new(
            checked((uint)width),
            checked((uint)height),
            DxgiFormat.B8G8R8A8_UNorm,
            stereo: false,
            Usage.RenderTargetOutput,
            bufferCount: 2,
            Scaling.Stretch,
            SwapEffect.FlipSequential,
            AlphaMode.Premultiplied,
            SwapChainFlags.None);
        return _factory.CreateSwapChainForComposition(
            _device,
            description,
            null);
    }

    internal void AddVisual(IDCompositionVisual visual)
    {
        // With a null reference visual, false places the new visual above
        // every existing sibling. The API's insertAbove naming is inverted
        // for this special case.
        _rootVisual.AddVisual(visual, insertAbove: false, referenceVisual: null)
            .CheckError();
    }

    internal void RemoveVisual(IDCompositionVisual visual)
    {
        _rootVisual.RemoveVisual(visual).CheckError();
    }

    internal void Render(
        NativeCompositionSurface surface,
        IReadOnlyList<NativeSpineDrawBatch> batches,
        Vector4 transform)
    {
        ID3D11RenderTargetView target = surface.RenderTarget ??
            throw new InvalidOperationException("Surface has no render target.");
        _context.OMSetRenderTargets(target, null);
        _context.ClearRenderTargetView(
            target,
            new Color4(0, 0, 0, 0));
        _context.RSSetViewport(
            0,
            0,
            surface.PixelWidth,
            surface.PixelHeight);
        _context.RSSetState(_rasterizerState);
        _context.OMSetDepthStencilState(_depthStencilState, 0);
        _context.IASetInputLayout(_inputLayout);
        _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _context.VSSetShader(_vertexShader);
        _context.VSSetConstantBuffer(0, _constantBuffer);
        _context.PSSetShader(_pixelShader);
        _context.PSSetSampler(0, _sampler);

        foreach (NativeSpineDrawBatch batch in batches)
        {
            EnsureDynamicBuffers(
                batch.VertexCount,
                batch.IndexCount);
            UploadSpan<NativeSpineVertex>(
                _vertexBuffer!,
                batch.Vertices.AsSpan(0, batch.VertexCount));
            UploadSpan<int>(
                _indexBuffer!,
                batch.Indices.AsSpan(0, batch.IndexCount));

            ShaderConstants constants = new(transform);
            UploadValue(_constantBuffer, constants);
            GpuTexture texture = GetOrCreateTexture(batch.Texture);
            _context.IASetVertexBuffer(
                0,
                _vertexBuffer!,
                VertexStride,
                0);
            _context.IASetIndexBuffer(
                _indexBuffer,
                DxgiFormat.R32_UInt,
                0);
            _context.PSSetShaderResource(0, texture.View);
            _context.OMSetBlendState(GetBlendState(batch.BlendMode));
            _context.DrawIndexed(
                checked((uint)batch.IndexCount),
                0,
                0);
        }

        _context.PSSetShaderResource(0, null!);
        _context.OMSetRenderTargets(
            Array.Empty<ID3D11RenderTargetView>(),
            null);
        surface.Present();
    }

    internal ID3D11RenderTargetView CreateRenderTarget(
        IDXGISwapChain1 swapChain,
        out ID3D11Texture2D backBuffer)
    {
        backBuffer = swapChain.GetBuffer<ID3D11Texture2D>(0);
        return _device.CreateRenderTargetView(backBuffer, null);
    }

    private IDCompositionTarget CreateCompositionTarget(IntPtr windowHandle)
    {
        _compositionDevice.CreateTargetForHwnd(
            windowHandle,
            topmost: true,
            out IDCompositionTarget target).CheckError();
        return target;
    }

    private GpuTexture GetOrCreateTexture(NativeTextureSource source)
    {
        if (_textures.TryGetValue(source.Path, out GpuTexture? existing))
            return existing;

        Texture2DDescription description = new(
            DxgiFormat.B8G8R8A8_UNorm,
            checked((uint)source.Width),
            checked((uint)source.Height),
            1,
            0,
            BindFlags.ShaderResource | BindFlags.RenderTarget,
            ResourceUsage.Default,
            CpuAccessFlags.None,
            1,
            0,
            ResourceOptionFlags.GenerateMips);
        ID3D11Texture2D texture = _device.CreateTexture2D(
            description);
        byte[] pixels = source.CopyBgraPixels();
        _context.UpdateSubresource(
            pixels,
            texture,
            0,
            checked((uint)(source.Width * 4)),
            0);
        GpuTexture gpuTexture = new()
        {
            Texture = texture,
            View = _device.CreateShaderResourceView(texture, null)
        };
        _context.GenerateMips(gpuTexture.View);
        _textures[source.Path] = gpuTexture;
        return gpuTexture;
    }

    public void PurgeTextures(IReadOnlySet<string> activePaths)
    {
        foreach (string path in _textures.Keys
                     .Where(path => !activePaths.Contains(path))
                     .ToArray())
        {
            _textures[path].Dispose();
            _textures.Remove(path);
        }
    }

    private ID3D11BlendState GetBlendState(BlendMode blendMode) =>
        blendMode switch
        {
            BlendMode.Additive => _additiveBlend,
            BlendMode.Multiply => _multiplyBlend,
            BlendMode.Screen => _screenBlend,
            _ => _normalBlend
        };

    private void EnsureDynamicBuffers(int vertexCount, int indexCount)
    {
        if (vertexCount > _vertexCapacity)
        {
            _vertexBuffer?.Dispose();
            _vertexCapacity = GetCapacity(vertexCount);
            _vertexBuffer = _device.CreateBuffer(
                checked((uint)(_vertexCapacity * VertexStride)),
                BindFlags.VertexBuffer,
                ResourceUsage.Dynamic,
                CpuAccessFlags.Write,
                ResourceOptionFlags.None,
                0);
        }

        if (indexCount > _indexCapacity)
        {
            _indexBuffer?.Dispose();
            _indexCapacity = GetCapacity(indexCount);
            _indexBuffer = _device.CreateBuffer(
                checked((uint)(_indexCapacity * sizeof(int))),
                BindFlags.IndexBuffer,
                ResourceUsage.Dynamic,
                CpuAccessFlags.Write,
                ResourceOptionFlags.None,
                0);
        }
    }

    private unsafe void UploadSpan<T>(
        ID3D11Buffer buffer,
        ReadOnlySpan<T> data)
        where T : unmanaged
    {
        MappedSubresource mapped = _context.Map(
            buffer,
            MapMode.WriteDiscard,
            D3DMapFlags.None);
        data.CopyTo(
            new Span<T>(
                mapped.DataPointer.ToPointer(),
                data.Length));
        _context.Unmap(buffer);
    }

    private void UploadValue<T>(ID3D11Buffer buffer, T value)
        where T : unmanaged
    {
        UploadSpan(
            buffer,
            MemoryMarshal.CreateReadOnlySpan(ref value, 1));
    }

    private static int GetCapacity(int required)
    {
        int capacity = 256;
        while (capacity < required)
            capacity = checked(capacity * 2);
        return capacity;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        _context.ClearState();
        foreach (GpuTexture texture in _textures.Values)
            texture.Dispose();
        _textures.Clear();
        _indexBuffer?.Dispose();
        _vertexBuffer?.Dispose();
        _constantBuffer.Dispose();
        _screenBlend.Dispose();
        _multiplyBlend.Dispose();
        _additiveBlend.Dispose();
        _normalBlend.Dispose();
        _depthStencilState.Dispose();
        _rasterizerState.Dispose();
        _sampler.Dispose();
        _inputLayout.Dispose();
        _pixelShader.Dispose();
        _vertexShader.Dispose();
        _rootVisual.Dispose();
        _compositionTarget.Dispose();
        _compositionDevice.Dispose();
        _factory.Dispose();
        _dxgiDevice.Dispose();
        _context.Dispose();
        _device.Dispose();
    }
}
