// 自检：把 TouchKeys 窗口的逐像素 alpha 图取样出来，确认
//   1) 摇杆/按键区域 alpha > 0（会吃触摸）
//   2) 空白区域 alpha == 0（Windows 会穿透，不挡游戏）
// 用法：SelfTest.exe  （需先启动 TouchKeys.exe）
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

internal static class SelfTest
{
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int L, T, R, B; }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE { public int CX, CY; }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BLENDFUNCTION { public byte Op, Flags, Alpha, Format; }

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumProc cb, IntPtr l);

    private delegate bool EnumProc(IntPtr h, IntPtr l);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr h, int idx);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr h, out RECT r);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr h);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr h, IntPtr dc);

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int w, int h);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr dc, IntPtr o);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr o);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr dc, IntPtr bmp, uint start, uint lines,
        byte[] bits, ref BITMAPINFO bi, uint usage);

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight;
        public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter,
                   biClrUsed, biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER hdr;
        public int c1, c2, c3;
    }

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr h);

    private static IntPtr _found = IntPtr.Zero;
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    public static void Main()
    {
        // 先找到 TouchKeys 进程 id
        System.Diagnostics.Process[] procs =
            System.Diagnostics.Process.GetProcessesByName("TouchKeys");

        if (procs.Length == 0)
        {
            Console.WriteLine("TouchKeys 进程没在运行，先启动 TouchKeys.exe");
            return;
        }

        Console.WriteLine("找到 TouchKeys 进程: " + procs[0].Id);

        EnumWindows(delegate(IntPtr h, IntPtr l)
        {
            uint pid;
            GetWindowThreadProcessId(h, out pid);
            if (pid != (uint)procs[0].Id) return true;
            if (!IsWindowVisible(h)) return true;

            int ex = GetWindowLong(h, GWL_EXSTYLE);
            if ((ex & WS_EX_LAYERED) != 0)
            {
                _found = h;
                Console.WriteLine("命中分层窗口句柄: 0x" + h.ToString("X"));
                return false;
            }
            return true;
        }, IntPtr.Zero);

        if (_found == IntPtr.Zero)
        {
            Console.WriteLine("找不到 TouchKeys 窗口（先把它启动起来）");
            return;
        }

        RECT rc;
        GetWindowRect(_found, out rc);
        int w = rc.R - rc.L, hh = rc.B - rc.T;
        Console.WriteLine("窗口大小: " + w + " x " + hh);

        int ex2 = GetWindowLong(_found, GWL_EXSTYLE);
        Console.WriteLine("WS_EX_LAYERED: " + ((ex2 & WS_EX_LAYERED) != 0));
        Console.WriteLine("WS_EX_TRANSPARENT(整窗穿透，应为 False): "
            + ((ex2 & 0x20) != 0));

        IntPtr screen = GetDC(IntPtr.Zero);
        IntPtr mem = CreateCompatibleDC(screen);
        IntPtr bmp = CreateCompatibleBitmap(screen, w, hh);
        IntPtr old = SelectObject(mem, bmp);

        Bitmap img = null;
        try
        {
            PrintWindow(_found, mem, 2); // PW_RENDERFULLCONTENT

            BITMAPINFO bi = new BITMAPINFO();
            bi.hdr.biSize = Marshal.SizeOf(typeof(BITMAPINFOHEADER));
            bi.hdr.biWidth = w;
            bi.hdr.biHeight = -hh;          // top-down
            bi.hdr.biPlanes = 1;
            bi.hdr.biBitCount = 32;
            bi.hdr.biCompression = 0;

            byte[] bits = new byte[w * hh * 4];
            int got = GetDIBits(mem, bmp, 0, (uint)hh, bits, ref bi, 0);
            Console.WriteLine("GetDIBits 行数: " + got);

            img = new Bitmap(w, hh, PixelFormat.Format32bppArgb);
            BitmapData bd = img.LockBits(new Rectangle(0, 0, w, hh),
                ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            Marshal.Copy(bits, 0, bd.Scan0, bits.Length);
            img.UnlockBits(bd);

            // 统计 alpha
            int opaque = 0, transparent = 0;
            for (int y = 0; y < hh; y += 3)
            {
                for (int x = 0; x < w; x += 3)
                {
                    if (img.GetPixel(x, y).A > 8) opaque++;
                    else transparent++;
                }
            }
            int total = opaque + transparent;
            Console.WriteLine();
            Console.WriteLine("=== 逐像素 alpha 统计（决定穿透范围）===");
            Console.WriteLine("  有内容(吃触摸): " + opaque + "  占 " +
                (100.0 * opaque / total).ToString("0.00") + "%");
            Console.WriteLine("  空白(穿透)   : " + transparent + "  占 " +
                (100.0 * transparent / total).ToString("0.00") + "%");

            Console.WriteLine();
            Console.WriteLine("=== 关键点位采样 ===");
            Report(img, "左上角(应穿透)", 5, 5);
            Report(img, "屏幕中央(应穿透)", w / 2, hh / 2);
            Report(img, "右下角(应穿透)", w - 5, hh - 5);
            Report(img, "摇杆中心(应有内容)", 200, Math.Min(700, hh - 1));

            img.Save("selftest-alpha.png", ImageFormat.Png);
            Console.WriteLine();
            Console.WriteLine("alpha 图已存: selftest-alpha.png");
        }
        finally
        {
            if (img != null) img.Dispose();
            SelectObject(mem, old);
            DeleteObject(bmp);
            DeleteDC(mem);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    private static void Report(Bitmap img, string label, int x, int y)
    {
        if (x < 0 || y < 0 || x >= img.Width || y >= img.Height) return;
        Color c = img.GetPixel(x, y);
        Console.WriteLine(string.Format("  {0,-22} ({1},{2}) A={3,-3} RGB=({4},{5},{6})",
            label, x, y, c.A, c.R, c.G, c.B));
    }
}
