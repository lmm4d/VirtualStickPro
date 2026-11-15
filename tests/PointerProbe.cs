// 探测：注入触摸后，究竟哪个窗口收到了什么消息
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

internal static class PointerProbe
{
    [DllImport("user32.dll", SetLastError = true)]
    static extern bool InitializeTouchInjection(uint maxCount, uint dwMode);
    [DllImport("user32.dll", SetLastError = true)]
    static extern bool InjectTouchInput(uint count, [In] POINTER_TOUCH_INFO[] contacts);
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool IsMouseInPointerEnabled();

    const uint TOUCH_FEEDBACK_DEFAULT = 0x1;
    const uint INRANGE = 0x00000002, INCONTACT = 0x00000004;
    const uint DOWN = 0x00010000, UPDATE = 0x00020000, UP = 0x00040000;
    const uint PT_TOUCH = 2;

    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }

    [StructLayout(LayoutKind.Sequential)]
    struct POINTER_INFO
    {
        public uint pointerType, pointerId, frameId, pointerFlags;
        public IntPtr sourceDevice, hwndTarget;
        public POINT ptPixelLocation, ptHimetricLocation, ptPixelLocationRaw, ptHimetricLocationRaw;
        public uint dwTime, historyCount;
        public int InputState;
        public uint dwKeyStates;
        public ulong PerformanceCount;
        public int ButtonChangeType;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct POINTER_TOUCH_INFO
    {
        public POINTER_INFO pointerInfo;
        public uint touchFlags, touchMask;
        public RECT rcContact, rcContactRaw;
        public uint orientation, pressure;
    }

    static POINTER_TOUCH_INFO Make(uint id, int x, int y, uint flags)
    {
        var t = new POINTER_TOUCH_INFO();
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
        t.touchMask = 0x3;
        t.rcContact.L = x - 2; t.rcContact.R = x + 2;
        t.rcContact.T = y - 2; t.rcContact.B = y + 2;
        t.rcContactRaw = t.rcContact;
        t.pressure = 32000;
        return t;
    }

    // 一个能记录所有消息的测试窗口
    class ProbeForm : Form
    {
        public int Touches, Pointers, MouseMsgs;
        public string Log = "";

        public ProbeForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            Bounds = new System.Drawing.Rectangle(0, 0, 400, 400);
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Location = new System.Drawing.Point(0, 0);
            BackColor = System.Drawing.Color.DarkSlateBlue;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg >= 0x0245 && m.Msg <= 0x024F) Pointers++;
            else if (m.Msg == 0x0240) Touches++;
            else if (m.Msg >= 0x0200 && m.Msg <= 0x020E) MouseMsgs++;

            if (m.Msg == 0x0246 || m.Msg == 0x0245 || m.Msg == 0x0247 || m.Msg == 0x0240)
            {
                Log += "msg=0x" + m.Msg.ToString("X4") + " wParam=" + m.WParam + "\n";
            }
            base.WndProc(ref m);
        }
    }

    public static void Main()
    {
        Console.WriteLine("IsMouseInPointerEnabled = " + IsMouseInPointerEnabled());
        Console.WriteLine();

        if (!InitializeTouchInjection(10, TOUCH_FEEDBACK_DEFAULT))
        {
            Console.WriteLine("注入失败 " + Marshal.GetLastWin32Error());
            return;
        }

        var f = new ProbeForm();
        f.Show();
        Application.DoEvents();
        Thread.Sleep(600);

        Console.WriteLine("探测窗口已显示，位置 (0,0)-(400,400)");
        Console.WriteLine("在 (100,100) 注入触摸...\n");

        var t = new POINTER_TOUCH_INFO[1];
        t[0] = Make(1, 100, 100, DOWN | INRANGE | INCONTACT);
        InjectTouchInput(1, t);
        Application.DoEvents();
        Thread.Sleep(400);

        for (int i = 1; i <= 5; i++)
        {
            t[0] = Make(1, 100, 100 - i * 10, UPDATE | INRANGE | INCONTACT);
            InjectTouchInput(1, t);
            Application.DoEvents();
            Thread.Sleep(60);
        }

        t[0] = Make(1, 100, 50, UP);
        InjectTouchInput(1, t);
        Application.DoEvents();
        Thread.Sleep(400);

        Console.WriteLine("结果：");
        Console.WriteLine("  WM_TOUCH 消息数   : " + f.Touches);
        Console.WriteLine("  WM_POINTER 消息数 : " + f.Pointers);
        Console.WriteLine("  鼠标消息数        : " + f.MouseMsgs);
        Console.WriteLine();
        Console.WriteLine(f.Log.Length > 0 ? "捕获到的消息：\n" + f.Log : "（没有捕获到触摸/指针消息）");

        f.Close();
        Application.Exit();
    }
}
