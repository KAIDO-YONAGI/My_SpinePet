using Spine;

namespace SpinePet.Rendering.Native;

internal readonly record struct NativeSpineBounds(
    float Left,
    float Top,
    float Right,
    float Bottom)
{
    public float Width => Math.Max(1, Right - Left);
    public float Height => Math.Max(1, Bottom - Top);
    public bool IsEmpty =>
        !float.IsFinite(Left) ||
        !float.IsFinite(Top) ||
        !float.IsFinite(Right) ||
        !float.IsFinite(Bottom) ||
        Right <= Left ||
        Bottom <= Top;

    public NativeSpineBounds Union(NativeSpineBounds other)
    {
        if (IsEmpty)
            return other;
        if (other.IsEmpty)
            return this;

        return new NativeSpineBounds(
            Math.Min(Left, other.Left),
            Math.Min(Top, other.Top),
            Math.Max(Right, other.Right),
            Math.Max(Bottom, other.Bottom));
    }

    public NativeSpineBounds Expand(
        float horizontal,
        float top,
        float bottom)
    {
        return new NativeSpineBounds(
            Left - horizontal,
            Top - top,
            Right + horizontal,
            Bottom + bottom);
    }

    public static NativeSpineBounds Empty =>
        new(float.PositiveInfinity, float.PositiveInfinity,
            float.NegativeInfinity, float.NegativeInfinity);
}

internal static class NativeSpineEnvelopeCalculator
{
    private const float SamplesPerSecond = 12;
    private const int MaximumSamplesPerAnimation = 48;

    public static (NativeSpineBounds Setup, NativeSpineBounds Envelope) Calculate(
        SkeletonData data)
    {
        NativeSpineGeometry geometry = new();
        Skeleton setupSkeleton = new(data);
        setupSkeleton.SetToSetupPose();
        setupSkeleton.UpdateWorldTransform();
        NativeSpineBounds setup = GetBounds(geometry.Build(setupSkeleton));
        if (setup.IsEmpty)
        {
            setup = new NativeSpineBounds(
                data.X,
                data.Y,
                data.X + Math.Max(1, data.Width),
                data.Y + Math.Max(1, data.Height));
        }

        NativeSpineBounds envelope = setup;
        Animation[] animations = data.Animations.Items;
        for (int animationIndex = 0;
             animationIndex < data.Animations.Count;
             animationIndex++)
        {
            Animation animation = animations[animationIndex];
            Skeleton probe = new(data);
            AnimationState state = new(new AnimationStateData(data));
            state.SetAnimation(0, animation, false);

            int sampleCount = Math.Clamp(
                (int)Math.Ceiling(animation.Duration * SamplesPerSecond),
                2,
                MaximumSamplesPerAnimation);
            float previousTime = 0;
            for (int sampleIndex = 0;
                 sampleIndex <= sampleCount;
                 sampleIndex++)
            {
                float sampleTime =
                    animation.Duration * sampleIndex / sampleCount;
                state.Update(Math.Max(0, sampleTime - previousTime));
                state.Apply(probe);
                probe.UpdateWorldTransform();
                previousTime = sampleTime;
                envelope = envelope.Union(
                    GetBounds(geometry.Build(probe)));
            }
        }

        float horizontalPadding = Math.Max(48, envelope.Width * 0.12f);
        float topPadding = Math.Max(48, envelope.Height * 0.12f);
        float bottomPadding = Math.Max(128, envelope.Height * 0.35f);
        return (
            setup,
            envelope.Expand(
                horizontalPadding,
                topPadding,
                bottomPadding));
    }

    public static NativeSpineBounds GetBounds(
        IReadOnlyList<NativeSpineDrawBatch> batches)
    {
        NativeSpineBounds bounds = NativeSpineBounds.Empty;
        foreach (NativeSpineDrawBatch batch in batches)
        {
            for (int index = 0; index < batch.VertexCount; index++)
            {
                NativeSpineVertex vertex = batch.Vertices[index];
                bounds = bounds.Union(
                    new NativeSpineBounds(
                        vertex.Position.X,
                        vertex.Position.Y,
                        vertex.Position.X + 0.001f,
                        vertex.Position.Y + 0.001f));
            }
        }

        return bounds;
    }
}
