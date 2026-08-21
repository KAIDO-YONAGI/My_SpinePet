using System.IO;
using SpinePet.Infrastructure;

namespace SpinePet.Tests;

public sealed class SingleInstanceNameFactoryTests
{
    [Fact]
    public void SameDirectoryUsesSameNamesAcrossPathFormatting()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "SpinePet",
            "Release");
        string alternatePath =
            directory.ToLowerInvariant() + Path.DirectorySeparatorChar;

        SingleInstanceNames expected =
            SingleInstanceNameFactory.Create(directory);
        SingleInstanceNames actual =
            SingleInstanceNameFactory.Create(alternatePath);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void DifferentDirectoriesUseDifferentNames()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "SpinePet",
            Guid.NewGuid().ToString("N"));

        SingleInstanceNames first =
            SingleInstanceNameFactory.Create(Path.Combine(root, "Release-A"));
        SingleInstanceNames second =
            SingleInstanceNameFactory.Create(Path.Combine(root, "Release-B"));

        Assert.NotEqual(first.MutexName, second.MutexName);
        Assert.NotEqual(
            first.ActivationEventName,
            second.ActivationEventName);
    }

    [Fact]
    public void NamesUseMatchingVersionedPathScope()
    {
        SingleInstanceNames names =
            SingleInstanceNameFactory.Create(AppContext.BaseDirectory);

        string mutexScope = names.MutexName.Split('.')[^1];
        string activationScope =
            names.ActivationEventName.Split('.')[^1];

        Assert.StartsWith(
            @"Local\SpinePet.SingleInstance.v2.",
            names.MutexName);
        Assert.StartsWith(
            @"Local\SpinePet.Activate.v2.",
            names.ActivationEventName);
        Assert.Equal(mutexScope, activationScope);
        Assert.Equal(24, mutexScope.Length);
    }
}
