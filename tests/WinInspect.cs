// 检查 TouchKeys 窗口的真实几何 + 分层属性 + 在摇杆位置调用 WindowFromPoint
using System;
using System.Runtime.InteropServices;
using System.Text;

internal static class WinInspect
{
    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int L, T, R, B; }

    [StructLayout(LayoutKind.Sequential)]
    struct POINT { public int X, Y; }

    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int idx);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr h, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int i);

    static IntPtr _found;

    public static void Main()
    {
        var procs = System.Diagnostics.Process.GetProcessesByName("TouchKeys");
        if (procs.Length == 0) { Console.WriteLine("TouchKeys 未运行"); return; }
        uint targetPid = (uint)procs[0].Id;

        EnumWindows(delegate(IntPtr h, IntPtr l)
        {
            uint pid;
            GetWindowThreadProcessId(h, out pid);
            if (pid != targetPid || !IsWindowVisible(h)) return true;
            int ex = GetWindowLong(h, -20);
            if ((ex & 0x80000) != 0) { _found = h; return false; }
            return true;
        }, IntPtr.Zero);

        if (_found == IntPtr.Zero) { Console.WriteLine("没找到分层窗口"); return; }

        StringBuilder cls = new StringBuilder(256);
        GetClassNameW(_found, cls, cls.Capacity);

        RECT r;
        GetWindowRect(_found, out r);
        int ex2 = GetWindowLong(_found, -20);

        Console.WriteLine("=== 窗口几何 ===");
        Console.WriteLine("句柄     : 0x" + _found.ToString("X"));
        Console.WriteLine("类名     : " + cls);
        Console.WriteLine("位置     : L=" + r.L + " T=" + r.T + " R=" + r.R + " B=" + r.B);
        Console.WriteLine("尺寸     : " + (r.R - r.L) + " x " + (r.B - r.T));
        Console.WriteLine("虚拟屏   : " + GetSystemMetrics(78) + " x " + GetSystemMetrics(79));
        Console.WriteLine("主屏     : " + GetSystemMetrics(0) + " x " + GetSystemMetrics(1));
        Console.WriteLine();
        Console.WriteLine("=== 扩展样式 ===");
        Console.WriteLine("EWS 0x     : 0x" + ex2.ToString("X8"));
        Console.WriteLine("LAYERED    : " + ((ex2 & 0x80000) != 0));
        Console.WriteLine("TRANSPARENT: " + ((ex2 & 0x20) != 0) + "   <- 这必须 False，否则整窗穿透");
        Console.WriteLine("TOPMOST    : " + ((ex2 & 0x8) != 0));
        Console.WriteLine("NOACTIVATE : " + ((ex2 & 0x8000000) != 0));
        Console.WriteLine();

        Console.WriteLine("=== 命中测试（WindowFromPoint）===");
        int[][] pts = new int[][] {
            new int[]{5,5}, new int[]{768,480}, new int[]{200,700},
            new int[]{200,550}, new int[]{1155,755}
        };
        foreach (int[] pt in pts)
        {
            POINT p = new POINT(); p.X = pt[0]; p.Y = pt[1];
            IntPtr hit = WindowFromPoint(p);
            IntPtr root = GetAncestor(hit, 2); // GA_ROOT

            StringBuilder c2 = new StringBuilder(256);
            GetClassNameW(hit, c2, c2.Capacity);

            uint hpid;
            GetWindowThreadProcessId(hit, out hpid);

            Console.WriteLine(string.Format("  ({0,4},{1,4}) -> 0x{2:X} [{3}] pid={4} {5}",
                pt[0], pt[1], hit, c2.ToString().Trim(), hpid,
                hpid == targetPid ? "<== TouchKeys ✓" : ""));
        }
    }
}
