// 测量渲染性能：连续快速操作，统计 CPU 占用和响应延迟
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

internal static class PerfTest
{
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int i);

    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Sequential)]
    struct INPUT { public uint type; public MOUSEINPUT mi; }
    [DllImport("user32.dll")] static extern uint SendInput(uint n, INPUT[] p, int cb);

    const uint LEFTDOWN = 0x0002, LEFTUP = 0x0004;
    const uint ABSOLUTE = 0x8000, MOVE = 0x0001;

    static void Raw(int x, int y, uint extra)
    {
        int sw = GetSystemMetrics(0), sh = GetSystemMetrics(1);
        INPUT i = new INPUT();
        i.type = 0;
        i.mi.dx = (int)((long)x * 65535 / Math.Max(1, sw - 1));
        i.mi.dy = (int)((long)y * 65535 / Math.Max(1, sh - 1));
        i.mi.dwFlags = ABSOLUTE | MOVE | extra;
        SendInput(1, new INPUT[] { i }, Marshal.SizeOf(typeof(INPUT)));
    }

    public static void Main()
    {
        var procs = Process.GetProcessesByName("TouchKeys");
        if (procs.Length == 0) { Console.WriteLine("TouchKeys 未运行"); return; }
        var p = procs[0];

        int cx = 200, cy = 700;

        Console.WriteLine("=== 渲染性能测试 ===");
        Console.WriteLine("模拟：摇杆中心连续拖动 3 秒（约 200 次移动事件）");
        Console.WriteLine();

        p.Refresh();
        TimeSpan cpu0 = p.TotalProcessorTime;
        var sw = Stopwatch.StartNew();

        Raw(cx, cy, LEFTDOWN);
        Thread.Sleep(120);

        int moves = 0;
        while (sw.ElapsedMilliseconds < 3000)
        {
            // 绕小圈拖，持续产生重绘
            double ang = moves * 0.35;
            int x = cx + (int)(Math.Cos(ang) * 60);
            int y = cy + (int)(Math.Sin(ang) * 60);
            Raw(x, y, MOVE);
            moves++;
            Thread.Sleep(12);
        }

        Raw(cx, cy, LEFTUP);
        Thread.Sleep(300);
        sw.Stop();

        p.Refresh();
        TimeSpan cpu1 = p.TotalProcessorTime;

        double cpuMs = (cpu1 - cpu0).TotalMilliseconds;
        double wallMs = sw.Elapsed.TotalMilliseconds;
        double pct = cpuMs / wallMs * 100.0;

        Console.WriteLine("  移动事件数     : " + moves);
        Console.WriteLine("  实际耗时       : " + wallMs.ToString("0") + " ms");
        Console.WriteLine("  TouchKeys CPU  : " + cpuMs.ToString("0") + " ms");
        Console.WriteLine("  CPU 占用       : " + pct.ToString("0.0") + " %");
        Console.WriteLine("  平均每帧 CPU   : " + (cpuMs / moves).ToString("0.00") + " ms");
        Console.WriteLine();

        if (pct < 15) Console.WriteLine("★ 优秀：几乎无感");
        else if (pct < 30) Console.WriteLine("★ 良好：可接受");
        else if (pct < 60) Console.WriteLine("△ 偏高：仍有优化空间");
        else Console.WriteLine("✗ 过高：会明显卡顿");
    }
}
