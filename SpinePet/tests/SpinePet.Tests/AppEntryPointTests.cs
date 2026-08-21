using System.Reflection;
using SpinePet;

namespace SpinePet.Tests;

public sealed class AppEntryPointTests
{
    [Fact]
    public void AssemblyEntryPointEntersThroughSpinePetProgram()
    {
        MethodInfo? entryPoint = typeof(App).Assembly.EntryPoint;

        Assert.NotNull(entryPoint);
        Assert.Equal(
            "SpinePet.Program",
            entryPoint!.DeclaringType!.FullName);
    }
}
