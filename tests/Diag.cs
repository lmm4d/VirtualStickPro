// 诊断探针：量坐标空间 + 用 SendInput 绝对坐标点击摇杆
using System;
using System.Runtime.InteropServices;
using System.Threading;

internal static class Diag
{
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("gdi32.dll")] static extern int GetDeviceCaps(IntPtr dc, int idx);
    [DllImport("user32.dll")] static extern IntPtr GetDesktopWindow();
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int idx);
    [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr mon, int type, out uint x, out uint y);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(POINT p, uint flags);

    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr extra; }

    [StructLayout(LayoutKind.Sequential)]
    struct INPUT { public uint type; public MOUSEINPUT mi; }

    const uint INPUT_MOUSE = 0;
    const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    const uint MOUSEEVENTF_LEFTUP = 0x0004;
    const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
    const uint MOUSEEVENTF_MOVE = 0x0001;

    static void Send(bool down, int x, int y)
    {
        int sw = GetSystemMetrics(0), sh = GetSystemMetrics(1);
        INPUT inp = new INPUT();
        inp.type = INPUT_MOUSE;
        inp.mi.dx = (int)((long)x * 65535 / Math.Max(1, sw - 1));
        inp.mi.dy = (int)((long)y * 65535 / Math.Max(1, sh - 1));
        inp.mi.dwFlags = MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_MOVE |
            (down ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP);
        SendInput(1, new INPUT[] { inp }, Marshal.SizeOf(typeof(INPUT)));
    }

    [DllImport("user32.dll")]
    static extern uint SendInput(uint n, INPUT[] p, int cb);

    public static void Main()
    {
        IntPtr dc = GetDC(IntPtr.Zero);
        int logX = GetDeviceCaps(dc, 88);   // LOGPIXELSX
        int logY = GetDeviceCaps(dc, 90);   // LOGPIXELSY
        ReleaseDC(IntPtr.Zero, dc);

        Console.WriteLine("=== 坐标空间诊断 ===");
        Console.WriteLine("LOGPIXELSX = " + logX + "  (96=100%, 120=125%, 144=150%)");
        Console.WriteLine("屏幕 GetSystemMetrics = " + GetSystemMetrics(0) + " x " + GetSystemMetrics(1));

        POINT p;
        GetCursorPos(out p);
        Console.WriteLine("当前光标: " + p.X + "," + p.Y);

        int cx = 200, cy = 700;
        SetCursorPos(cx, cy);
        Thread.Sleep(200);
        GetCursorPos(out p);
        Console.WriteLine("SetCursorPos(" + cx + "," + cy + ") 后实际: " + p.X + "," + p.Y);

        POINT target = new POINT();
        target.X = cx; target.Y = cy;

        Console.WriteLine();
        Console.WriteLine("=== 用 SendInput 绝对坐标在摇杆上按下并上拖 ===");

        Send(true, cx, cy);
        Thread.Sleep(150);

        for (int i = 1; i <= 15; i++)
        {
            Send(true, cx, cy - i * 10);
            Thread.Sleep(50);
        }
        Thread.Sleep(200);
        Send(false, cx, cy - 150);
        Thread.Sleep(200);

        Console.WriteLine("完成。请看 touchkeys.log 里是否有 ContactDown / Recompute 记录。");
    }
}
