using System.Numerics;
using System.Runtime.InteropServices;
using Spine;

namespace SpinePet.Rendering.Native;

[StructLayout(LayoutKind.Sequential)]
internal readonly struct NativeSpineVertex
{
    public NativeSpineVertex(
        Vector2 position,
        Vector2 textureCoordinate,
        Vector4 lightColor,
        Vector4 darkColor)
    {
        Position = position;
        TextureCoordinate = textureCoordinate;
        LightColor = lightColor;
        DarkColor = darkColor;
    }

    public readonly Vector2 Position;
    public readonly Vector2 TextureCoordinate;
    public readonly Vector4 LightColor;
    public readonly Vector4 DarkColor;
}

internal sealed class NativeSpineDrawBatch
{
    private NativeSpineVertex[] _vertices = [];
    private int[] _indices = [];

    public NativeTextureSource Texture { get; private set; } = null!;
    public BlendMode BlendMode { get; private set; }
    public NativeSpineVertex[] Vertices => _vertices;
    public int[] Indices => _indices;
    public int VertexCount { get; private set; }
    public int IndexCount { get; private set; }

    public bool Matches(
        NativeTextureSource texture,
        BlendMode blendMode) =>
        ReferenceEquals(Texture, texture) && BlendMode == blendMode;

    public void Reset(
        NativeTextureSource texture,
        BlendMode blendMode)
    {
        Texture = texture;
        BlendMode = blendMode;
        VertexCount = 0;
        IndexCount = 0;
    }

    public int AppendVertices(
        float[] positions,
        float[] uvs,
        int positionsLength,
        Vector4 light,
        Vector4 dark)
    {
        int sourceVertexCount = positionsLength / 2;
        int vertexOffset = VertexCount;
        EnsureVertexCapacity(vertexOffset + sourceVertexCount);
        for (int index = 0; index < sourceVertexCount; index++)
        {
            int source = index * 2;
            _vertices[vertexOffset + index] = new NativeSpineVertex(
                new Vector2(positions[source], positions[source + 1]),
                new Vector2(uvs[source], uvs[source + 1]),
                light,
                dark);
        }

        VertexCount += sourceVertexCount;
        return vertexOffset;
    }

    public void AppendIndices(
        int[] indices,
        int indexCount,
        int vertexOffset)
    {
        EnsureIndexCapacity(IndexCount + indexCount);
        for (int index = 0; index < indexCount; index++)
        {
            _indices[IndexCount + index] = indices[index] + vertexOffset;
        }

        IndexCount += indexCount;
    }

    private void EnsureVertexCapacity(int requiredCapacity)
    {
        if (_vertices.Length >= requiredCapacity)
        {
            return;
        }

        Array.Resize(
            ref _vertices,
            GetExpandedCapacity(_vertices.Length, requiredCapacity));
    }

    private void EnsureIndexCapacity(int requiredCapacity)
    {
        if (_indices.Length >= requiredCapacity)
        {
            return;
        }

        Array.Resize(
            ref _indices,
            GetExpandedCapacity(_indices.Length, requiredCapacity));
    }

    private static int GetExpandedCapacity(
        int currentCapacity,
        int requiredCapacity)
    {
        long doubled = Math.Max(16L, (long)currentCapacity * 2);
        return checked((int)Math.Max(requiredCapacity, doubled));
    }
}

internal sealed class NativeSpineGeometry
{
    private static readonly int[] QuadTriangles = [0, 1, 2, 2, 3, 0];

    private readonly SkeletonClipping _clipper = new();
    private readonly List<NativeSpineDrawBatch> _activeBatches = [];
    private readonly List<NativeSpineDrawBatch> _batchPool = [];
    private float[] _worldVertices = new float[8];

