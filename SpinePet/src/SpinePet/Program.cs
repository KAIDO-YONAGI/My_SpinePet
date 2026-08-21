using System.Windows;

namespace SpinePet;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        App app = new();
        app.Run();
    }
}
