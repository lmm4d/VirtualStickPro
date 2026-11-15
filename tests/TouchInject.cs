// 用系统级触摸注入（Touch Injection API）模拟两根手指，
// 验证 TouchKeys 是否能同时处理两个独立触点。
//
// InitializeTouchInjection + InjectTouchInput 是 Windows 8+ 官方 API，
// 产生的就是真实的 WM_POINTER(PT_TOUCH) 消息。
using System;
using System.Runtime.InteropServices;
using System.Threading;

internal static class TouchInject
{
    [DllImport("user32.dll", SetLastError = true)]
    static extern bool InitializeTouchInjection(uint maxCount, uint dwMode);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool InjectTouchInput(uint count, [In] POINTER_TOUCH_INFO[] contacts);

    [DllImport("user32.dll")]
    static extern short GetAsyncKeyState(int vk);

    [DllImport("user32.dll")]
    static extern int GetSystemMetrics(int i);

    const uint TOUCH_FEEDBACK_DEFAULT = 0x1;

    const uint POINTER_FLAG_INRANGE = 0x00000002;
    const uint POINTER_FLAG_INCONTACT = 0x00000004;
    const uint POINTER_FLAG_DOWN = 0x00010000;
    const uint POINTER_FLAG_UPDATE = 0x00020000;
    const uint POINTER_FLAG_UP = 0x00040000;

