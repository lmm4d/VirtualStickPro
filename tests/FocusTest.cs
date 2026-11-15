// 验证不抢焦点：记录前台窗口，按住摇杆拖动，再看前台窗口有没有变
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

internal static class FocusTest
{
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int i);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);

    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Sequential)]
    struct INPUT { public uint type; public MOUSEINPUT mi; }
    [DllImport("user32.dll")] static extern uint SendInput(uint n, INPUT[] p, int cb);

    const uint LEFTDOWN = 0x0002, LEFTUP = 0x0004;
    const uint ABSOLUTE = 0x8000, MOVE = 0x0001;

    static void Click(bool down, int x, int y)
    {
        int sw = GetSystemMetrics(0), sh = GetSystemMetrics(1);
        INPUT i = new INPUT();
        i.type = 0;
        i.mi.dx = (int)((long)x * 65535 / Math.Max(1, sw - 1));
        i.mi.dy = (int)((long)y * 65535 / Math.Max(1, sh - 1));
        i.mi.dwFlags = ABSOLUTE | MOVE | (down ? LEFTDOWN : LEFTUP);
        SendInput(1, new INPUT[] { i }, Marshal.SizeOf(typeof(INPUT)));
    }

    static string Describe()
    {
        IntPtr h = GetForegroundWindow();
        uint pid;
        GetWindowThreadProcessId(h, out pid);
        StringBuilder sb = new StringBuilder(256);
        GetWindowTextW(h, sb, sb.Capacity);
        string proc = "?";
        try { proc = Process.GetProcessById((int)pid).ProcessName; } catch { }
        return string.Format("0x{0:X} [{1}] \"{2}\"", h, proc, sb.ToString());
    }

    public static void Main()
    {
        // 先建一个"假游戏"窗口占住前台：用记事本
        Console.WriteLine("=== 焦点保持测试 ===");

        IntPtr before = GetForegroundWindow();
        Console.WriteLine("拖动前前台: " + Describe());
        uint pidBefore; GetWindowThreadProcessId(before, out pidBefore);

        // 按住摇杆中心并拖动
        int cx = 200, cy = 700;
        Click(true, cx, cy);
        Thread.Sleep(120);
        for (int i = 1; i <= 10; i++)
        {
            Click(true, cx, cy - i * 12);
            Thread.Sleep(60);
        }
        Thread.Sleep(150);

        IntPtr during = GetForegroundWindow();
        uint pidDuring; GetWindowThreadProcessId(during, out pidDuring);
        Console.WriteLine("拖动中前台: " + Describe());

        Click(false, cx, cy - 120);
        Thread.Sleep(200);

        IntPtr after = GetForegroundWindow();
        Console.WriteLine("释放后前台: " + Describe());

        Console.WriteLine();
        bool same = (pidBefore == pidDuring);
        Console.WriteLine(same
            ? "★ 通过：拖动摇杆期间前台进程没有变（没有抢焦点）"
            : "✗ 失败：前台切到了别的进程 —— 仍会抢焦点");
    }
}
