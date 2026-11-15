// 触摸/鼠标打在摇杆上，验证是否真的发出 W 键。
// 做法：装一个低级键盘钩子不太好（会显得像键盘记录器），
//      改成轮询 GetAsyncKeyState —— 只读、不安钩子。
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

internal static class KeyProbe
{
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vk);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint flags, int dx, int dy, uint data, IntPtr extra);

    private const uint LEFTDOWN = 0x0002;
    private const uint LEFTUP = 0x0004;

    public static void Main()
    {
        Console.WriteLine("=== 摇杆 → 键盘 实测 ===");

        int cx = 200, cy = 700;
        int upY = cy - 150;    // 推到上方

        Console.WriteLine("把鼠标移到摇杆中心并按住往上拖...");
        SetCursorPos(cx, cy);
        Thread.Sleep(300);

        mouse_event(LEFTDOWN, 0, 0, 0, IntPtr.Zero);
        Thread.Sleep(150);

        bool sawW = false;
        bool sawA = false;
        bool sawS = false;
        bool sawD = false;

        // 缓慢往上移动，途中持续采样
        for (int i = 0; i <= 20; i++)
        {
            int y = cy - (cy - upY) * i / 20;
            SetCursorPos(cx, y);
            Thread.Sleep(60);

            if ((GetAsyncKeyState(0x57) & 0x8000) != 0) sawW = true; // W
            if ((GetAsyncKeyState(0x41) & 0x8000) != 0) sawA = true; // A
            if ((GetAsyncKeyState(0x53) & 0x8000) != 0) sawS = true; // S
            if ((GetAsyncKeyState(0x44) & 0x8000) != 0) sawD = true; // D
        }

        mouse_event(LEFTUP, 0, 0, 0, IntPtr.Zero);
        Thread.Sleep(300);

        bool afterW = (GetAsyncKeyState(0x57) & 0x8000) != 0;

        Console.WriteLine();
        Console.WriteLine("推杆过程中观察到:");
        Console.WriteLine("  W (上) : " + sawW);
        Console.WriteLine("  S (下) : " + sawS);
        Console.WriteLine("  A (左) : " + sawA);
        Console.WriteLine("  D (右) : " + sawD);
        Console.WriteLine();
        Console.WriteLine("松开后 W 是否仍按住(应为 False): " + afterW);
        Console.WriteLine();
        Console.WriteLine(sawW && !afterW
            ? "★ 通过：推上发出 W，松开后正确复位"
            : "✗ 未通过：检查 TouchKeys 是否在运行、是否处于游玩模式");
    }
}
