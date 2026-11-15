// 测量不同 DPI 感知模式下的坐标返回值
using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal static class DpiProbe
{
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int i);
    [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr v);
    [DllImport("user32.dll")] static extern IntPtr GetThreadDpiAwarenessContext();
    [DllImport("user32.dll")] static extern int GetAwarenessFromDpiAwarenessContext(IntPtr v);
    [DllImport("user32.dll")] static extern uint GetDpiForSystem();

    static void Report(string tag)
    {
        Console.WriteLine("--- " + tag);
        Console.WriteLine("  GetSystemMetrics(0,1)  = " + GetSystemMetrics(0) + " x " + GetSystemMetrics(1));
        Console.WriteLine("  SM_CXVIRTUALSCREEN     = " + GetSystemMetrics(78) + " x " + GetSystemMetrics(79));
        Console.WriteLine("  Screen.PrimaryScreen   = " + Screen.PrimaryScreen.Bounds);
        Console.WriteLine("  VirtualScreen          = " + SystemInformation.VirtualScreen);
        try
        {
            Console.WriteLine("  GetDpiForSystem()      = " + GetDpiForSystem());
            Console.WriteLine("  awareness              = " + GetAwarenessFromDpiAwarenessContext(GetThreadDpiAwarenessContext()));
        }
        catch (Exception e) { Console.WriteLine("  dpi api: " + e.Message); }
        Console.WriteLine("  WorkingArea            = " + Screen.PrimaryScreen.WorkingArea);
        Console.WriteLine();
    }

    public static void Main()
    {
        Console.WriteLine("=== 未设置 DPI 之前 ===");
        Report("default");

        if (Environment.GetEnvironmentVariable("MODE") == "sysaware")
        {
            SetProcessDpiAwarenessContext(new IntPtr(-2));
            Console.WriteLine("=== 已设 System DPI Aware ===");
            Report("system-aware");
        }
    }
}
