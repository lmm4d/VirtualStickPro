// 验证：拖摇杆到锁定按钮 -> 锁定并持续 W；松开手指后仍保持
using System;
using System.Runtime.InteropServices;
using System.Threading;

internal static class LockTest
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

    static void Mouse(bool down, int x, int y)
    {
        int sw = GetSystemMetrics(0), sh = GetSystemMetrics(1);
        INPUT i = new INPUT();
        i.type = 0;
        i.mi.dx = (int)((long)x * 65535 / Math.Max(1, sw - 1));
        i.mi.dy = (int)((long)y * 65535 / Math.Max(1, sh - 1));
        i.mi.dwFlags = ABSOLUTE | MOVE | (down ? LEFTDOWN : LEFTUP);
        SendInput(1, new INPUT[] { i }, Marshal.SizeOf(typeof(INPUT)));
    }

    static bool W() { return (GetAsyncKeyState(0x57) & 0x8000) != 0; }

    public static void Main()
    {
        // ini: cx=200 cy=700 radius=130 lockRadius=34 lockGap=26
        // 锁定按钮圆心 = (200, 700-130-26-34) = (200, 510)
        int cx = 200, cy = 700;
        int lockX = 200, lockY = 510;

        Console.WriteLine("=== 锁定功能测试 ===");
        Console.WriteLine("摇杆中心 (" + cx + "," + cy + ")，锁定按钮 (" + lockX + "," + lockY + ")");
        Console.WriteLine();

        // 1) 直接点锁定按钮：应该无效
        Mouse(true, lockX, lockY);
        Thread.Sleep(250);
        bool afterClick = W();
        Mouse(false, lockX, lockY);
        Thread.Sleep(250);
        Console.WriteLine("1) 直接点锁定按钮 -> W=" + afterClick +
            (afterClick == false ? "   ✓ 无效（符合设计）" : "   ✗ 不该生效"));

        // 2) 拖摇杆到锁定按钮：应该锁定
        Mouse(true, cx, cy);
        Thread.Sleep(150);

        bool sawWDuringDrag = false;
        for (int i = 1; i <= 14; i++)
        {
            int y = cy - (cy - lockY) * i / 14;
            Mouse(true, cx, y);
            Thread.Sleep(55);
            if (W()) sawWDuringDrag = true;
        }
        Thread.Sleep(200);

        bool wBeforeRelease = W();

        // 3) 松开手指：应该保持锁定
        Mouse(false, lockX, lockY);
        Thread.Sleep(400);
        bool wAfterRelease = W();

        Console.WriteLine("2) 拖到锁定区 -> 拖动中出现 W=" + sawWDuringDrag +
            "，松手前 W=" + wBeforeRelease);
        Console.WriteLine("3) 松手后仍 W=" + wAfterRelease +
            (wAfterRelease ? "   ✓ 保持锁定（一直前进）" : "   ✗ 锁定丢失"));

        // 4) 再按住摇杆：应该解锁
        Mouse(true, cx, cy);
        Thread.Sleep(250);
        bool wAfterTouch = W();
        Mouse(false, cx, cy);
        Thread.Sleep(250);
        Console.WriteLine("4) 再按住摇杆 -> W=" + wAfterTouch +
            (wAfterTouch == false ? "   ✓ 已解锁" : "   ✗ 没解开"));

        Console.WriteLine();
        bool pass = (!afterClick) && sawWDuringDrag && wAfterRelease && (!wAfterTouch);
        Console.WriteLine(pass ? "★ 全部通过" : "✗ 有失败项");
    }
}