    const uint PT_TOUCH = 0x00000002;
    const uint TOUCH_MASK_CONTACTAREA = 0x00000001;
    const uint TOUCH_MASK_PRESSURE = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    struct POINTER_INFO
    {
        public uint pointerType;
        public uint pointerId;
        public uint frameId;
        public uint pointerFlags;
        public IntPtr sourceDevice;
        public IntPtr hwndTarget;
        public POINT ptPixelLocation;
        public POINT ptHimetricLocation;
        public POINT ptPixelLocationRaw;
        public POINT ptHimetricLocationRaw;
        public uint dwTime;
        public uint historyCount;
        public int InputState;
        public uint dwKeyStates;
        public ulong PerformanceCount;
        public int ButtonChangeType;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct POINTER_TOUCH_INFO
    {
        public POINTER_INFO pointerInfo;
        public uint touchFlags;
        public uint touchMask;
        public RECT rcContact;
        public RECT rcContactRaw;
        public uint orientation;
        public uint pressure;
    }

    static POINTER_TOUCH_INFO Make(uint id, int x, int y, uint flags)
    {
        POINTER_TOUCH_INFO t = new POINTER_TOUCH_INFO();
        t.pointerInfo.pointerType = PT_TOUCH;
        t.pointerInfo.pointerId = id;
        t.pointerInfo.pointerFlags = flags;
        t.pointerInfo.ptPixelLocation.X = x;
        t.pointerInfo.ptPixelLocation.Y = y;
        t.pointerInfo.ptPixelLocationRaw.X = x;
        t.pointerInfo.ptPixelLocationRaw.Y = y;
        t.pointerInfo.ptHimetricLocation.X = x;
        t.pointerInfo.ptHimetricLocation.Y = y;
        t.pointerInfo.ptHimetricLocationRaw.X = x;
        t.pointerInfo.ptHimetricLocationRaw.Y = y;

        t.touchMask = TOUCH_MASK_CONTACTAREA | TOUCH_MASK_PRESSURE;
        t.rcContact.Left = x - 2;
        t.rcContact.Right = x + 2;
        t.rcContact.Top = y - 2;
        t.rcContact.Bottom = y + 2;
        t.rcContactRaw = t.rcContact;
        t.pressure = 32000;
        return t;
    }

    static bool K(int vk) { return (GetAsyncKeyState(vk) & 0x8000) != 0; }

    static int _pass, _fail;
    static void Check(string name, bool ok, string detail)
    {
        Console.WriteLine((ok ? "  OK  " : "  FAIL") + "  " + name.PadRight(34) + detail);
        if (ok) _pass++; else _fail++;
    }

    public static void Main()
    {
        Console.WriteLine("=== 多点触摸注入测试 ===");

        if (!InitializeTouchInjection(10, TOUCH_FEEDBACK_DEFAULT))
        {
            int e = Marshal.GetLastWin32Error();
            Console.WriteLine("InitializeTouchInjection 失败，错误码 " + e);
            Console.WriteLine("（1444 = 没有可用的触摸屏；需要真实触摸设备）");
            return;
        }
        Console.WriteLine("触摸注入已初始化，最多 10 点");
        Console.WriteLine();

        int cx = 200, cy = 700;              // 摇杆中心
        int bx = 1180 + 50, by = 760 + 50;   // button.1 中心（攻 = 1）
        int push = 80;

        // -------------------------------------------------
        // 测试：手指1 推摇杆向上，手指2 同时按按钮
        // -------------------------------------------------
        POINTER_TOUCH_INFO[] two = new POINTER_TOUCH_INFO[2];

        // 两指同时按下
        two[0] = Make(1, cx, cy, POINTER_FLAG_DOWN | POINTER_FLAG_INRANGE | POINTER_FLAG_INCONTACT);
        two[1] = Make(2, bx, by, POINTER_FLAG_DOWN | POINTER_FLAG_INRANGE | POINTER_FLAG_INCONTACT);
        InjectTouchInput(2, two);
        Thread.Sleep(250);

        bool stickDown = K(0x57) || K(0x41) || K(0x53) || K(0x44);
        bool btnDown = K(0x31);
        Console.WriteLine("两指按下后：摇杆键=" + stickDown + "  按钮键(1)=" + btnDown);

        // 手指1 往上推（手指2 保持按着）
        for (int i = 1; i <= 8; i++)
        {
            two[0] = Make(1, cx, cy - push * i / 8,
                POINTER_FLAG_UPDATE | POINTER_FLAG_INRANGE | POINTER_FLAG_INCONTACT);
            two[1] = Make(2, bx, by,
                POINTER_FLAG_UPDATE | POINTER_FLAG_INRANGE | POINTER_FLAG_INCONTACT);
            InjectTouchInput(2, two);
            Thread.Sleep(45);
        }
        Thread.Sleep(200);

        bool wNow = K(0x57);
        bool btnStill = K(0x31);
        Check("摇杆(W) + 按钮(1) 同时触发", wNow && btnStill,
            "W=" + wNow + " 按钮=" + btnStill);

        // 松开手指1（摇杆），手指2 保持
        two[0] = Make(1, cx, cy - push, POINTER_FLAG_UP);
        two[1] = Make(2, bx, by, POINTER_FLAG_UPDATE | POINTER_FLAG_INRANGE | POINTER_FLAG_INCONTACT);
        InjectTouchInput(2, two);
        Thread.Sleep(300);

        bool wReleased = !K(0x57);
        bool btnHeld = K(0x31);
        Check("松开摇杆后按钮仍按住", wReleased && btnHeld,
            "W已松=" + wReleased + " 按钮仍按=" + btnHeld);

        // 松开手指2
        two[1] = Make(2, bx, by, POINTER_FLAG_UP);
        InjectTouchInput(1, new POINTER_TOUCH_INFO[] { two[1] });
        Thread.Sleep(300);

        bool allUp = !K(0x57) && !K(0x31) && !K(0x41) && !K(0x53) && !K(0x44);
        Check("全部松开后无残留", allUp, "全部释放=" + allUp);

        Console.WriteLine();
        Console.WriteLine("=== 结果 ===");
        Console.WriteLine("  通过 " + _pass + " / " + (_pass + _fail));
        Console.WriteLine(_fail == 0 ? "★ 多指同时触发正常" : "✗ 多指仍有问题");
    }
}
