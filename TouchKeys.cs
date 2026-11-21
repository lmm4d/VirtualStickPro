// ============================================================================
//  TouchKeys —— 触屏虚拟摇杆 + 自定义按键（键盘映射）
//
//  设计要点
//   1) 全屏分层窗口（Layered Window）+ 逐像素 alpha：只有画了控件的地方
//      才接收鼠标/触摸，alpha=0 的区域 Windows 自动穿透，不影响游戏。
//   2) 只调用 SendInput 发送"扫描码"按键，无驱动、无全局钩子（不进杀软黑名单）。
//   3) 支持多点触控（WM_TOUCH）：左手推摇杆 + 右手按按钮可同时进行。
//   4) 配置存在同目录 touchkeys.ini，纯文本可手工改。
//
//  编译（Windows 自带 csc 即可）：
//    csc /target:winexe /out:TouchKeys.exe TouchKeys.cs
//
//  快捷键：Ctrl+Alt+F2 切换编辑/游玩模式
// ============================================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace TouchKeys
{
    // ========================================================================
    //  Win32 互操作
    // ========================================================================
    internal struct POINT
    {
        public int X; public int Y;
        public POINT(int x, int y) { X = x; Y = y; }
    }

    internal struct SIZE
    {
        public int CX; public int CY;
        public SIZE(int x, int y) { CX = x; CY = y; }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct BLENDFUNCTION
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TOUCHINPUT
    {
        public int x;
        public int y;
        public IntPtr hSource;
        public int dwID;
        public int dwFlags;
        public int dwMask;
        public int dwTime;
        public IntPtr dwExtraInfo;
        public int cxContact;
        public int cyContact;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public int mouseData;
        public int dwFlags;
        public int time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public int dwFlags;
        public int time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct HARDWAREINPUT
    {
        public int uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct INPUTUNION
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct INPUT
    {
        public uint type;
        public INPUTUNION u;
    }

    internal static class Native
    {
        public const int WS_EX_LAYERED = 0x00080000;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_TOPMOST = 0x00000008;
        public const int WS_EX_TRANSPARENT = 0x00000020;
        public const int WS_EX_NOACTIVATE = 0x08000000;

        public const int WM_MOUSEACTIVATE = 0x0021;
        public const int WM_TOUCH = 0x0240;
        public const int MI_WP_SIGNATURE = unchecked((int)0xFF515700);
        public const int MI_WP_MASK = unchecked((int)0xFFFFFF00);

        // ---- 指针消息（Windows 8+，每根手指独立 pointerId，比 WM_TOUCH 更可靠）----
        public const int WM_POINTERUPDATE = 0x0245;
        public const int WM_POINTERDOWN = 0x0246;
        public const int WM_POINTERUP = 0x0247;
        public const int WM_POINTERENTER = 0x0249;
        public const int WM_POINTERLEAVE = 0x024A;
        public const int WM_POINTERACTIVATE = 0x024B;
        public const int WM_POINTERCAPTURECHANGED = 0x024C;
        public const int WM_POINTERWHEEL = 0x024E;
        public const int WM_POINTERHWHEEL = 0x024F;

        public const uint PT_TOUCH = 0x00000002;
        public const uint PT_PEN = 0x00000003;
        public const uint PT_MOUSE = 0x00000004;

        /// <summary>从 pointerId 取"触点编号"用的位宽（低 16 位是 id）。</summary>
        public const int POINTER_ID_MASK = 0xFFFF;
        public const int WM_HOTKEY = 0x0312;
        public const int WM_CANCELMODE = 0x001F;
        public const int MA_NOACTIVATE = 3;
        public const int PA_NOACTIVATE = 3;

        public const uint TOUCHEVENTF_MOVE = 0x0001;
        public const uint TOUCHEVENTF_DOWN = 0x0002;
        public const uint TOUCHEVENTF_UP = 0x0004;

        public const int ULW_ALPHA = 2;
        public const byte AC_SRC_OVER = 0;
        public const byte AC_SRC_ALPHA = 1;

        public const uint INPUT_KEYBOARD = 1;
        public const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
        public const uint KEYEVENTF_KEYUP = 0x0002;
        public const uint KEYEVENTF_SCANCODE = 0x0008;

        public const uint MAPVK_VK_TO_VSC = 0;
        public const uint MAPVK_VSC_TO_VK = 1;

        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_SHIFT = 0x0004;
        public const uint MOD_WIN = 0x0008;

        public const int DIB_RGB_COLORS = 0;

        public const int SM_XVIRTUALSCREEN = 76;
        public const int SM_YVIRTUALSCREEN = 77;
        public const int SM_CXVIRTUALSCREEN = 78;
        public const int SM_CYVIRTUALSCREEN = 79;

        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int nIndex);

        [DllImport("user32.dll")]
        public static extern IntPtr SetCapture(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        public static extern IntPtr GetCapture();

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        public static extern IntPtr GetMessageExtraInfo();

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFOHEADER
        {
            public int biSize;
            public int biWidth;
            public int biHeight;
            public short biPlanes;
            public short biBitCount;
            public int biCompression;
            public int biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public int biClrUsed;
            public int biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFO
        {
            public BITMAPINFOHEADER bmiHeader;
            public int bmiColors0;
            public int bmiColors1;
            public int bmiColors2;
        }

        [DllImport("gdi32.dll", SetLastError = true)]
        public static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO pbmi,
            int usage, out IntPtr ppvBits, IntPtr hSection, int offset);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst,
            ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pprSrc,
            int crKey, ref BLENDFUNCTION pblend, int dwFlags);

        [DllImport("user32.dll")]
        public static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateCompatibleDC(IntPtr hDC);

        [DllImport("gdi32.dll")]
        public static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        public static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);

        [DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr hObject);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterTouchWindow(IntPtr hwnd, uint ulFlags);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool EnableMouseInPointer(bool fEnable);

        [DllImport("user32.dll")]
        public static extern bool IsMouseInPointerEnabled();

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetPointerId(IntPtr pointerInfo);

        // 注意：不要用 GetPointerType —— 它在部分系统上不存在，
        // P/Invoke 解析失败会抛 NullReferenceException（用户实际遇到过）。
        // 统一走 GetPointerInfo，它在所有支持 WM_POINTER 的系统上都有。
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool GetPointerInfo(uint pointerId, ref POINTER_INFO pointerInfo);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool GetPointerTouchInfo(uint pointerId, ref POINTER_TOUCH_INFO info);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool GetPointerPenInfo(uint pointerId, ref POINTER_PEN_INFO penInfo);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT2
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT2
        {
            public int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINTER_INFO
        {
            public uint pointerType;
            public uint pointerId;
            public uint frameId;
            public uint pointerFlags;
            public IntPtr sourceDevice;
            public IntPtr hwndTarget;
            public POINT2 ptPixelLocation;
            public POINT2 ptHimetricLocation;
            public POINT2 ptPixelLocationRaw;
            public POINT2 ptHimetricLocationRaw;
            public uint dwTime;
            public uint historyCount;
            public int InputState;
            public uint dwKeyStates;
            public ulong PerformanceCount;
            public int ButtonChangeType;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINTER_TOUCH_INFO
        {
            public POINTER_INFO pointerInfo;
            public uint touchFlags;
            public uint touchMask;
            public RECT2 rcContact;
            public RECT2 rcContactRaw;
            public uint orientation;
            public uint pressure;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINTER_PEN_INFO
        {
            public POINTER_INFO pointerInfo;
            public uint penMask;
            public uint pressure;
            public uint rotation;
            public int tiltX;
            public int tiltY;
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool GetTouchInputInfo(IntPtr hTouchInput, int cInputs,
            [In, Out] TOUCHINPUT[] pInputs, int cbSize);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool CloseTouchInputHandle(IntPtr hTouchInput);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint nInputs,
            [In] INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        public static extern uint MapVirtualKey(uint uCode, uint uMapType);

        [DllImport("user32.dll")]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();

        [DllImport("user32.dll")]
        public static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        [DllImport("shcore.dll")]
        public static extern int SetProcessDpiAwareness(int value);

        public static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 =
            new IntPtr(-4);
        public static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE =
            new IntPtr(-3);
        public static readonly IntPtr DPI_AWARENESS_CONTEXT_SYSTEM_AWARE =
            new IntPtr(-2);

        [DllImport("user32.dll", EntryPoint = "GetDpiForSystem")]
        public static extern uint GetDpiForSystem();
    }

    // ========================================================================
    //  按键名 <-> 虚拟键码
    // ========================================================================
    internal static class KeyNames
    {
        private static readonly Dictionary<string, int> ByName =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<int, string> ByCode =
            new Dictionary<int, string>();

        static KeyNames()
        {
            Add("BACK", 0x08); Add("TAB", 0x09); Add("ENTER", 0x0D);
            Add("SHIFT", 0x10); Add("CTRL", 0x11); Add("ALT", 0x12);
            Add("PAUSE", 0x13); Add("CAPSLOCK", 0x14); Add("ESC", 0x1B);
            Add("SPACE", 0x20); Add("PAGEUP", 0x21); Add("PAGEDOWN", 0x22);
            Add("END", 0x23); Add("HOME", 0x24);
            Add("LEFT", 0x25); Add("UP", 0x26); Add("RIGHT", 0x27); Add("DOWN", 0x28);
            Add("INSERT", 0x2D); Add("DELETE", 0x2E);
            Add("LSHIFT", 0xA0); Add("RSHIFT", 0xA1);
            Add("LCTRL", 0xA2); Add("RCTRL", 0xA3);
            Add("LALT", 0xA4); Add("RALT", 0xA5);

            for (int i = 0; i < 10; i++)
                Add(i.ToString(CultureInfo.InvariantCulture), 0x30 + i);

            for (char c = 'A'; c <= 'Z'; c++)
                Add(c.ToString(), (int)c);

            for (int i = 1; i <= 12; i++)
                Add("F" + i.ToString(CultureInfo.InvariantCulture), 0x70 + i - 1);

            // 数字小键盘
            for (int i = 0; i <= 9; i++)
                Add("NUM" + i.ToString(CultureInfo.InvariantCulture), 0x60 + i);
            Add("MULTIPLY", 0x6A); Add("ADD", 0x6B); Add("SUBTRACT", 0x6D);
            Add("DECIMAL", 0x6E); Add("DIVIDE", 0x6F);
        }

        private static void Add(string name, int code)
        {
            ByName[name] = code;
            if (!ByCode.ContainsKey(code)) ByCode[code] = name;
        }

        /// <summary>把 "W" / "SPACE" / "0x41" 解析成虚拟键码。</summary>
        public static bool TryParse(string text, out int vk)
        {
            vk = 0;
            if (string.IsNullOrEmpty(text)) return false;

            string s = text.Trim();
            if (s.Length == 0) return false;

            if (ByName.TryGetValue(s, out vk)) return true;

            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                int hex;
                if (int.TryParse(s.Substring(2), NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture, out hex) && hex > 0 && hex < 256)
                {
                    vk = hex;
                    return true;
                }
            }

            int dec;
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out dec)
                && dec > 0 && dec < 256)
            {
                vk = dec;
                return true;
            }
            return false;
        }

        public static string ToName(int vk)
        {
            string name;
            if (ByCode.TryGetValue(vk, out name)) return name;
            return "0x" + vk.ToString("X2", CultureInfo.InvariantCulture);
        }

        /// <summary>方向键 / 小键盘 / Delete 等需要 EXTENDEDKEY 标志。</summary>
        public static bool IsExtended(int vk)
        {
            switch (vk)
            {
                case 0x21: case 0x22: case 0x23: case 0x24:
                case 0x25: case 0x26: case 0x27: case 0x28:
                case 0x2D: case 0x2E:
                case 0x6F:
                case 0x90: case 0xA3: case 0xA5:
                    return true;
                default:
                    return false;
            }
        }
    }

    // ========================================================================
    //  发送按键
    // ========================================================================
    internal static class KeySender
    {
        public static void Send(int vk, bool down)
        {
            if (vk <= 0) return;

            INPUT input = new INPUT();
            input.type = Native.INPUT_KEYBOARD;
            input.u.ki.wVk = 0;

            uint scan = Native.MapVirtualKey((uint)vk, Native.MAPVK_VK_TO_VSC);
            input.u.ki.wScan = (ushort)(scan & 0xFF);

            uint flags = Native.KEYEVENTF_SCANCODE;
            if (KeyNames.IsExtended(vk)) flags |= Native.KEYEVENTF_EXTENDEDKEY;
            if (!down) flags |= Native.KEYEVENTF_KEYUP;
            input.u.ki.dwFlags = (int)flags;
            input.u.ki.time = 0;
            input.u.ki.dwExtraInfo = IntPtr.Zero;

            Native.SendInput(1, new INPUT[] { input }, Marshal.SizeOf(typeof(INPUT)));
        }
    }

    // ========================================================================
    //  可移动控件基类
    // ========================================================================
    internal abstract class OverlayItem
    {
        public static int ScreenWidth = 1920;
        public static int ScreenHeight = 1080;
        public bool Selected;

        public abstract Rectangle Bounds { get; }
        public abstract bool HitTest(Point p);

        public virtual void Move(int dx, int dy)
        {
            Rectangle r = Bounds;
            if (this is StickItem)
            {
                StickItem s = (StickItem)this;
                int minX = s.Radius;
                int maxX = Math.Max(minX, ScreenWidth - s.Radius);
                int minY = s.Radius;
                int maxY = Math.Max(minY, ScreenHeight - s.Radius);
                dx = Math.Max(minX - s.CX, Math.Min(maxX - s.CX, dx));
                dy = Math.Max(minY - s.CY, Math.Min(maxY - s.CY, dy));
            }
            Rectangle moved = new Rectangle(r.X + dx, r.Y + dy, r.Width, r.Height);
            ApplyBounds(moved);
        }

        protected abstract void ApplyBounds(Rectangle r);

        public virtual void Resize(int delta)
        {
        }
    }

    // ========================================================================
    //  摇杆
    // ========================================================================
    internal class StickItem : OverlayItem
    {
        public int CX = 200;
        public int CY = 700;
        public int Radius = 105;
        public double Deadzone = 0.28;
        public int KeyUp = 0x57;   // W
        public int KeyDown = 0x53; // S
        public int KeyLeft = 0x41; // A
        public int KeyRight = 0x44; // D

        /// <summary>当前推杆向量，范围 -1..1。</summary>
        public double VectorX;
        public double VectorY;

        // ---------------- 和平精英式摇杆 ----------------

        /// <summary>手指是否正按在摇杆上（决定外圈是否显示）。</summary>
        public bool Active;

        /// <summary>按下瞬间手指相对摇杆中心的偏移，让"抓取"不跳变。</summary>
        public int GrabDX;
        public int GrabDY;

        /// <summary>
        /// 小圈（摇杆帽）当前中心。它跟着手指走，但被限制在大圈内。
        /// </summary>
        public int KnobCX
        {
            get
            {
                int max = Math.Max(0, Math.Min(Radius, Math.Min(CX, Math.Max(0, OverlayItem.ScreenWidth - CX))));
                int dx = (int)(VectorX * max);
                return Math.Max(0, Math.Min(OverlayItem.ScreenWidth - 1, CX + dx));
            }
        }

        public int KnobCY
        {
            get
            {
                int max = Math.Max(0, Math.Min(Radius, Math.Min(CY, Math.Max(0, OverlayItem.ScreenHeight - CY))));
                int dy = (int)(VectorY * max);
                return Math.Max(0, Math.Min(OverlayItem.ScreenHeight - 1, CY + dy));
            }
        }
        /// <summary>小圈半径（摇杆帽），比大圈小很多。</summary>
        public int KnobRadius
        {
            get { return Math.Max(18, (int)(Radius * 0.42)); }
        }

        // ---------------- 锁定（一直前进） ----------------

        /// <summary>锁定按钮圆心（在摇杆正上方）。</summary>
        public int LockCX { get { return CX; } }
        public int LockCY { get { return CY - Radius - LockGap - LockRadius; } }
        public int LockRadius = 34;
        public int LockGap = 26;

        /// <summary>是否处于锁定状态。锁定后摇杆失效，持续按 KeyLock。</summary>
        public bool Locked;

        /// <summary>锁定后持续发送的键，默认前进（W）。</summary>
        public int KeyLock = 0x57;

        public Rectangle LockBounds
        {
            get
            {
                return new Rectangle(LockCX - LockRadius, LockCY - LockRadius,
                    LockRadius * 2, LockRadius * 2);
            }
        }

        public bool LockHitTest(Point p)
        {
            double dx = p.X - LockCX;
            double dy = p.Y - LockCY;
            return dx * dx + dy * dy <= (LockRadius * 1.15) * (LockRadius * 1.15);
        }

        public override Rectangle Bounds
        {
            get { return new Rectangle(CX - Radius, CY - Radius, Radius * 2, Radius * 2); }
        }

        public override bool HitTest(Point p)
        {
            double dx = p.X - CX;
            double dy = p.Y - CY;
            double limit = Radius * 1.25;
            return dx * dx + dy * dy <= limit * limit;
        }

        protected override void ApplyBounds(Rectangle r)
        {
            CX = r.X + r.Width / 2;
            CY = r.Y + r.Height / 2;
        }

        public override void Resize(int delta)
        {
            Radius = Math.Max(50, Math.Min(400, Radius + delta));
        }

        /// <summary>
        /// 由当前推杆状态计算方向键。
        ///
        /// 关键：用"角度扇区"判定，而不是把 x/y 分别跟死区比较。
        /// 后者在斜向推杆时会把较小的那个分量吃掉（比如拖到左上远处，
        /// 水平分量被压到死区以下，A 就不发了）。
        ///
        /// 扇区划分（死区把每个方向切成 ±45° 楔形）：
        ///   右: -22.5°..22.5°   下: 22.5°..67.5°     ...
        /// 多方向（如左上）会同时给出两个键，这是游戏需要的。
        /// </summary>
        public void CollectKeys(HashSet<int> keys)
        {
            if (Locked)
            {
                // 锁定时摇杆不参与，只发锁定键（持续前进）
                keys.Add(KeyLock);
                return;
            }

            if (Magnitude < Deadzone) return;

            // 用"单位方向的轴投影"判定，而不是角度扇区。
            //
            // 角度扇区的坑：45° 斜向正好落在 22.5° 与 157.5° 的边界上，
            // 稍一偏移就会只剩一个方向（实测 -135° 只出了 W，A 被吃掉）。
            //
            // 轴投影天然支持斜向双键：方向单位向量在 x/y 上的分量
            // 各自和一个较小的阈值比较，两个都超就同时按两个键。
            //
            // 阈值用 sin(22.5°)≈0.383：这样正方向（分量 0/1）一定能过，
            // 而接近 45° 斜向（0.707/0.707）两个都能过。
            // 取小一点（0.30）让斜向更容易双触发，手感更跟手。
            const double AxisThreshold = 0.30;

            bool left = DirX <= -AxisThreshold;
            bool right = DirX >= AxisThreshold;
            bool up = DirY <= -AxisThreshold;
            bool down = DirY >= AxisThreshold;

            if (left) keys.Add(KeyLeft);
            if (right) keys.Add(KeyRight);
            if (up) keys.Add(KeyUp);
            if (down) keys.Add(KeyDown);
        }

        /// <summary>按下时记录抓取点，避免小圈瞬间跳到手指位置。</summary>
        public void BeginGrab(Point p)
        {
            Active = true;
            GrabDX = p.X - CX;
            GrabDY = p.Y - CY;

            // 抓在中心附近时，直接按手指位置算，手感更直接
            UpdateFromPoint(p);
        }

        /// <summary>手指推杆的"幅度"，0..1。0 表示在中心。</summary>
        public double Magnitude;

        /// <summary>手指方向（单位向量），不受幅度影响。</summary>
        public double DirX;
        public double DirY;

        public void UpdateFromPoint(Point p)
        {
            // 用"手指位置 - 抓取偏移"作为有效推杆点，
            // 这样按在边缘也不会瞬间弹满。
            double dx = (p.X - GrabDX) - CX;
            double dy = (p.Y - GrabDY) - CY;

            double len = Math.Sqrt(dx * dx + dy * dy);

            if (len < 0.5)
            {
                Magnitude = 0;
                DirX = 0;
                DirY = 0;
                VectorX = 0;
                VectorY = 0;
                return;
            }

            // 方向永远是单位向量，幅度单独记录（超出大圈就贴边）
            DirX = dx / len;
            DirY = dy / len;
            Magnitude = Math.Min(1.0, len / Radius);

            VectorX = DirX * Magnitude;
            VectorY = DirY * Magnitude;
        }

        public void Release()
        {
            VectorX = 0;
            VectorY = 0;

            // 这三个也必须清 —— CollectKeys 用 Magnitude 判方向，
            // 只清 VectorX/Y 的话松开后按键会一直卡住不放。
            Magnitude = 0;
            DirX = 0;
            DirY = 0;

            Active = false;
            GrabDX = 0;
            GrabDY = 0;
        }
    }

    // ========================================================================
    //  自定义按键
    // ========================================================================
    internal class ButtonItem : OverlayItem
    {
        public int X = 0;
        public int Y = 0;
        public int W = 110;
        public int H = 110;
        public string Label = "键";
        public int Vk = 0x20; // SPACE

        /// <summary>按住这个按钮的触点数。多指按同一按钮时互不干扰。</summary>
        private int _pressCount;

        public bool Pressed { get { return _pressCount > 0; } }
        public double PressScale = 1.0;
        public Timer AnimationTimer;

        public void PressIn() { _pressCount++; }
        public void PressOut() { if (_pressCount > 0) _pressCount--; }
        public void ForceRelease() { _pressCount = 0; }

        public override Rectangle Bounds
        {
            get { return new Rectangle(X, Y, W, H); }
        }

        public override bool HitTest(Point p)
        {
            return Bounds.Contains(p);
        }

        protected override void ApplyBounds(Rectangle r)
        {
            X = r.X;
            Y = r.Y;
        }

        public override void Resize(int delta)
        {
            W = Math.Max(40, Math.Min(500, W + delta));
            H = Math.Max(40, Math.Min(500, H + delta));
        }
    }

    // ========================================================================
    //  配置
    // ========================================================================
    internal class Config
    {
        public bool ShowGear = true;
        public int GearSize = 40;
        public int Opacity = 150;          // 控件基础不透明度 0-255
        public bool AutoStart;
        public int ToggleModifiers = (int)(Native.MOD_CONTROL | Native.MOD_ALT);
        public int ToggleKey = 0x72; // F3

        public StickItem Stick = new StickItem();
        public List<ButtonItem> Buttons = new List<ButtonItem>();

        public static string DefaultPath
        {
            get
            {
                string candidate = null;

                try
                {
                    string exe = Application.ExecutablePath;
                    string dir = Path.GetDirectoryName(exe);
                    if (!string.IsNullOrEmpty(dir)) candidate = Path.Combine(dir, "touchkeys.ini");
                }
                catch
                {
                }

                if (candidate != null && CanWriteTo(candidate)) return candidate;

                // 回退 1：用户 AppData
                string appData;
                try
                {
                    appData = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "TouchKeys");
                    string p1 = Path.Combine(appData, "touchkeys.ini");
                    if (CanWriteTo(p1)) return p1;
                }
                catch
                {
                }

                // 回退 2：临时目录
                return Path.Combine(Path.GetTempPath(), "touchkeys.ini");
            }
        }

        /// <summary>判断某个 ini 路径所在目录可写（不存在则尝试创建）。</summary>
        private static bool CanWriteTo(string iniPath)
        {
            try
            {
                string dir = Path.GetDirectoryName(iniPath);
                if (string.IsNullOrEmpty(dir)) return false;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                string probe = Path.Combine(dir, ".tk_probe_" +
                    Guid.NewGuid().ToString("N").Substring(0, 6));
                File.WriteAllText(probe, "x");
                File.Delete(probe);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static Config Load(string path)
        {
            Config cfg = new Config();

            if (!File.Exists(path))
            {
                cfg.Seed();
                cfg.Save(path);
                return cfg;
            }

            Dictionary<string, Dictionary<string, string>> ini;
            try
            {
                ini = ReadIni(path);
            }
            catch (Exception ex)
            {
                // A malformed or temporarily unreadable INI must not prevent the
                // overlay from starting. Preserve the user's file for recovery.
                string backup = path + ".broken-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                try { File.Copy(path, backup, false); } catch { backup = "(备份失败)"; }
                OverlayForm.Log("【配置异常】无法读取 " + path + "：" + ex.Message +
                    "；备份：" + backup + "；已载入默认配置");
                cfg.Seed();
                cfg.Save(path);
                return cfg;
            }

            Dictionary<string, string> app;
            if (ini.TryGetValue("app", out app))
            {
                cfg.ShowGear = ReadBool(app, "showGear", cfg.ShowGear);
                cfg.GearSize = ReadInt(app, "gearSize", cfg.GearSize);
                cfg.Opacity = ReadInt(app, "opacity", cfg.Opacity);
                cfg.AutoStart = ReadBool(app, "autoStart", cfg.AutoStart);
                cfg.ToggleModifiers = ReadInt(app, "toggleModifiers", cfg.ToggleModifiers);
                cfg.ToggleKey = ReadInt(app, "toggleKey", cfg.ToggleKey);
                if (cfg.Opacity < 30) cfg.Opacity = 30;
                if (cfg.Opacity > 255) cfg.Opacity = 255;
            }

            Dictionary<string, string> st;
            if (ini.TryGetValue("stick", out st))
            {
                cfg.Stick.CX = ReadInt(st, "cx", cfg.Stick.CX);
                cfg.Stick.CY = ReadInt(st, "cy", cfg.Stick.CY);
                cfg.Stick.Radius = ReadInt(st, "radius", cfg.Stick.Radius);
                cfg.Stick.Deadzone = ReadDouble(st, "deadzone", cfg.Stick.Deadzone);
                cfg.Stick.KeyUp = ReadKey(st, "keyUp", cfg.Stick.KeyUp);
                cfg.Stick.KeyDown = ReadKey(st, "keyDown", cfg.Stick.KeyDown);
                cfg.Stick.KeyLeft = ReadKey(st, "keyLeft", cfg.Stick.KeyLeft);
                cfg.Stick.KeyRight = ReadKey(st, "keyRight", cfg.Stick.KeyRight);
                cfg.Stick.KeyLock = ReadKey(st, "keyLock", cfg.Stick.KeyLock);
                cfg.Stick.LockRadius = ReadInt(st, "lockRadius", cfg.Stick.LockRadius);
                cfg.Stick.LockGap = ReadInt(st, "lockGap", cfg.Stick.LockGap);
            }

            // button.0 / button.1 ...
            List<string> names = new List<string>(ini.Keys);
            names.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (string section in names)
            {
                if (!section.StartsWith("button.", StringComparison.OrdinalIgnoreCase)) continue;
                Dictionary<string, string> b = ini[section];
                ButtonItem item = new ButtonItem();
                item.X = ReadInt(b, "x", item.X);
                item.Y = ReadInt(b, "y", item.Y);
                item.W = ReadInt(b, "w", item.W);
                item.H = ReadInt(b, "h", item.H);
                item.Label = ReadString(b, "label", item.Label);
                item.Vk = ReadKey(b, "key", item.Vk);
                cfg.Buttons.Add(item);
            }

            if (cfg.Buttons.Count == 0)
            {
                OverlayForm.Log("【配置异常】配置中没有有效按键，已恢复默认控件");
                cfg.Seed();
            }
            return cfg;
        }

        private void Seed()
        {
            Stick.CX = 200;
            Stick.CY = 700;
            Stick.Radius = 130;

            Buttons.Clear();
            Buttons.Add(NewButton(1100, 700, 110, "跳", 0x20));
            Buttons.Add(NewButton(1220, 600, 100, "攻", 0x31));
            Buttons.Add(NewButton(1220, 820, 100, "技", 0x32));
        }

        private static ButtonItem NewButton(int x, int y, int size, string label, int vk)
        {
            ButtonItem b = new ButtonItem();
            b.X = x; b.Y = y; b.W = size; b.H = size;
            b.Label = label; b.Vk = vk;
            return b;
        }

        public void Save(string path)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("; TouchKeys 配置 —— 可以直接用记事本改，改完在菜单里点 \"重新载入\"");
            sb.AppendLine("; 按键名可用: A-Z 0-9 F1-F12 SPACE ENTER ESC TAB SHIFT CTRL ALT UP DOWN LEFT RIGHT");
            sb.AppendLine(";            也可直接写十六进制虚拟键码，例如 0x41");
            sb.AppendLine();
            sb.AppendLine("[app]");
            sb.AppendLine("showGear=" + (ShowGear ? "1" : "0"));
            sb.AppendLine("gearSize=" + GearSize.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("opacity=" + Opacity.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("autoStart=" + (AutoStart ? "1" : "0"));
            sb.AppendLine("toggleModifiers=" + ToggleModifiers.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("toggleKey=" + ToggleKey.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine();
            sb.AppendLine("[stick]");
            sb.AppendLine("cx=" + Stick.CX.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("cy=" + Stick.CY.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("radius=" + Stick.Radius.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("deadzone=" + Stick.Deadzone.ToString("0.00", CultureInfo.InvariantCulture));
            sb.AppendLine("keyUp=" + KeyNames.ToName(Stick.KeyUp));
            sb.AppendLine("keyDown=" + KeyNames.ToName(Stick.KeyDown));
            sb.AppendLine("keyLeft=" + KeyNames.ToName(Stick.KeyLeft));
            sb.AppendLine("keyRight=" + KeyNames.ToName(Stick.KeyRight));
            sb.AppendLine("; 锁定后持续发送的键（默认 W = 一直前进）");
            sb.AppendLine("keyLock=" + KeyNames.ToName(Stick.KeyLock));
            sb.AppendLine("lockRadius=" + Stick.LockRadius.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("lockGap=" + Stick.LockGap.ToString(CultureInfo.InvariantCulture));

            for (int i = 0; i < Buttons.Count; i++)
            {
                ButtonItem b = Buttons[i];
                sb.AppendLine();
                sb.AppendLine("[button." + i.ToString(CultureInfo.InvariantCulture) + "]");
                sb.AppendLine("x=" + b.X.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("y=" + b.Y.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("w=" + b.W.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("h=" + b.H.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("label=" + b.Label);
                sb.AppendLine("key=" + KeyNames.ToName(b.Vk));
            }

            try
            {
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
            }
            catch (Exception ex)
            {
                // 配置写不进去不该让程序崩，运行期配置仍然有效
                OverlayForm.Log("【配置异常】保存失败 " + path + "：" + ex.Message);
            }
        }

        // ---------- INI 读取 ----------

        private static Dictionary<string, Dictionary<string, string>> ReadIni(string path)
        {
            Dictionary<string, Dictionary<string, string>> result =
                new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            Dictionary<string, string> current = null;

            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                if (line[0] == ';' || line[0] == '#') continue;

                if (line[0] == '[' && line[line.Length - 1] == ']')
                {
                    string name = line.Substring(1, line.Length - 2).Trim();
                    current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    result[name] = current;
                    continue;
                }

                if (current == null) continue;

                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim();
                string val = line.Substring(eq + 1).Trim();
                current[key] = val;
            }

            return result;
        }

        private static bool ReadBool(Dictionary<string, string> d, string key, bool fallback)
        {
            string v;
            if (!d.TryGetValue(key, out v)) return fallback;
            return v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        private static int ReadInt(Dictionary<string, string> d, string key, int fallback)
        {
            string v;
            if (!d.TryGetValue(key, out v)) return fallback;
            int n;
            if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) return n;
            return fallback;
        }

        private static double ReadDouble(Dictionary<string, string> d, string key, double fallback)
        {
            string v;
            if (!d.TryGetValue(key, out v)) return fallback;
            double n;
            if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out n)) return n;
            return fallback;
        }

        private static string ReadString(Dictionary<string, string> d, string key, string fallback)
        {
            string v;
            if (!d.TryGetValue(key, out v)) return fallback;
            return v.Length == 0 ? fallback : v;
        }

        private static int ReadKey(Dictionary<string, string> d, string key, int fallback)
        {
            string v;
            if (!d.TryGetValue(key, out v)) return fallback;
            int vk;
            if (KeyNames.TryParse(v, out vk)) return vk;
            return fallback;
        }
    }

    // ========================================================================
    //  按键捕获对话框（有焦点时监听 KeyDown，不用全局钩子）
    // ========================================================================
    internal class KeyDialog : Form
    {
        private readonly TextBox _keyBox;
        private readonly TextBox _labelBox;
        private readonly Label _hint;
        private readonly Button _captureBtn;

        /// <summary>是否正在等待用户按下实体键。</summary>
        private bool _capturing;

        public int ResultVk = 0;
        public string ResultLabel = "";

        public KeyDialog(string currentKey, string currentLabel)
        {
            Text = "设置按键";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(380, 196);
            Font = new Font("Microsoft YaHei UI", 9f);
            BackColor = Color.FromArgb(250, 250, 252);

            Label l1 = new Label();
            l1.Text = "按键（可直接输入名称或十六进制码）";
            l1.SetBounds(16, 12, 340, 20);
            Controls.Add(l1);

            _keyBox = new TextBox();
            _keyBox.Text = currentKey;
            _keyBox.SetBounds(16, 34, 230, 26);
            Controls.Add(_keyBox);

            _captureBtn = new Button();
            _captureBtn.Text = "捕获按键";
            _captureBtn.SetBounds(254, 33, 102, 28);
            _captureBtn.Click += OnCaptureClick;
            Controls.Add(_captureBtn);

            Label l2 = new Label();
            l2.Text = "按钮上显示的文字（可留空，自动用键名）";
            l2.SetBounds(16, 70, 340, 20);
            Controls.Add(l2);

            _labelBox = new TextBox();
            _labelBox.Text = currentLabel;
            _labelBox.SetBounds(16, 92, 340, 26);
            Controls.Add(_labelBox);

            _hint = new Label();
            _hint.Text = "提示：点「捕获按键」后按下实体键即可自动填入";
            _hint.ForeColor = Color.DimGray;
            _hint.SetBounds(16, 124, 340, 20);
            Controls.Add(_hint);

            Button ok = new Button();
            ok.Text = "确定";
            ok.SetBounds(200, 154, 78, 28);
            ok.Click += delegate { Accept(); };
            Controls.Add(ok);

            Button cancel = new Button();
            cancel.Text = "取消";
            cancel.SetBounds(284, 154, 78, 28);
            cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;

            // 捕获状态下光标离开输入框就自动结束，避免一直吞键
            Deactivate += delegate { StopCapture(); };
        }

        private void OnCaptureClick(object sender, EventArgs e)
        {
            if (_capturing) { StopCapture(); return; }

            _capturing = true;
            _captureBtn.Text = "按任意键…";
            _captureBtn.BackColor = Color.FromArgb(255, 236, 179);
            _hint.Text = "正在捕获：请按下要绑定的实体键（Esc 取消）";
            _hint.ForeColor = Color.FromArgb(200, 100, 0);
            _keyBox.Focus();
        }

        private void StopCapture()
        {
            if (!_capturing) return;
            _capturing = false;
            _captureBtn.Text = "捕获按键";
            _captureBtn.BackColor = SystemColors.Control;
            _hint.Text = "提示：点「捕获按键」后按下实体键即可自动填入";
            _hint.ForeColor = Color.DimGray;
        }

        /// <summary>
        /// 只有处于捕获状态时才拦截按键，其余时候让输入框正常打字。
        /// 这是之前"按钮名称打不进去"的根因：原来无条件吞掉所有按键。
        /// </summary>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            Keys mods = keyData & Keys.Modifiers;

            if (!_capturing)
            {
                if (key == Keys.Escape && mods == Keys.None)
                {
                    DialogResult = DialogResult.Cancel;
                    return true;
                }
                return base.ProcessCmdKey(ref msg, keyData);
            }

            // ---- 以下只在捕获状态执行 ----

            if (key == Keys.Escape && mods == Keys.None)
            {
                StopCapture();
                return true;
            }

            // 只接受主键，不接受单独按下的修饰键
            if (key == Keys.ShiftKey || key == Keys.ControlKey || key == Keys.Menu ||
                key == Keys.LShiftKey || key == Keys.RShiftKey ||
                key == Keys.LControlKey || key == Keys.RControlKey ||
                key == Keys.LMenu || key == Keys.RMenu || key == Keys.LWin ||
                key == Keys.RWin)
            {
                return true;
            }

            int code = (int)key;
            if (code > 0 && code < 256)
            {
                string name = KeyNames.ToName(code);
                if (name.StartsWith("0x"))
                    name = "0x" + code.ToString("X2");

                _keyBox.Text = name;
                if (_labelBox.Text.Length == 0 || _labelBox.Text == "新")
                    _labelBox.Text = name;

                StopCapture();
                return true;
            }
            return true;
        }

        private void Accept()
        {
            int vk;
            if (!KeyNames.TryParse(_keyBox.Text, out vk))
            {
                MessageBox.Show(this, "无法识别的按键：" + _keyBox.Text +
                    "\n\n可以用 A-Z、0-9、F1-F12、SPACE、ENTER、UP 等名字，\n" +
                    "也可以直接写十六进制虚拟键码，例如 0x41。",
                    "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _keyBox.Focus();
                _keyBox.SelectAll();
                return;
            }

            ResultVk = vk;
            string label = _labelBox.Text.Trim();
            ResultLabel = label.Length == 0 ? KeyNames.ToName(vk) : label;
            DialogResult = DialogResult.OK;
        }
    }

    // ========================================================================
    //  主窗口
    // ========================================================================
    internal class OverlayForm : Form
    {
        private const int HotkeyId = 0x5043;
        private const int ToggleHotkeyId = 0x5044;

        private readonly string _configPath;
        private Config _cfg;

        private bool _editMode;
        private Rectangle _screenBounds;

        private readonly Dictionary<int, Point> _touches = new Dictionary<int, Point>();
        private int _stickTouch = -2;                       // -2 表示没有
        private readonly Dictionary<int, int> _touchToButton = new Dictionary<int, int>();

        /// <summary>当前确实按在某个按钮内部的触点集合（用于精确计数）。</summary>
        private readonly HashSet<int> _buttonTouchInside = new HashSet<int>();

        private readonly HashSet<int> _pressedKeys = new HashSet<int>();
        private readonly HashSet<int> _wantedKeys = new HashSet<int>();

        private OverlayItem _dragItem;
        private Point _dragLast;
        private bool _dragging;
        private bool _touchActive;
        private int _touchContacts;
        private DateTime _lastTouchUtc = DateTime.MinValue;

        private string _status = "";
        private NotifyIcon _tray;
        private bool _hidden;
        private bool _hotkeyRegistered;
        private bool _toggleHotkeyRegistered;
        private readonly Dictionary<int, DateTime> _activeContacts = new Dictionary<int, DateTime>();
        private Timer _contactWatchdog;

        private Native.BITMAPINFO _bitmapInfo;
        private IntPtr _dibBitsPtr = IntPtr.Zero;

        /// <summary>--selftest：渲染一次后统计 alpha 并退出，用于验证穿透。</summary>
        public static bool SelfTestRequested;

        /// <summary>--debug：把鼠标/触摸/按键事件写进 touchkeys.log，用于排查。</summary>
        public static bool DebugEnabled;

        /// <summary>--multitouch：启动后直接跑多指状态自测并退出。</summary>
        public static bool MultiTouchSelfTest;

        internal static void Log(string msg)
        {
            // 异常相关一定要记下来（即使没开 --debug），否则崩了查不了
            bool important = msg.StartsWith("【") || msg.Contains("异常");
            if (!DebugEnabled && !important) return;
            try
            {
                File.AppendAllText("touchkeys.log",
                    DateTime.Now.ToString("HH:mm:ss.fff") + " " + msg + Environment.NewLine,
                    Encoding.UTF8);
            }
            catch
            {
            }
        }

        public OverlayForm(string configPath)
        {
            _configPath = configPath;
            _cfg = Config.Load(configPath);

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            SetStyle(ControlStyles.Opaque, true);
            SetStyle(ControlStyles.OptimizedDoubleBuffer, false);
            BackColor = Color.Black;

            // 用实际窗口能到达的范围，而不是 VirtualScreen。
            // VirtualScreen 在 DPI 感知模式下可能返回比实际大的值（例如 1920x1200
            // 而实际只有 1536x960），那样画布尺寸和窗口尺寸会对不上，
            // 导致控件被画到窗口外，该位置 alpha=0、点击直接穿透。
            Rectangle vs = GetPhysicalScreenBounds();
            _screenBounds = vs;
            OverlayItem.ScreenWidth = vs.Width;
            OverlayItem.ScreenHeight = vs.Height;
            Bounds = vs;

            Load += OnFormLoad;
            FormClosing += OnFormClosing;
            Deactivate += delegate { if (!_editMode) ReleaseAllTouches(); };
        }

        /// <summary>
        /// 拿物理像素的屏幕范围。优先用 GetSystemMetrics(SM_CXVIRTUALSCREEN)，
        /// 拿不到再退回 SystemInformation.VirtualScreen。
        /// </summary>
        private static Rectangle GetPhysicalScreenBounds()
        {
            try
            {
                int w = Native.GetSystemMetrics(Native.SM_CXVIRTUALSCREEN);
                int h = Native.GetSystemMetrics(Native.SM_CYVIRTUALSCREEN);
                int x = Native.GetSystemMetrics(Native.SM_XVIRTUALSCREEN);
                int y = Native.GetSystemMetrics(Native.SM_YVIRTUALSCREEN);

                if (w > 0 && h > 0) return new Rectangle(x, y, w, h);
            }
            catch
            {
            }

            return SystemInformation.VirtualScreen;
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_LAYERED;
                cp.ExStyle |= Native.WS_EX_TOOLWINDOW;
                cp.ExStyle |= Native.WS_EX_TOPMOST;
                // 关键：绝不激活。否则一点摇杆就抢走焦点，游戏立刻切出去，
                // 表现为"按下摇杆画面就跳到摇杆自己的界面"。
                cp.ExStyle |= Native.WS_EX_NOACTIVATE;
                return cp;
            }
        }

        private void OnFormLoad(object sender, EventArgs e)
        {
            // TWF_FINETOUCH (0x1)：更精细的触摸采样
            // 不要带 TWF_WANTPALM (0x2)，否则 Windows 会把多点压成单点（掌拒）
            try
            {
                Native.EnableMouseInPointer(false);
            }
            catch
            {
            }
            bool ok = Native.RegisterTouchWindow(Handle, 0x1);
            Log("RegisterTouchWindow = " + ok + " , MaxTouchPoints=" +
                Native.GetSystemMetrics(95) + " , Digitizer=" + Native.GetSystemMetrics(94));
            if (!ok) Log("RegisterTouchWindow 失败，错误码 " + Marshal.GetLastWin32Error());
            _hotkeyRegistered = Native.RegisterHotKey(Handle, HotkeyId,
                Native.MOD_CONTROL | Native.MOD_ALT, 0x71); // Ctrl+Alt+F2
            RegisterToggleHotkey();
            UpdateAutoStart(false);
            CreateTrayIcon();
            _contactWatchdog = new Timer();
            _contactWatchdog.Interval = 500;
            _contactWatchdog.Tick += delegate { CheckStaleContacts(); };
            _contactWatchdog.Start();

            RequestRender();

            if (MultiTouchSelfTest)
            {
                SelfTestMultiTouch();
                Application.ExitThread();
            }
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            ReleaseAllKeys();
            foreach (ButtonItem button in _cfg.Buttons)
            {
                if (button.AnimationTimer != null)
                {
                    button.AnimationTimer.Stop();
                    button.AnimationTimer.Dispose();
                    button.AnimationTimer = null;
                }
            }
            Native.UnregisterHotKey(Handle, HotkeyId);
            Native.UnregisterHotKey(Handle, ToggleHotkeyId);
            if (_tray != null) { _tray.Visible = false; _tray.Dispose(); _tray = null; }
            if (_contactWatchdog != null) { _contactWatchdog.Stop(); _contactWatchdog.Dispose(); }
            ReleaseGdi();
            if (_canvas != null) { _canvas.Dispose(); _canvas = null; }
        }

        // ====================================================================
        //  渲染
        // ====================================================================

        // ---- 渲染优化用的状态 ----
        private Bitmap _canvas;              // 复用位图，不再每帧 new
        private Rectangle _dirty;            // 本帧需要更新的区域
        private bool _dirtyAll = true;       // 需要整屏重绘
        private bool _renderPending;         // 已排队但还没画
        private Point _lastKnobPos = new Point(-1, -1);
        private bool _lastLocked;

        /// <summary>某个按钮所占的区域（留余量给投影和描边）。</summary>
        private Rectangle ButtonDirtyRect(int index)
        {
            if (index < 0 || index >= _cfg.Buttons.Count) return Rectangle.Empty;
            Rectangle r = _cfg.Buttons[index].Bounds;
            r.Inflate(10, 10);
            return r;
        }

        /// <summary>摇杆 + 锁定扣所占的区域（留点余量给阴影和描边）。</summary>
        private Rectangle StickDirtyRect()
        {
            StickItem s = _cfg.Stick;
            int pad = 12;
            Rectangle r = new Rectangle(s.CX - s.Radius - pad, s.CY - s.Radius - pad,
                (s.Radius + pad) * 2, (s.Radius + pad) * 2);

            // 锁定扣在正上方，也要并进来
            Rectangle lockR = s.LockBounds;
            lockR.Inflate(8, 8);
            return Rectangle.Union(r, lockR);
        }

        private void AnimateButton(int index, bool pressed)
        {
            if (index < 0 || index >= _cfg.Buttons.Count) return;
            ButtonItem button = _cfg.Buttons[index];
            if (button.AnimationTimer != null) { button.AnimationTimer.Stop(); button.AnimationTimer.Dispose(); button.AnimationTimer = null; }
            double start = button.PressScale;
            double target = pressed ? 0.84 : 1.0;
            int step = 0;
            Timer timer = new Timer();
            timer.Interval = 16;
            timer.Tick += delegate
            {
                step++;
                double t = Math.Min(1.0, step / 9.0);
                // 弹性缓动：按下缩小，松开稍微过冲再回到原尺寸。
                double eased = pressed ? (1.0 - Math.Pow(1.0 - t, 3.0)) : (t < 0.75 ? (1.12 * t) : (1.0 + 0.12 * (1.0 - (t - 0.75) / 0.25)));
                button.PressScale = start + (target - start) * Math.Min(1.0, Math.Max(0.0, eased));
                int currentIndex = _cfg.Buttons.IndexOf(button);
                if (currentIndex < 0) { timer.Stop(); timer.Dispose(); button.AnimationTimer = null; return; }
                MarkDirty(ButtonDirtyRect(currentIndex));
                RequestRender();
                if (t >= 1.0)
                {
                    button.PressScale = target;
                    timer.Stop(); timer.Dispose(); button.AnimationTimer = null;
                }
            };
            button.AnimationTimer = timer;
            timer.Start();
        }

        /// <summary>把一块区域标记为需要重绘。</summary>
        private void MarkDirty(Rectangle r)
        {
            if (_dirtyAll) return;
            if (_dirty.IsEmpty) _dirty = r;
            else _dirty = Rectangle.Union(_dirty, r);
        }

        /// <summary>整个界面都要重来（配置变更、模式切换等）。</summary>
        private void InvalidateAll()
        {
            _dirtyAll = true;
        }

        /// <summary>
        /// 请求重绘。
        ///
        /// 关键优化：不再每收到一条触点消息就立刻同步画一遍，
        /// 而是标记"待重绘"，由计时器按固定节奏（约 60fps）合并刷新。
        /// 多指同时操作时消息会成倍增加，原来的写法会画到手软。
        /// </summary>
        private void RequestRender()
        {
            if (_renderPending) return;
            _renderPending = true;

            if (_renderTimer == null)
            {
                _renderTimer = new Timer();
                _renderTimer.Interval = 16;   // ~60fps
                _renderTimer.Tick += delegate
                {
                    _renderTimer.Stop();
                    _renderPending = false;
                    Render(true);
                };
            }
            _renderTimer.Start();
        }

        private Timer _renderTimer;

        private void Render()
        {
            Render(false);
        }

        /// <param name="incremental">true = 只更新脏区；false = 整屏重画</param>
        private void Render(bool incremental)
        {
            if (!IsHandleCreated) return;

            int w = _screenBounds.Width;
            int h = _screenBounds.Height;
            if (w <= 0 || h <= 0) return;

            bool needNewCanvas = _canvas == null ||
                                 _canvas.Width != w || _canvas.Height != h;

            if (needNewCanvas || _dirtyAll || !incremental)
            {
                if (needNewCanvas)
                {
                    if (_canvas != null) _canvas.Dispose();
                    _canvas = new Bitmap(w, h, PixelFormat.Format32bppArgb);
                    _lastKnobPos = new Point(-1, -1);
                }

                _bitmapInfo = new Native.BITMAPINFO();
                _bitmapInfo.bmiHeader.biSize = Marshal.SizeOf(typeof(Native.BITMAPINFOHEADER));
                _bitmapInfo.bmiHeader.biWidth = w;
                _bitmapInfo.bmiHeader.biHeight = -h;
                _bitmapInfo.bmiHeader.biPlanes = 1;
                _bitmapInfo.bmiHeader.biBitCount = 32;
                _bitmapInfo.bmiHeader.biCompression = 0;

                using (Graphics g = Graphics.FromImage(_canvas))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    g.Clear(Color.Transparent);

                        if (_editMode) DrawEditBackdrop(g, _screenBounds.Width, _screenBounds.Height);
                    DrawStick(g);
                    for (int i = 0; i < _cfg.Buttons.Count; i++) DrawButton(g, _cfg.Buttons[i], i);
                    if (_editMode) DrawEditHints(g, _screenBounds.Width, _screenBounds.Height);
                    else if (_status.Length > 0) DrawStatus(g, _screenBounds.Width, _screenBounds.Height);
                }

                if (SelfTestRequested) { DumpAlpha(_canvas); return; }

                _dirtyAll = false;
                _dirty = Rectangle.Empty;

                // 整屏更新时，脏区就是全屏
                PushLayered(_canvas, new Rectangle(0, 0, w, h));
                return;
            }

            // Animation and touch updates mark only the affected area. Rebuild
            // that area on the canvas before copying it into the layered window.
            Rectangle dirty = Rectangle.Intersect(_dirty, new Rectangle(0, 0, w, h));
            _dirty = Rectangle.Empty;
            if (dirty.IsEmpty) return;

            using (Graphics g = Graphics.FromImage(_canvas))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                g.SetClip(dirty);
                g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                using (SolidBrush clear = new SolidBrush(Color.Transparent))
                    g.FillRectangle(clear, dirty);
                g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceOver;

                if (_editMode) DrawEditBackdrop(g, w, h);
                DrawStick(g);
                for (int i = 0; i < _cfg.Buttons.Count; i++) DrawButton(g, _cfg.Buttons[i], i);
                if (_editMode) DrawEditHints(g, w, h);
                else if (_status.Length > 0) DrawStatus(g, w, h);
            }

            PushLayered(_canvas, dirty);
        }

        private void DumpAlpha(Bitmap bmp)
        {
            int w = bmp.Width, h = bmp.Height;
            int opaque = 0, clear = 0, partial = 0;

            for (int y = 0; y < h; y += 3)
            {
                for (int x = 0; x < w; x += 3)
                {
                    byte a = bmp.GetPixel(x, y).A;
                    if (a == 0) clear++;
                    else if (a == 255) opaque++;
                    else partial++;
                }
            }

            int total = opaque + clear + partial;
            Console.WriteLine("=== alpha 分布（决定触摸穿透范围）===");
            Console.WriteLine("  完全透明(穿透)    : " + clear + "  " + Pct(clear, total));
            Console.WriteLine("  部分透明(有内容)  : " + partial + "  " + Pct(partial, total));
            Console.WriteLine("  完全不透明(有内容): " + opaque + "  " + Pct(opaque, total));
            Console.WriteLine("  总采样: " + total);

            Console.WriteLine();
            Console.WriteLine("=== 关键点位 ===");
            Probe(bmp, "左上角(期望A=0)", 5, 5);
            Probe(bmp, "屏幕中央(期望A=0)", w / 2, h / 2);
            Probe(bmp, "右下角(期望A=0)", w - 5, h - 5);
            Probe(bmp, "摇杆中心(期望A>0)", _cfg.Stick.CX, _cfg.Stick.CY);
            if (_cfg.Buttons.Count > 0)
            {
                ButtonItem b = _cfg.Buttons[0];
                Probe(bmp, "按键#0(期望A>0)", b.X + b.W / 2, b.Y + b.H / 2);
            }

            try { bmp.Save("alpha-check.png", ImageFormat.Png); } catch { }
            Console.WriteLine();
            Console.WriteLine("alpha-check.png 已保存");
            Application.ExitThread();
        }

        private static string Pct(int n, int total)
        {
            if (total <= 0) return "0%";
            return (100.0 * n / total).ToString("0.00") + "%";
        }

        private static void Probe(Bitmap bmp, string label, int x, int y)
        {
            if (x < 0 || y < 0 || x >= bmp.Width || y >= bmp.Height) return;
            Color c = bmp.GetPixel(x, y);
            Console.WriteLine("  " + label.PadRight(20) + " (" + x + "," + y + ") A=" +
                c.A.ToString().PadRight(4) + " BGR=(" + c.B + "," + c.G + "," + c.R + ")");
        }

        // ---- 持久化的 GDI 资源：不再每帧创建/销毁（这是最大的热点）----
        private IntPtr _memDc = IntPtr.Zero;
        private IntPtr _hDib = IntPtr.Zero;
        private IntPtr _oldDib = IntPtr.Zero;
        private int _dibW, _dibH;

        /// <summary>把画好的内容推上分层窗口。GDI 资源全部复用。</summary>
        private void PushLayered(Bitmap bmp, Rectangle dirty)
        {
            int w = bmp.Width, h = bmp.Height;

            // 尺寸变了才重建 DIB 段
            if (_hDib == IntPtr.Zero || _dibW != w || _dibH != h)
            {
                ReleaseGdi();
                _bitmapInfo = new Native.BITMAPINFO();
                _bitmapInfo.bmiHeader.biSize = Marshal.SizeOf(typeof(Native.BITMAPINFOHEADER));
                _bitmapInfo.bmiHeader.biWidth = w;
                _bitmapInfo.bmiHeader.biHeight = -h;
                _bitmapInfo.bmiHeader.biPlanes = 1;
                _bitmapInfo.bmiHeader.biBitCount = 32;
                _bitmapInfo.bmiHeader.biCompression = 0;

                IntPtr screenDc = Native.GetDC(IntPtr.Zero);
                _memDc = Native.CreateCompatibleDC(screenDc);

                _hDib = Native.CreateDIBSection(_memDc, ref _bitmapInfo,
                    Native.DIB_RGB_COLORS, out _dibBitsPtr, IntPtr.Zero, 0);

                Native.ReleaseDC(IntPtr.Zero, screenDc);

                if (_hDib == IntPtr.Zero || _dibBitsPtr == IntPtr.Zero)
                {
                    ReleaseGdi();
                    return;
                }

                _oldDib = Native.SelectObject(_memDc, _hDib);
                _dibW = w;
                _dibH = h;

                // 重建后必须整屏刷一次
                dirty = new Rectangle(0, 0, w, h);
            }

            CopyPremultiplied(bmp, _dibBitsPtr, dirty);

            IntPtr dc = Native.GetDC(IntPtr.Zero);
            try
            {
                SIZE size = new SIZE(w, h);
                POINT src = new POINT(0, 0);
                POINT dst = new POINT(_screenBounds.X, _screenBounds.Y);

                BLENDFUNCTION blend = new BLENDFUNCTION();
                blend.BlendOp = Native.AC_SRC_OVER;
                blend.BlendFlags = 0;
                blend.SourceConstantAlpha = 255;
                blend.AlphaFormat = Native.AC_SRC_ALPHA;

                Native.UpdateLayeredWindow(Handle, dc, ref dst, ref size,
                    _memDc, ref src, 0, ref blend, Native.ULW_ALPHA);
            }
            finally
            {
                Native.ReleaseDC(IntPtr.Zero, dc);
            }
        }

        private void ReleaseGdi()
        {
            if (_memDc != IntPtr.Zero)
            {
                if (_oldDib != IntPtr.Zero) Native.SelectObject(_memDc, _oldDib);
                if (_hDib != IntPtr.Zero) Native.DeleteObject(_hDib);
                Native.DeleteDC(_memDc);
            }
            _memDc = IntPtr.Zero;
            _hDib = IntPtr.Zero;
            _oldDib = IntPtr.Zero;
            _dibBitsPtr = IntPtr.Zero;
            _dibW = 0;
            _dibH = 0;
        }

        // ---- 复用缓冲，避免每帧分配几 MB 数组（GC 压力是大头之一）----
        private byte[] _srcBuf = new byte[0];
        private byte[] _dstBuf = new byte[0];
        private int _bufW, _bufH;

        /// <summary>
        /// 把 GDI+ 画好的像素按"预乘 alpha"拷进 DIB 段。
        ///
        /// 性能要点：
        ///  · 复用两个 byte 缓冲，不再每帧 new（原来每帧分配约 12 MB）
        ///  · 只处理 dirty 区域，不是整屏 147 万像素全扫
        ///  · 用 unsafe 指针逐像素，比数组下标快一截
        /// </summary>
        private void CopyPremultiplied(Bitmap bmp, IntPtr dest, Rectangle dirty)
        {
            BitmapData bd = bmp.LockBits(
                new Rectangle(0, 0, bmp.Width, bmp.Height),
                ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

            try
            {
                int stride = bd.Stride;
                int w = bmp.Width;
                int h = bmp.Height;

                if (_bufW != w || _bufH != h || _srcBuf.Length < stride * h)
                {
                    _bufW = w;
                    _bufH = h;
                    _srcBuf = new byte[stride * h];
                    _dstBuf = new byte[w * h * 4];
                }

                // 只拷新的那一整张（Marshal.Copy 很快），预乘只算脏区
                Marshal.Copy(bd.Scan0, _srcBuf, 0, stride * h);

                // 裁剪脏区到图像边界
                int x0 = Math.Max(0, dirty.Left);
                int y0 = Math.Max(0, dirty.Top);
                int x1 = Math.Min(w, dirty.Right);
                int y1 = Math.Min(h, dirty.Bottom);
                if (x1 <= x0 || y1 <= y0) return;

                // 预乘 + 直写 DIB（dest 是 top-down，行距 = w*4）
                unsafe
                {
                    byte* sBase = (byte*)bd.Scan0;
                    byte* dBase = (byte*)dest;

                    for (int y = y0; y < y1; y++)
                    {
                        byte* srow = sBase + y * stride + x0 * 4;
                        byte* drow = dBase + y * w * 4 + x0 * 4;

                        for (int x = 0; x < (x1 - x0); x++)
                        {
                            byte a = srow[3];
                            if (a == 0)
                            {
                                drow[0] = 0; drow[1] = 0; drow[2] = 0; drow[3] = 0;
                            }
                            else if (a == 255)
                            {
                                drow[0] = srow[0]; drow[1] = srow[1];
                                drow[2] = srow[2]; drow[3] = 255;
                            }
                            else
                            {
                                drow[0] = (byte)(srow[0] * a / 255);
                                drow[1] = (byte)(srow[1] * a / 255);
                                drow[2] = (byte)(srow[2] * a / 255);
                                drow[3] = a;
                            }
                            srow += 4;
                            drow += 4;
                        }
                    }
                }
            }
            finally
            {
                bmp.UnlockBits(bd);
            }
        }

        private int Alpha(int baseAlpha)
        {
            return Math.Max(0, Math.Min(255, baseAlpha * _cfg.Opacity / 255));
        }

        private void DrawEditBackdrop(Graphics g, int w, int h)
        {
            // 编辑模式下铺一层极淡的底色，让整屏都能接收点击（否则空白处会穿透）
            using (SolidBrush b = new SolidBrush(Color.FromArgb(22, 40, 120, 220)))
                g.FillRectangle(b, 0, 0, w, h);
        }

        /// <summary>
        /// 摇杆绘制 —— 参考和平精英的触控摇杆：
        /// 磨砂半透明底盘 + 细亮边 + 方向刻度 + 带高光的摇杆帽 + 顶部锁定按钮。
        /// </summary>
        /// <summary>
        /// 摇杆绘制 —— 和平精英式：
        ///  · 平时只有一圈淡淡的轮廓，不抢视线
        ///  · 按下后外圈变亮，小圈（摇杆帽）跟着手指走
        ///  · 小圈圆心始终被限制在大圈内，永远不会"跑出去"
        /// </summary>
        private void DrawStick(Graphics g)
        {
            StickItem s = _cfg.Stick;
            bool active = s.Active || s.Locked;
            Rectangle outer = new Rectangle(s.CX - s.Radius, s.CY - s.Radius,
                s.Radius * 2, s.Radius * 2);

            if (s.Locked)
            {
                DrawLockButton(g);
                return;
            }

            // ---------------- 大圈 ----------------
            // ---------------- 小圈（摇杆帽） ----------------

            int kr = s.KnobRadius;
            int kx = s.KnobCX;
            int ky = s.KnobCY;

            Rectangle knob = new Rectangle(kx - kr, ky - kr, kr * 2, kr * 2);

            // 投影
            using (SolidBrush sh = new SolidBrush(Color.FromArgb(Alpha(active ? 70 : 40), 0, 0, 0)))
                g.FillEllipse(sh, new Rectangle(knob.X + 1, knob.Y + 3, knob.Width, knob.Height));

            // 主体：上亮下暗的渐变
            int topA = active ? 245 : 222;
            int botA = active ? 212 : 188;

            using (GraphicsPath clip = new GraphicsPath())
            {
                clip.AddEllipse(knob);
                GraphicsState gs = g.Save();
                g.SetClip(clip);

                using (LinearGradientBrush lg = new LinearGradientBrush(
                    knob,
                    Color.FromArgb(Alpha(topA), 252, 254, 255),
                    Color.FromArgb(Alpha(botA), 150, 178, 214),
                    LinearGradientMode.Vertical))
                {
                    g.FillRectangle(lg, knob);
                }

                // 顶部高光（和平精英摇杆帽那块明显的高光）
                int hw = (int)(kr * 0.92);
                int hh = Math.Max(5, (int)(kr * 0.48));
                Rectangle hi = new Rectangle(kx - hw / 2, ky - kr + (int)(kr * 0.26), hw, hh);
                using (SolidBrush hb = new SolidBrush(Color.FromArgb(Alpha(205), 255, 255, 255)))
                    g.FillEllipse(hb, hi);

                g.Restore(gs);
            }

            // 帽描边
            using (Pen kp = new Pen(Color.FromArgb(Alpha(active ? 235 : 205), 255, 255, 255),
                active ? 2.2f : 1.8f))
                g.DrawEllipse(kp, knob);

            if (s.Selected && _editMode)
            {
                using (Pen sel = new Pen(Color.Lime, 2f))
                {
                    sel.DashStyle = DashStyle.Dash;
                    g.DrawRectangle(sel, outer);
                }
            }

            DrawLockButton(g);
        }

        private void DrawLockButton(Graphics g)
        {
            StickItem s = _cfg.Stick;
            Rectangle r = s.LockBounds;

            using (SolidBrush sh = new SolidBrush(Color.FromArgb(Alpha(40), 0, 0, 0)))
                g.FillEllipse(sh, new Rectangle(r.X + 1, r.Y + 3, r.Width, r.Height));

            Color fill = s.Locked
                ? Color.FromArgb(Alpha(225), 62, 210, 140)
                : Color.FromArgb(Alpha(140), 20, 24, 34);

            using (SolidBrush b = new SolidBrush(fill))
                g.FillEllipse(b, r);

            using (Pen p = new Pen(Color.FromArgb(Alpha(215), 235, 245, 255), 2f))
                g.DrawEllipse(p, r);

            // 画一把小锁：锁体 + 锁梁
            int cx = r.X + r.Width / 2;
            int cy = r.Y + r.Height / 2;

            using (Pen lp = new Pen(Color.FromArgb(Alpha(240), 255, 255, 255), 2.2f))
            {
                lp.StartCap = LineCap.Round;
                lp.EndCap = LineCap.Round;

                // 锁梁
                int ark = (int)(r.Width * 0.19);
                Rectangle arc = new Rectangle(cx - ark, cy - ark - 4, ark * 2, ark * 2);
                g.DrawArc(lp, arc, 200, 140);

                // 锁体
                Rectangle body = new Rectangle(cx - (int)(r.Width * 0.20),
                    cy - 4, (int)(r.Width * 0.40), (int)(r.Height * 0.30));
                g.FillRectangle(new SolidBrush(Color.FromArgb(Alpha(240), 255, 255, 255)), body);
            }
        }

        private static Rectangle Inflate(Rectangle r, int d)
        {
            return new Rectangle(r.X - d, r.Y - d, r.Width + d * 2, r.Height + d * 2);
        }

        /// <summary>
        /// 按钮绘制 —— 参考和平精英的技能键：
        /// 半透明圆角方块 + 内高光 + 亮边，按下时高亮并轻微内缩。
        /// </summary>
        private void DrawButton(Graphics g, ButtonItem b, int index)
        {
            Rectangle r = b.Bounds;

            // 按下时轻微内缩，给手指一个反馈
            if (b.Pressed) r = Inflate(r, -3);
            int insetX = (int)(r.Width * (1.0 - b.PressScale) / 2.0);
            int insetY = (int)(r.Height * (1.0 - b.PressScale) / 2.0);
            r = new Rectangle(r.X + insetX, r.Y + insetY,
                Math.Max(1, r.Width - insetX * 2), Math.Max(1, r.Height - insetY * 2));

            int radius = Math.Min(r.Width, r.Height) / 2;

            // 投影
            Rectangle shadow = new Rectangle(r.X + 1, r.Y + 4, r.Width, r.Height);
            using (GraphicsPath sp = RoundedRect(shadow, radius))
            using (SolidBrush sb = new SolidBrush(Color.FromArgb(Alpha(46), 0, 0, 0)))
                g.FillPath(sb, sp);

            using (GraphicsPath path = RoundedRect(r, radius))
            {
                // 底色
                Color top = Color.FromArgb(Alpha(150), 26, 30, 42);
                Color bottom = Color.FromArgb(Alpha(125), 12, 14, 22);

                using (LinearGradientBrush lg = new LinearGradientBrush(
                    r, top, bottom, LinearGradientMode.Vertical))
                {
                    g.FillPath(lg, path);
                }

                // 上半部高光带
                GraphicsState gs = g.Save();
                g.SetClip(path);
                Rectangle hiArea = new Rectangle(r.X, r.Y, r.Width, r.Height / 2);
                using (LinearGradientBrush hb = new LinearGradientBrush(
                    hiArea, Color.FromArgb(Alpha(52), 255, 255, 255),
                    Color.FromArgb(0, 255, 255, 255), LinearGradientMode.Vertical))
                {
                    g.FillRectangle(hb, hiArea);
                }
                g.Restore(gs);

                // 描边
                using (Pen border = new Pen(Color.FromArgb(Alpha(215),
                    Color.FromArgb(200, 226, 240)),
                    2f))
                {
                    g.DrawPath(border, path);
                }
            }

            // 文字
            string text = string.IsNullOrEmpty(b.Label) ? KeyNames.ToName(b.Vk) : b.Label;
            float fontSize = Math.Max(10f, Math.Min(r.Height * 0.38f, 40f));
            using (Font f = new Font("Microsoft YaHei UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                StringFormat sf = new StringFormat();
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;

                // 文字阴影，避免浅色背景上看不清
                using (SolidBrush sh = new SolidBrush(Color.FromArgb(Alpha(110), 0, 0, 0)))
                    g.DrawString(text, f, sh, new Rectangle(r.X + 1, r.Y + 2, r.Width, r.Height), sf);

                using (SolidBrush tb = new SolidBrush(Color.FromArgb(Alpha(255), 255, 255, 255)))
                    g.DrawString(text, f, tb, r, sf);

                sf.Dispose();
            }

            if (_editMode)
            {
                using (Pen sel = new Pen(b.Selected ? Color.Lime : Color.FromArgb(120, 255, 255, 0), 2f))
                {
                    sel.DashStyle = DashStyle.Dash;
                    g.DrawRectangle(sel, b.Bounds);
                }

                using (Font f = new Font("Consolas", 11f, FontStyle.Regular, GraphicsUnit.Pixel))
                using (SolidBrush sb = new SolidBrush(Color.FromArgb(230, 255, 255, 0)))
                {
                    g.DrawString("#" + index.ToString(CultureInfo.InvariantCulture),
                        f, sb, b.Bounds.X, b.Bounds.Y - 16);
                }
            }
        }

        private void DrawGear(Graphics g)
        {
            int size = _cfg.GearSize;
            Rectangle r = new Rectangle(_screenBounds.Width - size - 8, 8, size, size);

            using (SolidBrush b = new SolidBrush(Color.FromArgb(Alpha(120), 20, 25, 35)))
                g.FillEllipse(b, r);
            using (Pen p = new Pen(Color.FromArgb(Alpha(200), 160, 210, 255), 1.6f))
                g.DrawEllipse(p, r);

            using (Font f = new Font("Segoe UI Symbol", size * 0.45f,
                FontStyle.Regular, GraphicsUnit.Pixel))
            using (SolidBrush tb = new SolidBrush(Color.FromArgb(Alpha(230), 200, 225, 255)))
            {
                StringFormat sf = new StringFormat();
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;
                g.DrawString("\u2699", f, tb, r, sf);
                sf.Dispose();
            }
        }

        private void DrawEditHints(Graphics g, int w, int h)
        {
            string[] lines = new string[]
            {
                "【编辑模式】拖动 = 移动　滚轮 = 缩放　右键 = 菜单　Esc = 退出",
                "摇杆映射: 上=" + KeyNames.ToName(_cfg.Stick.KeyUp) +
                    "  下=" + KeyNames.ToName(_cfg.Stick.KeyDown) +
                    "  左=" + KeyNames.ToName(_cfg.Stick.KeyLeft) +
                    "  右=" + KeyNames.ToName(_cfg.Stick.KeyRight),
                "空 白 区 域 在 游 玩 模 式 下 会 完 全 穿 透，不挡触摸"
            };

            float y = 20;
            using (Font f = new Font("Microsoft YaHei UI", 14f, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                foreach (string line in lines)
                {
                    SizeF sz = g.MeasureString(line, f);
                    RectangleF box = new RectangleF(16, y, sz.Width + 20, sz.Height + 10);
                    using (SolidBrush bg = new SolidBrush(Color.FromArgb(170, 0, 0, 0)))
                        g.FillRectangle(bg, box);
                    using (SolidBrush tb = new SolidBrush(Color.FromArgb(240, 255, 255, 255)))
                        g.DrawString(line, f, tb, 26, y + 5);
                    y += sz.Height + 16;
                }
            }
        }

        private void DrawStatus(Graphics g, int w, int h)
        {
            using (Font f = new Font("Microsoft YaHei UI", 13f, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                SizeF sz = g.MeasureString(_status, f);
                RectangleF box = new RectangleF((w - sz.Width) / 2f, h - 64, sz.Width + 24, sz.Height + 12);
                using (SolidBrush bg = new SolidBrush(Color.FromArgb(170, 0, 0, 0)))
                    g.FillRectangle(bg, box);
                using (SolidBrush tb = new SolidBrush(Color.FromArgb(240, 255, 255, 255)))
                    g.DrawString(_status, f, tb, box.X + 12, box.Y + 6);
            }
        }

        private static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = Math.Max(2, radius * 2);
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;

            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        // ====================================================================
        //  触摸 / 鼠标
        // ====================================================================

        protected override void WndProc(ref Message m)
        {
            // 触摸/指针相关的消息全部记下来（用于诊断真机为什么收不到多指）
            if (DebugEnabled)
            {
                if (m.Msg == Native.WM_TOUCH || m.Msg == 0x0240 ||
                    (m.Msg >= 0x0245 && m.Msg <= 0x024F) ||
                    m.Msg == 0x0200 || m.Msg == 0x0201 || m.Msg == 0x0202)
                {
                    Log("MSG 0x" + m.Msg.ToString("X4") + " wParam=" + m.WParam);
                }
            }

            // ---------------- 指针消息优先（每根手指独立 id）----------------
            if (m.Msg == Native.WM_POINTERDOWN ||
                m.Msg == Native.WM_POINTERUPDATE ||
                m.Msg == Native.WM_POINTERUP)
            {
                HandlePointer(m.Msg, m.WParam);
                m.Result = IntPtr.Zero;
                return;
            }

            // 指针要激活时保持不抢焦点
            if (m.Msg == Native.WM_POINTERACTIVATE)
            {
                m.Result = (IntPtr)Native.PA_NOACTIVATE;
                return;
            }

            // ---------------- 老的 WM_TOUCH（指针不可用时的后备）----------------
            if (m.Msg == Native.WM_TOUCH)
            {
                HandleTouch(m.WParam, m.LParam);
                m.Result = (IntPtr)1;
                return;
            }

            if (m.Msg == Native.WM_HOTKEY && m.WParam.ToInt32() == HotkeyId)
            {
                ToggleEditMode();
                return;
            }
            if (m.Msg == Native.WM_HOTKEY && m.WParam.ToInt32() == ToggleHotkeyId)
            {
                ToggleOverlayVisibility();
                return;
            }
            if (m.Msg == Native.WM_CANCELMODE || m.Msg == Native.WM_POINTERCAPTURECHANGED)
                ReleaseAllTouches();

            if (m.Msg == Native.WM_MOUSEACTIVATE)
            {
                m.Result = (IntPtr)Native.MA_NOACTIVATE;
                return;
            }

            base.WndProc(ref m);
        }

        /// <summary>
        /// 处理 WM_POINTER。
        ///
        /// 这是多指能同时工作的关键：每根手指有自己的 pointerId，
        /// 不会像 WM_TOUCH 那样被掌拒（TWF_WANTPALM）压成单点。
        /// 也不像合成鼠标事件那样全部挤在 id=-1 上。
        /// </summary>
        /// <summary>
        /// 处理 WM_POINTER。
        ///
        /// 这是多指能同时工作的关键：每根手指有自己的 pointerId。
        ///
        /// 重要：整条路径用 try/catch 包住。P/Invoke 到缺失的 API、
        /// 结构体布局不匹配都可能抛异常，而 WndProc 里未处理的异常会
        /// 弹出 .NET 崩溃对话框并杀掉程序 —— 输入层绝不能把主程序拖死。
        /// </summary>
        private void HandlePointer(int msg, IntPtr wParam)
        {
            try
            {
                uint id = (uint)(wParam.ToInt64() & Native.POINTER_ID_MASK);
                if (id == 0) return;

                // 先取基础信息：用它判断指针类型，也拿到坐标
                Native.POINTER_INFO baseInfo = new Native.POINTER_INFO();
                if (!Native.GetPointerInfo(id, ref baseInfo)) return;

                // 只关心触摸和笔；合成鼠标交给 OnMouse 系列处理，避免重复
                if (baseInfo.pointerType == Native.PT_MOUSE) return;

                Point screen = new Point(baseInfo.ptPixelLocation.X, baseInfo.ptPixelLocation.Y);
                Point local = new Point(screen.X - _screenBounds.X,
                                        screen.Y - _screenBounds.Y);

                // 触摸的话，尝试拿更精确的触点信息（失败也无所谓，用基础坐标即可）
                if (baseInfo.pointerType == Native.PT_TOUCH)
                {
                    Native.POINTER_TOUCH_INFO touch = new Native.POINTER_TOUCH_INFO();
                    if (Native.GetPointerTouchInfo(id, ref touch))
                    {
                        local = new Point(touch.pointerInfo.ptPixelLocation.X - _screenBounds.X,
                                          touch.pointerInfo.ptPixelLocation.Y - _screenBounds.Y);
                    }
                }

                // pointerId 高位标记，避免和鼠标的 -1 冲突
                int contact = (int)(id | 0x10000);

                _lastTouchUtc = DateTime.UtcNow;
                _touchActive = true;
                _lastTouchPoint = local;

                if (msg == Native.WM_POINTERDOWN)
                {
                    if (DebugEnabled) Log("POINTER DOWN id=" + id + " type=" +
                        baseInfo.pointerType + " at " + local.X + "," + local.Y);
                    ContactDown(contact, local);
                }
                else if (msg == Native.WM_POINTERUP)
                {
                    if (DebugEnabled) Log("POINTER UP id=" + id);
                    ContactUp(contact);
                }
                else
                {
                    ContactMove(contact, local);
                }

                RequestRender();
            }
            catch (Exception ex)
            {
                // 指针处理失败绝不能让程序崩掉，记一笔继续跑
                Log("HandlePointer 异常: " + ex.GetType().Name + " " + ex.Message);
            }
        }

        private void HandleTouch(IntPtr hTouch, IntPtr lParam)
        {
            int count = (int)(lParam.ToInt64() & 0xFFFF);
            TOUCHINPUT[] inputs = new TOUCHINPUT[count];

            if (!Native.GetTouchInputInfo(hTouch, count, inputs, Marshal.SizeOf(typeof(TOUCHINPUT))))
                return;

            _lastTouchUtc = DateTime.UtcNow;
            _touchActive = true;
            _touchContacts = 0;

            if (count > 0)
            {
                TOUCHINPUT first = inputs[0];
                _lastTouchPoint = new Point(
                    (first.x / 100) - _screenBounds.X,
                    (first.y / 100) - _screenBounds.Y);
            }

            try
            {
                for (int i = 0; i < count; i++)
                {
                    TOUCHINPUT ti = inputs[i];
                    int id = ti.dwID;

                    Point screen = new Point(ti.x / 100, ti.y / 100);
                    Point local = new Point(screen.X - _screenBounds.X, screen.Y - _screenBounds.Y);

                    bool down = (ti.dwFlags & (int)Native.TOUCHEVENTF_DOWN) != 0;
                    bool up = (ti.dwFlags & (int)Native.TOUCHEVENTF_UP) != 0;

                    if (DebugEnabled)
                    {
                        Log("TOUCH id=" + id + " local=" + local.X + "," + local.Y +
                            " flags=0x" + ti.dwFlags.ToString("X") +
                            " down=" + down + " up=" + up +
                            " (本批触点总数=" + count + ")");
                    }

                    if (down)
                    {
                        _touchContacts++;
                        ContactDown(id, local);
                    }
                    else if (up)
                    {
                        ContactUp(id);
                    }
                    else
                    {
                        ContactMove(id, local);
                    }
                }
            }
            finally
            {
                Native.CloseTouchInputHandle(hTouch);
                RequestRender();
            }
        }

        /// <summary>最近一次触摸的位置（用于识别"合成的鼠标事件"）。</summary>
        private Point _lastTouchPoint = new Point(-9999, -9999);

        /// <summary>
        /// 判断这次鼠标事件是不是触摸合成出来的。
        ///
        /// 旧实现是"1.2 秒内发生过任何触摸就压掉所有鼠标事件"，
        /// 这会连真正的第二根手指也一起压掉 —— 表现就是多指完全失效。
        ///
        /// 现在要求：时间很近（<400ms）**且**位置几乎重合（<20px）才算合成事件。
        /// 这样真鼠标操作、以及另一根手指在别处的输入都不会被误伤。
        /// </summary>
        private bool IsSynthesizedMouse(Point p)
        {
            long extra = Native.GetMessageExtraInfo().ToInt64();
            return (extra & Native.MI_WP_MASK) == Native.MI_WP_SIGNATURE;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (IsSynthesizedMouse(e.Location)) return;

            if (e.Button == MouseButtons.Left)
            {
                ContactDown(-1, e.Location);
                if (_editMode && _dragging) Native.SetCapture(Handle);
            }
            else if (e.Button == MouseButtons.Right) ShowContextMenu(e.Location);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (IsSynthesizedMouse(e.Location) && !_dragging) return;

            // 编辑模式：拖动选中的控件
            if (_dragging)
            {
                ContactMove(-1, e.Location);
                return;
            }

            // 游玩模式：鼠标按住的摇杆/按钮也要跟随（否则只有触摸能用）
            if (_stickTouch == -1 || _touchToButton.ContainsKey(-1))
                ContactMove(-1, e.Location);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            ContactUp(-1);
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (IsHandleCreated && Native.GetCapture() != Handle && _dragging) ContactUp(-1);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (!_editMode) return;

            Point local = e.Location;
            OverlayItem item = HitItem(local);
            if (item == null) item = _cfg.Stick;
            if (item == null) return;

            item.Resize(e.Delta > 0 ? 8 : -8);
            _cfg.Save(_configPath);
            RequestRender();
        }

        private void ContactDown(int id, Point p)
        {
            Log("ContactDown id=" + id + " at " + p.X + "," + p.Y + " edit=" + _editMode
                + " stickHit=" + _cfg.Stick.HitTest(p) + " stickTouch=" + _stickTouch);

            if (_editMode)
            {
                OverlayItem hit = HitItem(p);

                if (hit == null && id == -1 && (Native.GetAsyncKeyState(0x02) & 0x8000) != 0)
                { ShowContextMenu(p); return; }

                foreach (ButtonItem b in _cfg.Buttons) b.Selected = false;
                _cfg.Stick.Selected = false;

                if (hit != null)
                {
                    _activeContacts[id] = DateTime.UtcNow;
                    hit.Selected = true;
                    _dragItem = hit;
                    _dragLast = p;
                    _dragging = true;
                    if (id == -1) Native.SetCapture(Handle);
                }
                InvalidateAll();
                RequestRender();
                return;
            }

            // 游玩模式：先判按钮（小的优先），再判摇杆
            for (int i = 0; i < _cfg.Buttons.Count; i++)
            {
                if (_cfg.Buttons[i].HitTest(p))
                {
                    _activeContacts[id] = DateTime.UtcNow;
                    _cfg.Buttons[i].PressIn();
                    AnimateButton(i, true);
                    _buttonTouchInside.Add(id);
                    _touchToButton[id] = i;
                    Recompute();
                    MarkDirty(ButtonDirtyRect(i));
                    RequestRender();
                    return;
                }
            }

            // 锁定按钮本身：点一下不产生任何效果（按需求：直接点击无效）
            // 只有把摇杆拖到它上面才会锁定。所以这里不做任何事。
            if (_cfg.Stick.LockHitTest(p)) return;

            // 摇杆是"单指专属"的，但按钮可以同时有多指。
            // 这里只在没人占用摇杆时抢占（_stickTouch == -2）。
            if (_cfg.Stick.HitTest(p) && _stickTouch == -2)
            {
                _activeContacts[id] = DateTime.UtcNow;
                // SetCapture 只对鼠标有意义；触摸用 WM_TOUCH 自己的跟踪
                _stickTouch = id;

                // 已经锁定的话，按住摇杆就是解锁（不然解不开）
                if (_cfg.Stick.Locked)
                {
                    _cfg.Stick.Locked = false;
                    Flash("已解除锁定");
                }

                _cfg.Stick.BeginGrab(p);
                Recompute();
                MarkDirty(StickDirtyRect());
                RequestRender();
            }
        }

        private void ContactMove(int id, Point p)
        {
            _activeContacts[id] = DateTime.UtcNow;
            if (_stickTouch == id) Log("ContactMove stick id=" + id + " at " + p.X + "," + p.Y);

            if (_editMode)
            {
                if (_dragging && _dragItem != null)
                {
                    Rectangle oldBounds = _dragItem.Bounds;
                    _dragItem.Move(p.X - _dragLast.X, p.Y - _dragLast.Y);
                    _dragLast = p;
                    Rectangle dirty = Rectangle.Union(oldBounds, _dragItem.Bounds);
                    if (_dragItem is StickItem) dirty = Rectangle.Union(dirty, StickDirtyRect());
                    dirty.Inflate(12, 12);
                    MarkDirty(dirty);
                    RequestRender();
                }
                return;
            }

            if (_stickTouch == id)
            {
                // 拖到锁定按钮上 -> 锁定（一直前进）
                if (_cfg.Stick.LockHitTest(p))
                {
                    if (!_cfg.Stick.Locked)
                    {
                        _cfg.Stick.Locked = true;
                        Flash("已锁定：持续 " + KeyNames.ToName(_cfg.Stick.KeyLock));
                    }
                    Recompute();
                    MarkDirty(StickDirtyRect());
                    RequestRender();
                    return;
                }

                // 拖着摇杆在其它位置移动时，若已锁定则解除锁定
                if (_cfg.Stick.Locked) _cfg.Stick.Locked = false;

                _cfg.Stick.UpdateFromPoint(p);
                Recompute();
                // 只有摇杆区域需要重画（含上方锁定扣）
                MarkDirty(StickDirtyRect());
                RequestRender();
                return;
            }

            int index;
            if (_touchToButton.TryGetValue(id, out index))
            {
                if (index < 0 || index >= _cfg.Buttons.Count) { ContactUp(id); return; }
                ButtonItem b = _cfg.Buttons[index];
                bool inside = b.HitTest(p);

                if (inside && !_buttonTouchInside.Contains(id))
                {
                    _buttonTouchInside.Add(id);
                    b.PressIn();
                    AnimateButton(index, true);
                    Recompute();
                    MarkDirty(ButtonDirtyRect(index));
                    RequestRender();
                }
                else if (!inside && _buttonTouchInside.Contains(id))
                {
                    _buttonTouchInside.Remove(id);
                    b.PressOut();
                    AnimateButton(index, false);
                    Recompute();
                    MarkDirty(ButtonDirtyRect(index));
                    RequestRender();
                }
            }
        }

        private void ContactUp(int id)
        {
            _activeContacts.Remove(id);
            Log("ContactUp id=" + id + " stickTouch=" + _stickTouch);
            if (_editMode)
            {
                _dragging = false;
                _dragItem = null;
                _cfg.Save(_configPath);
                RequestRender();
                return;
            }

            if (_stickTouch == id)
            {
                _stickTouch = -2;
                // 锁定状态下松开手指，保持锁定（"一直前进"必须跨松手持续）
                if (!_cfg.Stick.Locked)
                {
                    _cfg.Stick.Release();
                }
                Recompute();
                MarkDirty(StickDirtyRect());
                RequestRender();
                return;
            }

            int index;
            if (_touchToButton.TryGetValue(id, out index))
            {
                if (index < 0 || index >= _cfg.Buttons.Count)
                {
                    _touchToButton.Remove(id);
                    _buttonTouchInside.Remove(id);
                    Recompute();
                    RequestRender();
                    return;
                }
                _touchToButton.Remove(id);
                // 只有这个触点确实还按着，才减计数（防止重复减）
                if (_buttonTouchInside.Contains(id))
                {
                    _buttonTouchInside.Remove(id);
                    if (index >= 0 && index < _cfg.Buttons.Count)
                    {
                        _cfg.Buttons[index].PressOut();
                        AnimateButton(index, false);
                    }
                }
                else if (index >= 0 && index < _cfg.Buttons.Count && _cfg.Buttons[index].Pressed)
                {
                    _cfg.Buttons[index].ForceRelease();
                    _buttonTouchInside.Remove(id);
                    AnimateButton(index, false);
                }
                Recompute();
                RequestRender();
            }
        }

        /// <summary>清空触摸态（鼠标松开时兜底）。</summary>
        private void ReleaseAllTouches()
        {
            _activeContacts.Clear();
            _dragging = false;
            _dragItem = null;
            _stickTouch = -2;
            _cfg.Stick.Release();
            _touchToButton.Clear();
            _buttonTouchInside.Clear();
            for (int i = 0; i < _cfg.Buttons.Count; i++)
            {
                _cfg.Buttons[i].ForceRelease();
                AnimateButton(i, false);
            }
            foreach (ButtonItem b in _cfg.Buttons) MarkDirty(b.Bounds);
            MarkDirty(StickDirtyRect());
            Recompute();
            RequestRender();
        }

        private OverlayItem HitItem(Point p)
        {
            for (int i = _cfg.Buttons.Count - 1; i >= 0; i--)
                if (_cfg.Buttons[i].HitTest(p)) return _cfg.Buttons[i];
            if (_cfg.Stick.HitTest(p)) return _cfg.Stick;
            return null;
        }

        // ====================================================================
        //  按键同步
        // ====================================================================

        private void Recompute()
        {
            _wantedKeys.Clear();

            if (!_editMode)
            {
                _cfg.Stick.CollectKeys(_wantedKeys);
                foreach (ButtonItem b in _cfg.Buttons)
                    if (b.Pressed) _wantedKeys.Add(b.Vk);
            }

            // 需要松开
            List<int> release = new List<int>();
            foreach (int k in _pressedKeys)
                if (!_wantedKeys.Contains(k)) release.Add(k);
            foreach (int k in release)
            {
                KeySender.Send(k, false);
                _pressedKeys.Remove(k);
            }

            // 需要按下
            foreach (int k in _wantedKeys)
            {
                if (_pressedKeys.Contains(k)) continue;
                KeySender.Send(k, true);
                _pressedKeys.Add(k);
            }

            if (DebugEnabled && (_wantedKeys.Count > 0 || _pressedKeys.Count > 0))
            {
                string s = "Recompute wanted=[";
                foreach (int k in _wantedKeys) s += KeyNames.ToName(k) + " ";
                s += "] pressed=[";
                foreach (int k in _pressedKeys) s += KeyNames.ToName(k) + " ";
                Log(s + "]");
            }
        }

        /// <summary>
        /// 兜底：万一 WM_LBUTTONUP 丢了（NOACTIVATE 窗口偶尔会这样），
        /// 用轮询发现鼠标已松开就强制收尾，否则摇杆会一直卡在按下状态。
        /// </summary>
        private void SafetyCheckMouseReleased()
        {
            if (_editMode && !_dragging) return;

            // 只有"确实有鼠标触点"时才需要检查鼠标状态。
            // 之前这里对触摸也会触发，一路走到 ContactUp(-1)，
            // 把触摸正按着的按钮全清了 —— 这就是"不能同时触发"的原因。
            bool mouseOnStick = (_stickTouch == -1);
            bool mouseOnButton = _touchToButton.ContainsKey(-1);
            if (!mouseOnStick && !mouseOnButton) return;

            bool down = (Native.GetAsyncKeyState(0x01) & 0x8000) != 0;
            if (down) return;

            Log("SafetyCheck: 鼠标已松开，收尾鼠标触点");
            ContactUp(-1);
        }

        private void ReleaseAllKeys()
        {
            foreach (int k in new List<int>(_pressedKeys))
                KeySender.Send(k, false);
            _pressedKeys.Clear();
            _wantedKeys.Clear();
        }

        // ====================================================================
        //  菜单
        // ====================================================================

        private void ShowContextMenu(Point location)
        {
            OverlayItem hit = HitItem(location);

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Font = new Font("Microsoft YaHei UI", 9f);

            if (_editMode)
            {
                if (hit is ButtonItem)
                {
                    ButtonItem b = (ButtonItem)hit;
                    menu.Items.Add("设置按键 / 文字…", null,
                        delegate { EditButtonKey(b); });
                    menu.Items.Add("设置按钮大小…", null, delegate { EditItemSize(b); });
                    menu.Items.Add("删除这个按键", null, delegate
                    {
                        int removedIndex = _cfg.Buttons.IndexOf(b);
                        _cfg.Buttons.Remove(b);
                        if (b.AnimationTimer != null) { b.AnimationTimer.Stop(); b.AnimationTimer.Dispose(); b.AnimationTimer = null; }
                        _cfg.Save(_configPath);
                        RequestRender();
                    });
                    menu.Items.Add(new ToolStripSeparator());
                }
                else if (hit is StickItem)
                {
                    StickItem s = (StickItem)hit;
                    menu.Items.Add("设置摇杆映射…", null, delegate { EditStickKeys(s); });
                    menu.Items.Add("设置摇杆大小…", null, delegate { EditItemSize(s); });
                    menu.Items.Add(new ToolStripSeparator());
                }
                else
                {
                    menu.Items.Add("在这里添加按键", null, delegate
                    {
                        AddButtonAt(location);
                    });
                    menu.Items.Add(new ToolStripSeparator());
                }

                menu.Items.Add("保存配置并重启", null, delegate { SaveAndRestart(); });
            }
            else
            {
                menu.Items.Add("进入编辑模式", null, delegate { SetEditMode(true); });
            }

            menu.Items.Add("设置透明度…", null, delegate { EditOpacity(); });

            menu.Items.Add("重新载入配置", null, delegate
            {
                ReleaseAllTouches();
                _cfg = Config.Load(_configPath);
                RequestRender();
                Flash("已重新载入");
            });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("保存配置并重启", null, delegate { SaveAndRestart(); });

            menu.Show(this, location);
        }

        private void SaveAndRestart()
        {
            try
            {
                _cfg.Save(_configPath);
                ReleaseAllTouches();
                ReleaseAllKeys();
                string exe = Application.ExecutablePath;
                Timer restart = new Timer();
                restart.Interval = 180;
                restart.Tick += delegate
                {
                    restart.Stop(); restart.Dispose();
                    try { System.Diagnostics.Process.Start(exe); }
                    catch (Exception startError) { MessageBox.Show(this, "重启失败：" + startError.Message, "TouchKeys"); }
                    Close();
                };
                restart.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "保存并重启失败：" + ex.Message, "TouchKeys", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>调整整体不透明度（全局）。</summary>
        private void EditOpacity()
        {
            using (Form dlg = new Form())
            {
                dlg.Text = "整体不透明度";
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.StartPosition = FormStartPosition.CenterScreen;
                dlg.MaximizeBox = false;
                dlg.MinimizeBox = false;
                dlg.ShowInTaskbar = false;
                dlg.ClientSize = new Size(340, 120);
                dlg.Font = new Font("Microsoft YaHei UI", 9f);

                TrackBar bar = new TrackBar();
                bar.Minimum = 30;
                bar.Maximum = 255;
                bar.Value = Math.Max(30, Math.Min(255, _cfg.Opacity));
                bar.TickFrequency = 25;
                bar.SetBounds(16, 12, 308, 45);
                dlg.Controls.Add(bar);

                Label val = new Label();
                val.Text = "当前：" + bar.Value + "（越小越透明）";
                val.SetBounds(16, 58, 300, 20);
                dlg.Controls.Add(val);

                bar.ValueChanged += delegate
                {
                    _cfg.Opacity = bar.Value;
                    val.Text = "当前：" + bar.Value + "（越小越透明）";
                    Render();   // 实时预览
                };

                Button ok = new Button();
                ok.Text = "确定";
                ok.SetBounds(160, 84, 76, 28);
                ok.DialogResult = DialogResult.OK;
                dlg.Controls.Add(ok);

                Button no = new Button();
                no.Text = "取消";
                no.SetBounds(244, 84, 76, 28);
                no.DialogResult = DialogResult.Cancel;
                dlg.Controls.Add(no);

                dlg.AcceptButton = ok;
                dlg.CancelButton = no;

                int original = _cfg.Opacity;

                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    _cfg.Opacity = bar.Value;
                    _cfg.Save(_configPath);
                }
                else
                {
                    _cfg.Opacity = original;
                }
                RequestRender();
            }
        }

        private void AddButtonAt(Point p)
        {
            ButtonItem b = new ButtonItem();
            b.W = 110;
            b.H = 110;
            b.X = p.X - 55;
            b.Y = p.Y - 55;
            b.Label = "新";
            b.Vk = 0x20;

            using (KeyDialog dlg = new KeyDialog("SPACE", "新"))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    b.Vk = dlg.ResultVk;
                    b.Label = dlg.ResultLabel;
                }
            }

            _cfg.Buttons.Add(b);
            _cfg.Save(_configPath);
            RequestRender();
        }

        private void EditButtonKey(ButtonItem b)
        {
            using (KeyDialog dlg = new KeyDialog(KeyNames.ToName(b.Vk), b.Label))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    b.Vk = dlg.ResultVk;
                    b.Label = dlg.ResultLabel;
                    _cfg.Save(_configPath);
                    RequestRender();
                }
            }
        }

        private void EditItemSize(OverlayItem item)
        {
            bool isStick = item is StickItem;
            int width = isStick ? ((StickItem)item).Radius * 2 : ((ButtonItem)item).W;
            int height = isStick ? width : ((ButtonItem)item).H;
            using (Form dlg = new Form())
            {
                dlg.Text = isStick ? "设置摇杆大小" : "设置按钮大小";
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.StartPosition = FormStartPosition.CenterScreen;
                dlg.ClientSize = new Size(330, 150);
                dlg.MaximizeBox = dlg.MinimizeBox = false;
                dlg.ShowInTaskbar = false;
                dlg.Font = new Font("Microsoft YaHei UI", 9f);
                NumericUpDown w = new NumericUpDown { Minimum = 40, Maximum = 800, Value = width, Increment = 5 };
                w.SetBounds(145, 20, 150, 25);
                dlg.Controls.Add(new Label { Text = isStick ? "外圈直径（像素）" : "宽度（像素）", Left = 18, Top = 24, Width = 125 });
                dlg.Controls.Add(w);
                NumericUpDown h = new NumericUpDown { Minimum = 40, Maximum = 800, Value = height, Increment = 5, Enabled = !isStick };
                h.SetBounds(145, 54, 150, 25);
                dlg.Controls.Add(new Label { Text = "高度（像素）", Left = 18, Top = 58, Width = 125 });
                dlg.Controls.Add(h);
                Button ok = new Button { Text = "确定", DialogResult = DialogResult.OK };
                ok.SetBounds(142, 104, 72, 28); dlg.Controls.Add(ok);
                Button cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel };
                cancel.SetBounds(222, 104, 72, 28); dlg.Controls.Add(cancel);
                dlg.AcceptButton = ok; dlg.CancelButton = cancel;
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    if (isStick) ((StickItem)item).Radius = Math.Max(50, (int)w.Value / 2);
                    else { ((ButtonItem)item).W = (int)w.Value; ((ButtonItem)item).H = (int)h.Value; }
                    _cfg.Save(_configPath);
                    InvalidateAll(); RequestRender();
                }
            }
        }

        private void EditStickKeys(StickItem s)
        {
            using (StickDialog dlg = new StickDialog(s))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    _cfg.Save(_configPath);
                    RequestRender();
                }
            }
        }

        private void Flash(string text)
        {
            _status = text;
            RequestRender();

            Timer t = new Timer();
            t.Interval = 1800;
            t.Tick += delegate
            {
                t.Stop();
                t.Dispose();
                _status = "";
                RequestRender();
            };
            t.Start();
        }

        private void CreateTrayIcon()
        {
            _tray = new NotifyIcon();
            _tray.Icon = SystemIcons.Application;
            _tray.Text = "TouchKeys 虚拟按键";
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("显示 / 隐藏虚拟按钮", null, delegate { ToggleOverlayVisibility(); });
            menu.Items.Add("切换编辑模式", null, delegate { EnsureVisible(); ToggleEditMode(); });
            menu.Items.Add("保存配置并重启", null, delegate { SaveAndRestart(); });
            menu.Items.Add("添加虚拟按键…", null, delegate
            {
                EnsureVisible();
                SetEditMode(true);
                AddButtonAt(new Point(_screenBounds.Width / 2, _screenBounds.Height / 2));
            });
            menu.Items.Add("设置显示快捷键…", null, delegate { EditToggleHotkey(); });
            ToolStripMenuItem startup = new ToolStripMenuItem("开机自启动");
            startup.CheckOnClick = true;
            startup.Checked = _cfg.AutoStart;
            startup.Click += delegate
            {
                _cfg.AutoStart = startup.Checked;
                if (!UpdateAutoStart(true))
                {
                    startup.Checked = !_cfg.AutoStart;
                    _cfg.AutoStart = startup.Checked;
                }
                _cfg.Save(_configPath);
            };
            menu.Items.Add(startup);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, delegate { Close(); });
            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += delegate { ToggleOverlayVisibility(); };
            _tray.Visible = true;
        }

        private void ToggleOverlayVisibility()
        {
            if (_hidden) EnsureVisible();
            else
            {
                ReleaseAllTouches();
                ReleaseAllKeys();
                _hidden = true;
                Hide();
            }
        }

        private void EnsureVisible()
        {
            if (!_hidden) return;
            _hidden = false;
            Show();
            RequestRender();
        }

        private bool UpdateAutoStart(bool report)
        {
            try
            {
                string folder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                string linkPath = Path.Combine(folder, "TouchKeys.lnk");
                if (_cfg.AutoStart)
                {
                    Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                    if (shellType == null) throw new InvalidOperationException("Windows 快捷方式服务不可用");
                    object shell = Activator.CreateInstance(shellType);
                    object link = shellType.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { linkPath });
                    Type linkType = link.GetType();
                    linkType.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, link, new object[] { Application.ExecutablePath });
                    linkType.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.SetProperty, null, link, new object[] { Application.StartupPath });
                    linkType.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, link, null);
                }
                else if (File.Exists(linkPath)) File.Delete(linkPath);
                return true;
            }
            catch (Exception ex)
            {
                if (report) MessageBox.Show("无法更新自启动设置：" + ex.Message, "TouchKeys", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
        }

        private void RegisterToggleHotkey()
        {
            if (_toggleHotkeyRegistered) Native.UnregisterHotKey(Handle, ToggleHotkeyId);
            _toggleHotkeyRegistered = Native.RegisterHotKey(Handle, ToggleHotkeyId, (uint)_cfg.ToggleModifiers, (uint)_cfg.ToggleKey);
            if (!_toggleHotkeyRegistered)
            {
                _cfg.ToggleModifiers = (int)(Native.MOD_CONTROL | Native.MOD_ALT);
                _cfg.ToggleKey = 0x72;
                _toggleHotkeyRegistered = Native.RegisterHotKey(Handle, ToggleHotkeyId, (uint)_cfg.ToggleModifiers, (uint)_cfg.ToggleKey);
                Flash("显示快捷键注册失败，快捷键可能已被占用");
            }
        }

        private void EditToggleHotkey()
        {
            using (Form dlg = new Form())
            {
                dlg.Text = "设置显示 / 隐藏快捷键";
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.StartPosition = FormStartPosition.CenterScreen;
                dlg.ClientSize = new Size(350, 145);
                dlg.MaximizeBox = dlg.MinimizeBox = false;
                dlg.ShowInTaskbar = false;
                dlg.Font = new Font("Microsoft YaHei UI", 9f);
                CheckBox ctrl = new CheckBox { Text = "Ctrl", Checked = (_cfg.ToggleModifiers & Native.MOD_CONTROL) != 0 };
                CheckBox alt = new CheckBox { Text = "Alt", Checked = (_cfg.ToggleModifiers & Native.MOD_ALT) != 0 };
                CheckBox shift = new CheckBox { Text = "Shift", Checked = (_cfg.ToggleModifiers & Native.MOD_SHIFT) != 0 };
                ctrl.SetBounds(18, 18, 72, 24); alt.SetBounds(100, 18, 72, 24); shift.SetBounds(182, 18, 72, 24);
                dlg.Controls.Add(ctrl); dlg.Controls.Add(alt); dlg.Controls.Add(shift);
                Label lab = new Label { Text = "主按键（如 F3、F8、A）" }; lab.SetBounds(18, 55, 170, 22); dlg.Controls.Add(lab);
                TextBox key = new TextBox { Text = KeyNames.ToName(_cfg.ToggleKey) }; key.SetBounds(195, 52, 135, 24); dlg.Controls.Add(key);
                Button ok = new Button { Text = "确定", DialogResult = DialogResult.OK }; ok.SetBounds(170, 98, 76, 28); dlg.Controls.Add(ok);
                Button cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel }; cancel.SetBounds(252, 98, 76, 28); dlg.Controls.Add(cancel);
                dlg.AcceptButton = ok; dlg.CancelButton = cancel;
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    int vk;
                    if (!KeyNames.TryParse(key.Text, out vk) || (!ctrl.Checked && !alt.Checked && !shift.Checked))
                    { MessageBox.Show(this, "请输入有效按键，并至少选择 Ctrl、Alt 或 Shift。", "提示"); return; }
                    int previousModifiers = _cfg.ToggleModifiers, previousKey = _cfg.ToggleKey;
                    _cfg.ToggleKey = vk;
                    _cfg.ToggleModifiers = (ctrl.Checked ? (int)Native.MOD_CONTROL : 0) | (alt.Checked ? (int)Native.MOD_ALT : 0) | (shift.Checked ? (int)Native.MOD_SHIFT : 0);
                    if (_toggleHotkeyRegistered) Native.UnregisterHotKey(Handle, ToggleHotkeyId);
                    _toggleHotkeyRegistered = Native.RegisterHotKey(Handle, ToggleHotkeyId, (uint)_cfg.ToggleModifiers, (uint)_cfg.ToggleKey);
                    if (!_toggleHotkeyRegistered)
                    {
                        _cfg.ToggleKey = previousKey; _cfg.ToggleModifiers = previousModifiers;
                        _toggleHotkeyRegistered = Native.RegisterHotKey(Handle, ToggleHotkeyId, (uint)_cfg.ToggleModifiers, (uint)_cfg.ToggleKey);
                        MessageBox.Show(this, "该快捷键已被其它程序占用，已保留原设置。", "快捷键不可用", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    _cfg.Save(_configPath);
                }
            }
        }

        private void CheckStaleContacts()
        {
            if (_activeContacts.Count == 0) return;
            DateTime cutoff = DateTime.UtcNow.AddSeconds(-3);
            List<int> stale = new List<int>();
            foreach (KeyValuePair<int, DateTime> pair in _activeContacts)
                if (pair.Value < cutoff) stale.Add(pair.Key);
            foreach (int id in stale) ContactUp(id);
        }

        /// <summary>
        /// 自测：直接调用触点处理函数，模拟两根手指（id=101/102），
        /// 不依赖真实触摸硬件。用于验证多指状态管理是否正确。
        /// </summary>
        public void SelfTestMultiTouch()
        {
            Console.WriteLine("=== 多指状态管理自测（直接调用触点函数）===");

            int pass = 0, fail = 0;

            Action<string, bool, string> check = (name, ok, detail) =>
            {
                Console.WriteLine((ok ? "  OK  " : "  FAIL") + "  " + name.PadRight(30) + detail);
                if (ok) pass++; else fail++;
            };

            int cx = _cfg.Stick.CX;
            int cy = _cfg.Stick.CY;

            ButtonItem b0 = _cfg.Buttons.Count > 0 ? _cfg.Buttons[0] : null;

            // ---------------- 场景 1：手指1 推摇杆上，手指2 按按钮 ----------------
            ContactDown(101, new Point(cx, cy));
            ContactDown(102, new Point(b0 != null ? b0.X + b0.W / 2 : cx + 400,
                                      b0 != null ? b0.Y + b0.H / 2 : cy));

            // 手指1 往上推
            for (int i = 1; i <= 6; i++)
                ContactMove(101, new Point(cx, cy - 70 * i / 6));

            Recompute();
            bool stickOn = _pressedKeys.Contains(_cfg.Stick.KeyUp);
            bool btnOn = b0 == null || _pressedKeys.Contains(b0.Vk);

            check("摇杆(W) 与 按钮 同时按下", stickOn && btnOn,
                "W=" + stickOn + " 按钮=" + btnOn);

            // ---------------- 场景 2：松开摇杆，按钮应保持 ----------------
            ContactUp(101);
            Recompute();
            bool stickOff = !_pressedKeys.Contains(_cfg.Stick.KeyUp);
            bool btnStillOn = b0 == null || _pressedKeys.Contains(b0.Vk);

            check("松开摇杆后按钮仍按住", stickOff && btnStillOn,
                "W已松=" + stickOff + " 按钮仍按=" + btnStillOn);

            // ---------------- 场景 3：松开按钮，全部释放 ----------------
            ContactUp(102);
            Recompute();
            bool allClear = _pressedKeys.Count == 0;

            check("全部松开后无残留按键", allClear,
                "剩余按键=" + _pressedKeys.Count);

            // ---------------- 场景 4：同一按钮两指同按 ----------------
            if (b0 != null)
            {
                Point bp = new Point(b0.X + b0.W / 2, b0.Y + b0.H / 2);
                ContactDown(201, bp);
                ContactDown(202, bp);
                Recompute();
                bool doublePressed = b0.Pressed;

                ContactUp(201);
                Recompute();
                bool stillPressed = b0.Pressed;

                ContactUp(202);
                Recompute();
                bool fullyReleased = !b0.Pressed;

                check("同一按钮两指同按互不干扰",
                    doublePressed && stillPressed && fullyReleased,
                    "双按=" + doublePressed + " 松一指仍按=" + stillPressed +
                    " 全松=" + fullyReleased);
            }

            // 清理
            foreach (ButtonItem b in _cfg.Buttons) b.ForceRelease();
            _stickTouch = -2;
            _cfg.Stick.Release();
            _touchToButton.Clear();
            _buttonTouchInside.Clear();
            Recompute();

            Console.WriteLine();
            Console.WriteLine("  通过 " + pass + " / " + (pass + fail));
            Console.WriteLine(fail == 0 ? "★ 多指状态管理正确" : "✗ 有 " + fail + " 项失败");
        }

        private void ToggleEditMode()
        {
            SetEditMode(!_editMode);
        }

        private void SetEditMode(bool on)
        {
            if (_editMode == on) { if (!on) { _dragging = false; _dragItem = null; } return; }

            ReleaseAllTouches();
            _editMode = on;
            if (!on && Native.GetCapture() == Handle) Native.ReleaseCapture();

            foreach (ButtonItem b in _cfg.Buttons) b.Selected = false;
            _cfg.Stick.Selected = false;

            if (!on) _cfg.Save(_configPath);

            RequestRender();
        }
    }

    // ========================================================================
    //  摇杆方向映射对话框
    // ========================================================================
    internal class StickDialog : Form
    {
        private readonly StickItem _stick;
        private readonly TextBox _up, _down, _left, _right, _dz;

        public StickDialog(StickItem stick)
        {
            _stick = stick;
            Text = "摇杆按键映射";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(340, 300);
            Font = new Font("Microsoft YaHei UI", 9f);

            _up = AddRow("推上 →", KeyNames.ToName(stick.KeyUp), 20);
            _down = AddRow("推下 →", KeyNames.ToName(stick.KeyDown), 52);
            _left = AddRow("推左 →", KeyNames.ToName(stick.KeyLeft), 84);
            _right = AddRow("推右 →", KeyNames.ToName(stick.KeyRight), 116);

            Label l = new Label();
            l.Text = "死区 (0.05 - 0.9)";
            l.SetBounds(16, 152, 130, 20);
            Controls.Add(l);

            _dz = new TextBox();
            _dz.Text = stick.Deadzone.ToString("0.00", CultureInfo.InvariantCulture);
            _dz.SetBounds(160, 150, 160, 24);
            Controls.Add(_dz);

            Label tip = new Label();
            tip.Text = "斜向会自动同时按下两个键（如 W+A）";
            tip.ForeColor = Color.DimGray;
            tip.SetBounds(16, 184, 300, 20);
            Controls.Add(tip);

            Button ok = new Button();
            ok.Text = "确定";
            ok.SetBounds(166, 254, 74, 28);
            ok.Click += delegate { Apply(); };
            Controls.Add(ok);

            Button cancel = new Button();
            cancel.Text = "取消";
            cancel.SetBounds(246, 254, 74, 28);
            cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;
        }

        private TextBox AddRow(string label, string value, int top)
        {
            Label l = new Label();
            l.Text = label;
            l.SetBounds(16, top + 3, 80, 20);
            Controls.Add(l);

            TextBox t = new TextBox();
            t.Text = value;
            t.SetBounds(100, top, 220, 24);
            Controls.Add(t);
            return t;
        }

        private bool TryKey(TextBox box, out int vk)
        {
            if (KeyNames.TryParse(box.Text, out vk)) return true;
            MessageBox.Show(this, "无法识别的按键：" + box.Text, "提示",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            box.Focus();
            box.SelectAll();
            return false;
        }

        private void Apply()
        {
            int up, down, left, right;
            if (!TryKey(_up, out up)) return;
            if (!TryKey(_down, out down)) return;
            if (!TryKey(_left, out left)) return;
            if (!TryKey(_right, out right)) return;

            double dz;
            if (!double.TryParse(_dz.Text, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out dz)) dz = _stick.Deadzone;
            if (dz < 0.05) dz = 0.05;
            if (dz > 0.9) dz = 0.9;

            _stick.KeyUp = up;
            _stick.KeyDown = down;
            _stick.KeyLeft = left;
            _stick.KeyRight = right;
            _stick.Deadzone = dz;

            DialogResult = DialogResult.OK;
        }
    }

    // ========================================================================
    //  入口
    // ========================================================================
    internal static class Program
    {
        [DllImport("kernel32.dll")]
        private static extern bool AllocConsole();

        [DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int pid);

        private static void NativeAllocConsole()
        {
            try { AttachConsole(-1); } catch { }
            try { AllocConsole(); } catch { }
        }
        [STAThread]
        private static void Main(string[] args)
        {
            if (args != null)
            {
                foreach (string a in args)
                {
                    if (a == "--selftest" || a == "/selftest")
                    {
                        OverlayForm.SelfTestRequested = true;
                        NativeAllocConsole();
                    }
                    else if (a == "--debug" || a == "/debug")
                    {
                        OverlayForm.DebugEnabled = true;
                    }
                    else if (a == "--multitouch" || a == "/multitouch")
                    {
                        OverlayForm.MultiTouchSelfTest = true;
                        NativeAllocConsole();
                    }
                }
            }

            // 关键：这里【不能】设置 DPI 感知！
            //
            // 实测（125% 缩放的机器）：
            //   未设 DPI 感知      -> GetSystemMetrics = 1536x960, Screen.Bounds = 1536x960  一致
            //   设 SystemDPI Aware -> GetSystemMetrics = 1920x1200, Screen.Bounds = 1536x960  不一致
            //
            // UpdateLayeredWindow 的 dst/size 走的是 Window 的逻辑坐标，
            // 而窗口本身受系统缩放约束。DPI 感知一开，画布 1920x1200 塞进
            // 1536x960 的窗口，右下部分被截掉 —— 那里的控件 alpha=0，
            // 点击就被 Windows 判定为穿透，摇杆完全失灵。
            //
            // 保持 DPI unaware 反而让两套坐标统一，扫描码按键也不受影响。
            // 全局兜底：任何未处理异常都不要弹出 .NET 崩溃对话框。
            // 输入层程序崩一次就得重开，比单条消息处理失败糟糕得多。
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object s2, System.Threading.ThreadExceptionEventArgs e)
            {
                OverlayForm.Log("【UI 异常】" + e.Exception.GetType().Name + ": " +
                    e.Exception.Message);
            };
            AppDomain.CurrentDomain.UnhandledException += delegate(object s2,
                UnhandledExceptionEventArgs e)
            {
                Exception ex = e.ExceptionObject as Exception;
                if (ex != null)
                    OverlayForm.Log("【域异常】" + ex.GetType().Name + ": " + ex.Message);
            };

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            string path = Config.DefaultPath;

            Application.Run(new OverlayForm(path));
        }
    }
}
