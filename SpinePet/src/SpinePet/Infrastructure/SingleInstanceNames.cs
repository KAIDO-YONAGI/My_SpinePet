using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SpinePet.Infrastructure;

internal sealed record SingleInstanceNames(
    string MutexName,
    string ActivationEventName);

internal static class SingleInstanceNameFactory
{
    private const int ScopeHashBytes = 12;

    public static SingleInstanceNames Create(string executableDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executableDirectory);

        string normalizedDirectory = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(executableDirectory))
            .ToUpperInvariant();
        byte[] directoryHash = SHA256.HashData(
            Encoding.UTF8.GetBytes(normalizedDirectory));
        string scope = Convert.ToHexString(
            directoryHash.AsSpan(0, ScopeHashBytes));

        return new SingleInstanceNames(
            $@"Local\SpinePet.SingleInstance.v2.{scope}",
            $@"Local\SpinePet.Activate.v2.{scope}");
    }
}
