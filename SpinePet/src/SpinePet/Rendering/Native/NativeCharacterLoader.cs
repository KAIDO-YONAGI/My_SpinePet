using SpinePet.Models;

namespace SpinePet.Rendering.Native;

internal static class NativeCharacterLoader
{
    private const int MaxConcurrentLoads = 2;
    private static readonly SemaphoreSlim LoadGate = new(
        MaxConcurrentLoads,
        MaxConcurrentLoads);

    public static async Task<NativeCharacterLoadResult> LoadAsync(
        CharacterConfig character,
        CancellationToken cancellationToken)
    {
        await LoadGate.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                NativeSpineResource resource =
                    NativeSpineResource.Load(character);
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var bounds = NativeSpineEnvelopeCalculator.Calculate(
                        resource.SkeletonData);
                    cancellationToken.ThrowIfCancellationRequested();
                    return new NativeCharacterLoadResult(
                        resource,
                        bounds.Setup,
                        bounds.Envelope);
                }
                catch
                {
                    resource.Dispose();
                    throw;
                }
            }, cancellationToken);
        }
        finally
        {
            LoadGate.Release();
        }
    }
}

internal readonly record struct NativeCharacterLoadResult(
    NativeSpineResource Resource,
    NativeSpineBounds Setup,
    NativeSpineBounds Envelope);
