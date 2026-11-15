// 验证：小圈圆心是否始终被限制在大圈内
using System;
using System.Runtime.InteropServices;
using System.Threading;

internal static class StickRange
{
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int i);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);

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

    static bool K(int vk) { return (GetAsyncKeyState(vk) & 0x8000) != 0; }

    public static void Main()
    {
        int cx = 200, cy = 700;
        Console.WriteLine("=== 摇杆范围测试（圆心 200,700）===");
        Console.WriteLine("尝试把手指拖到极远处，确认方向键仍然生效（说明被夹在大圈内）");
        Console.WriteLine();

        // ---------- 1) 真正的 45° 斜向，且拖得极远 ----------
        Mouse(true, cx, cy);
        Thread.Sleep(150);

        bool sawA = false, sawW = false;
        // 左上 45°：x 和 y 减少同样的量，拖到远超半径的位置
        // 注意：不要拖出屏幕边界，否则系统收不到事件（与摇杆无关）
        for (int i = 1; i <= 12; i++)
        {
            int d = i * 15;                       // 最多 180px，远超半径 105 但不出屏
            Mouse(true, cx - d, cy - d);
            Thread.Sleep(40);
            if (K(0x41)) sawA = true;
            if (K(0x57)) sawW = true;
        }

        Console.WriteLine("左上 45° 拖到 -180,-180（远超半径 105，未出屏）：");
        Console.WriteLine("  发出 A(左) = " + sawA);
        Console.WriteLine("  发出 W(上) = " + sawW);

        Mouse(false, cx - 180, cy - 180);
        Thread.Sleep(350);
        bool released = !K(0x41) && !K(0x57);
        Console.WriteLine("  松手后复位 = " + released);

        Console.WriteLine();

        // ---------- 2) 纯方向：正左 ----------
        Mouse(true, cx, cy);
        Thread.Sleep(150);
        bool pureLeft = false;
        for (int i = 1; i <= 12; i++)
        {
            Mouse(true, cx - i * 15, cy);         // 正左，拖到 -180
            Thread.Sleep(40);
            if (K(0x41)) pureLeft = true;
        }
        bool noUp = !K(0x57);
        Mouse(false, cx - 180, cy);
        Thread.Sleep(300);
        Console.WriteLine("正左拖到 -180：");
        Console.WriteLine("  发出 A(左) = " + pureLeft);
        Console.WriteLine("  不应发出 W  = " + noUp);
        Console.WriteLine();

        bool ok = sawA && sawW && released && pureLeft && noUp;
        Console.WriteLine(ok
            ? "★ 通过：超范围后方向仍正确（小圈被夹在大圈内）"
            : "✗ 有失败项");
    }
}
