// 验证 WM_POINTER 相关 API 在本机是否可用，以及结构体大小是否正确
using System;
using System.Runtime.InteropServices;

internal static class PointerApiCheck
{
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
    static extern IntPtr GetModuleHandleA(string name);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
    static extern IntPtr GetProcAddress(IntPtr h, string name);

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

    static void Check(string fname)
    {
        IntPtr u = GetModuleHandleA("user32.dll");
        IntPtr p = GetProcAddress(u, fname);
        Console.WriteLine("  " + fname.PadRight(24) + (p != IntPtr.Zero ? "存在" : "缺失 ← 会导致崩溃"));
    }

    public static void Main()
    {
        Console.WriteLine("=== user32.dll 指针 API 检查 ===");
        Check("GetPointerInfo");
        Check("GetPointerType");
        Check("GetPointerTouchInfo");
        Check("GetPointerPenInfo");
        Check("GetPointerId");
        Check("EnableMouseInPointer");
        Check("IsMouseInPointerEnabled");

        Console.WriteLine();
        Console.WriteLine("=== 结构体大小（必须和 C 定义一致）===");
        Console.WriteLine("  POINTER_INFO        = " + Marshal.SizeOf(typeof(POINTER_INFO)) +
            "  (期望 96)");
        Console.WriteLine("  POINTER_TOUCH_INFO  = " + Marshal.SizeOf(typeof(POINTER_TOUCH_INFO)) +
            "  (期望 144)");
        Console.WriteLine("  IntPtr.Size         = " + IntPtr.Size +
            "  (" + (IntPtr.Size == 8 ? "64 位" : "32 位") + ")");
    }
}
