// 全方向 + 按钮 完整实测
using System;
using System.Runtime.InteropServices;
using System.Threading;

internal static class FullTest
{
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int i);

    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr extra; }

    [StructLayout(LayoutKind.Sequential)]
    struct INPUT { public uint type; public MOUSEINPUT mi; }

    [DllImport("user32.dll")] static extern uint SendInput(uint n, INPUT[] p, int cb);

    const uint LEFTDOWN = 0x0002, LEFTUP = 0x0004;
    const uint ABSOLUTE = 0x8000, MOVE = 0x0001;

    static void Send(bool down, int x, int y)
    {
        int sw = GetSystemMetrics(0), sh = GetSystemMetrics(1);
        INPUT inp = new INPUT();
        inp.type = 0;
        inp.mi.dx = (int)((long)x * 65535 / Math.Max(1, sw - 1));
        inp.mi.dy = (int)((long)y * 65535 / Math.Max(1, sh - 1));
        inp.mi.dwFlags = ABSOLUTE | MOVE | (down ? LEFTDOWN : LEFTUP);
        SendInput(1, new INPUT[] { inp }, Marshal.SizeOf(typeof(INPUT)));
    }

    static bool K(int vk) { return (GetAsyncKeyState(vk) & 0x8000) != 0; }

    static int _pass, _fail;

    static void Check(string name, bool ok, string detail)
    {
        Console.WriteLine((ok ? "  ✓ " : "  ✗ ") + name.PadRight(22) + detail);
        if (ok) _pass++; else _fail++;
    }

    /// <summary>从摇杆中心往某个方向推，检查对应键。</summary>
    static void TestStick(string dir, int dx, int dy, int expectVk, string expectName)
    {
        int cx = 200, cy = 700;

        Send(true, cx, cy);
        Thread.Sleep(120);

        bool saw = false;
        for (int i = 1; i <= 12; i++)
        {
            Send(true, cx + dx * i / 12, cy + dy * i / 12);
            Thread.Sleep(45);
            if (K(expectVk)) saw = true;
        }
        Thread.Sleep(100);
        Send(false, cx + dx, cy + dy);
        Thread.Sleep(200);

        bool stillDown = K(expectVk);
        Check("摇杆推" + dir, saw && !stillDown,
            "发出 " + expectName + "=" + saw + " 松开后复位=" + (!stillDown));
    }

    static void TestButton(string name, int bx, int by, int expectVk, string expectName)
    {
        // 先把光标"空移"到目标附近，避免从上一个位置跳过来时
        // 中间那一帧被当成拖动，把按钮判成移出。
        Send(true, bx, by);
        Thread.Sleep(60);
        Send(true, bx, by);
        Thread.Sleep(420);
        bool down = K(expectVk);
        Send(false, bx, by);
        Thread.Sleep(200);
        bool released = !K(expectVk);

        Check("按钮 " + name, down && released,
            "按下 " + expectName + "=" + down + " 松开复位=" + released);
    }

    public static void Main()
    {
        // 先把所有键复位
        foreach (int vk in new int[] { 0x57, 0x41, 0x53, 0x44, 0x20, 0x31, 0x32 })
        {
            if (K(vk))
            {
                INPUT inp = new INPUT();
                inp.type = 1;
                SendInput(1, new INPUT[] { inp }, Marshal.SizeOf(typeof(INPUT)));
            }
        }

        Console.WriteLine("=== 摇杆四方向 ===");
        TestStick("上", 0, -150, 0x57, "W");
        TestStick("下", 0, 150, 0x53, "S");
        TestStick("左", -150, 0, 0x41, "A");
        TestStick("右", 150, 0, 0x44, "D");

        Console.WriteLine();
        Console.WriteLine("=== 自定义按钮 ===");
        // ini: button.0 = (1100,700,110) SPACE ; button.1 = (1220,600,100) 1 ; button.2=(1220,820,100) 2
        TestButton("跳(SPACE)", 1155, 755, 0x20, "SPACE");
        TestButton("攻(1)", 1270, 650, 0x31, "1");
        TestButton("技(2)", 1270, 870, 0x32, "2");

        Console.WriteLine();
        Console.WriteLine("=== 结果 ===");
        Console.WriteLine("  通过 " + _pass + " / " + (_pass + _fail));
        Console.WriteLine(_fail == 0 ? "★ 全部通过" : "✗ 有 " + _fail + " 项失败");
    }
}
