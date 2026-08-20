using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SpinePet.Tests;

internal static class ProcessDpiInitialization
{
    private static readonly IntPtr PerMonitorAwareV2 = new(-4);

    [ModuleInitializer]
    internal static void EnsureProcessIsDpiAware()
    {
        // 桌宠本体以 DPI 感知模式运行（窗口/区域坐标为物理像素）；
        // 测试宿主没有清单，运行中 WPF 初始化会中途改变 DPI 虚拟化状态，
        // 导致原生窗口区域坐标按显示器缩放（如 1.75）被系统改写，
        // 断言结果依赖测试执行顺序。进程加载即固定为 Per-Monitor V2，
        // 与被测应用保持一致；已设置时静默失败，不影响其余测试。
        if (SetProcessDpiAwarenessContext(PerMonitorAwareV2))
            return;
        SetProcessDPIAware();
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessDpiAwarenessContext(
        IntPtr value);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessDPIAware();
}
