// 检查 TouchKeys 窗口的扩展样式，确认 NOACTIVATE 真的生效
using System;
using System.Runtime.InteropServices;
using System.Text;

internal static class StyleCheck
{
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);

    static IntPtr _found = IntPtr.Zero;

    public static void Main()
    {
        var procs = System.Diagnostics.Process.GetProcessesByName("TouchKeys");
        if (procs.Length == 0) { Console.WriteLine("TouchKeys 未运行"); return; }
        uint pid0 = (uint)procs[0].Id;

        EnumWindows((h, l) =>
        {
            uint pid;
            GetWindowThreadProcessId(h, out pid);
            if (pid != pid0 || !IsWindowVisible(h)) return true;
            int ex = GetWindowLong(h, -20);
            if ((ex & 0x80000) != 0) { _found = h; return false; }
            return true;
        }, IntPtr.Zero);

        if (_found == IntPtr.Zero) { Console.WriteLine("没找到分层窗口"); return; }

        int ex2 = GetWindowLong(_found, -20);
        StringBuilder cls = new StringBuilder(256);
        GetClassNameW(_found, cls, 256);

        Console.WriteLine("=== 窗口样式 ===");
        Console.WriteLine("句柄          : 0x" + _found.ToString("X"));
        Console.WriteLine("类名          : " + cls);
        Console.WriteLine("EXSTYLE       : 0x" + ex2.ToString("X8"));
        Console.WriteLine();
        Console.WriteLine("LAYERED       : " + ((ex2 & 0x00080000) != 0));
        Console.WriteLine("NOACTIVATE    : " + ((ex2 & 0x08000000) != 0) + "   <- 必须 True");
        Console.WriteLine("TOPMOST       : " + ((ex2 & 0x00000008) != 0));
        Console.WriteLine("TRANSPARENT   : " + ((ex2 & 0x00000020) != 0) + "   <- 必须 False");
        Console.WriteLine("TOOLWINDOW    : " + ((ex2 & 0x00000080) != 0));

        IntPtr fg = GetForegroundWindow();
        Console.WriteLine();
        Console.WriteLine("当前前台窗口  : 0x" + fg.ToString("X") +
            (fg == _found ? "  <== 是 TouchKeys（不该发生）" : "  （不是 TouchKeys，正常）"));

        bool ok = ((ex2 & 0x08000000) != 0) && ((ex2 & 0x00000020) == 0) && fg != _found;
        Console.WriteLine();
        Console.WriteLine(ok ? "★ 样式正确：不会抢焦点" : "✗ 样式有问题");
    }
}