    public IReadOnlyList<NativeSpineDrawBatch> Build(Skeleton skeleton)
    {
        _activeBatches.Clear();
        Slot[] slots = skeleton.DrawOrder.Items;

        for (int slotIndex = 0; slotIndex < skeleton.DrawOrder.Count; slotIndex++)
        {
            Slot slot = slots[slotIndex];
            Attachment? attachment = slot.Attachment;
            if (attachment is ClippingAttachment clip)
            {
                _clipper.ClipStart(slot, clip);
                continue;
            }

            float[]? vertices = null;
            float[]? uvs = null;
            int[]? triangles = null;
            AtlasRegion? region = null;
            float attachmentR = 1;
            float attachmentG = 1;
            float attachmentB = 1;
            float attachmentA = 1;

            if (attachment is RegionAttachment regionAttachment)
            {
                EnsureWorldVertices(8);
                regionAttachment.ComputeWorldVertices(slot, _worldVertices, 0);
                vertices = _worldVertices;
                uvs = regionAttachment.UVs;
                triangles = QuadTriangles;
                region = regionAttachment.Region as AtlasRegion;
                attachmentR = regionAttachment.R;
                attachmentG = regionAttachment.G;
                attachmentB = regionAttachment.B;
                attachmentA = regionAttachment.A;
            }
            else if (attachment is MeshAttachment meshAttachment)
            {
                EnsureWorldVertices(meshAttachment.WorldVerticesLength);
                meshAttachment.ComputeWorldVertices(
                    slot,
                    0,
                    meshAttachment.WorldVerticesLength,
                    _worldVertices,
                    0);
                vertices = _worldVertices;
                uvs = meshAttachment.UVs;
                triangles = meshAttachment.Triangles;
                region = meshAttachment.Region as AtlasRegion;
                attachmentR = meshAttachment.R;
                attachmentG = meshAttachment.G;
                attachmentB = meshAttachment.B;
                attachmentA = meshAttachment.A;
            }

            if (vertices == null ||
                uvs == null ||
                triangles == null ||
                region?.page.rendererObject is not NativeTextureSource texture)
            {
                _clipper.ClipEnd(slot);
                continue;
            }

            int verticesLength = attachment is MeshAttachment mesh
                ? mesh.WorldVerticesLength
                : 8;
            if (_clipper.IsClipping)
            {
                _clipper.ClipTriangles(
                    vertices,
                    verticesLength,
                    triangles,
                    triangles.Length,
                    uvs);
                vertices = _clipper.ClippedVertices.Items;
                uvs = _clipper.ClippedUVs.Items;
                triangles = _clipper.ClippedTriangles.Items;
                verticesLength = _clipper.ClippedVertices.Count;
            }

            Vector4 light = new(
                skeleton.R * slot.R * attachmentR,
                skeleton.G * slot.G * attachmentG,
                skeleton.B * slot.B * attachmentB,
                skeleton.A * slot.A * attachmentA);
            Vector4 dark = slot.HasSecondColor
                ? new Vector4(slot.R2, slot.G2, slot.B2, 1)
                : Vector4.Zero;

            int indexCount = _clipper.IsClipping
                ? _clipper.ClippedTriangles.Count
                : triangles.Length;
            if (verticesLength == 0 || indexCount == 0)
            {
                _clipper.ClipEnd(slot);
                continue;
            }

            NativeSpineDrawBatch batch = GetOrCreateBatch(
                texture,
                slot.Data.BlendMode);
            int vertexOffset = batch.AppendVertices(
                vertices,
                uvs,
                verticesLength,
                light,
                dark);
            batch.AppendIndices(triangles, indexCount, vertexOffset);
            _clipper.ClipEnd(slot);
        }

        _clipper.ClipEnd();
        return _activeBatches;
    }

    private void EnsureWorldVertices(int length)
    {
        if (_worldVertices.Length < length)
            _worldVertices = new float[length];
    }

    private NativeSpineDrawBatch GetOrCreateBatch(
        NativeTextureSource texture,
        BlendMode blendMode)
    {
        if (_activeBatches.Count > 0)
        {
            NativeSpineDrawBatch current = _activeBatches[^1];
            if (current.Matches(texture, blendMode))
            {
                return current;
            }
        }

        int batchIndex = _activeBatches.Count;
        if (batchIndex == _batchPool.Count)
        {
            _batchPool.Add(new NativeSpineDrawBatch());
        }

        NativeSpineDrawBatch batch = _batchPool[batchIndex];
        batch.Reset(texture, blendMode);
        _activeBatches.Add(batch);
        return batch;
    }
}
