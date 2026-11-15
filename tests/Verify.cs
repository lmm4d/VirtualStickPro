// 完整验收：坐标从 touchkeys.ini 读取，不写死
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

internal static class Verify
{
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int i);
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);

    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Sequential)]
    struct INPUT { public uint type; public MOUSEINPUT mi; }
    [DllImport("user32.dll")] static extern uint SendInput(uint n, INPUT[] p, int cb);

    const uint LEFTDOWN = 0x0002, LEFTUP = 0x0004;
    const uint ABSOLUTE = 0x8000, MOVE = 0x0001;

    static int _pass, _fail;
    static uint _tkPid;

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

    static void Check(string name, bool ok, string detail)
    {
        Console.WriteLine((ok ? "  OK  " : "  FAIL") + "  " + name.PadRight(20) + detail);
        if (ok) _pass++; else _fail++;
    }

    /// <summary>确认这个坐标点确实归 TouchKeys，避免被系统浮层挡住造成假失败。</summary>
    static bool OwnedByUs(int x, int y)
    {
        POINT p = new POINT(); p.X = x; p.Y = y;
        IntPtr h = WindowFromPoint(p);
        uint pid;
        GetWindowThreadProcessId(h, out pid);
        return pid == _tkPid;
    }

    // ---------- ini ----------
    static Dictionary<string, Dictionary<string, string>> ReadIni(string path)
    {
        var r = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> cur = null;
        foreach (string raw in File.ReadAllLines(path))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == ';' || line[0] == '#') continue;
            if (line[0] == '[' && line[line.Length - 1] == ']')
            {
                cur = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                r[line.Substring(1, line.Length - 2).Trim()] = cur;
                continue;
            }
            if (cur == null) continue;
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            cur[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
        }
        return r;
    }

    static int I(Dictionary<string, string> d, string k, int def)
    {
        string v; if (!d.TryGetValue(k, out v)) return def;
        int n; return int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) ? n : def;
    }

    static int KeyVk(string name)
    {
        name = name.Trim().ToUpperInvariant();
        if (name == "SPACE") return 0x20;
        if (name == "ENTER") return 0x0D;
        if (name == "ESC") return 0x1B;
        if (name.Length == 1)
        {
            char c = name[0];
            if (c >= 'A' && c <= 'Z') return c;
            if (c >= '0' && c <= '9') return c;
        }
        int n;
        if (int.TryParse(name, out n)) return n;
        if (name.StartsWith("0X") && int.TryParse(name.Substring(2),
                NumberStyles.HexNumber, CultureInfo.InvariantCulture, out n)) return n;
        return 0;
    }

    public static void Main()
    {
        var procs = System.Diagnostics.Process.GetProcessesByName("TouchKeys");
        if (procs.Length == 0) { Console.WriteLine("TouchKeys 没在运行"); return; }
        _tkPid = (uint)procs[0].Id;

        string ini = Path.Combine(
            Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location),
            "touchkeys.ini");
        if (!File.Exists(ini)) ini = @"C:\Temp\tkc\touchkeys.ini";
        if (!File.Exists(ini)) { Console.WriteLine("找不到 touchkeys.ini: " + ini); return; }

        var cfg = ReadIni(ini);
        var st = cfg.ContainsKey("stick") ? cfg["stick"] : new Dictionary<string, string>();

        int cx = I(st, "cx", 200);
        int cy = I(st, "cy", 700);
        int radius = I(st, "radius", 130);
        int lockR = I(st, "lockRadius", 34);
        int lockGap = I(st, "lockGap", 26);

        Console.WriteLine("=== 配置 ===");
        Console.WriteLine("  摇杆 (" + cx + "," + cy + ") r=" + radius);
        Console.WriteLine("  锁定 (" + cx + "," + (cy - radius - lockGap - lockR) + ")");
        Console.WriteLine();

        // ---------------- 摇杆四方向 ----------------
        Console.WriteLine("=== 摇杆四方向 ===");
        // 距离要小于"半径 + 间隙"，否则推到上方会撞到锁定按钮导致锁定
        // （推到锁定区就锁定是正确的设计，测试要避开）
        int push = Math.Max(60, radius - 20);
        object[][] dirs = new object[][] { new object[]{0,-push,0x57,"W(上)"}, new object[]{0,push,0x53,"S(下)"},
                                     new object[]{-push,0,0x41,"A(左)"}, new object[]{push,0,0x44,"D(右)"} };
        foreach (object[] d in dirs)
        {
            Mouse(true, cx, cy); Thread.Sleep(120);
            bool saw = false;
            for (int i = 1; i <= 12; i++)
            {
                Mouse(true, cx + (int)d[0] * i / 12, cy + (int)d[1] * i / 12);
                Thread.Sleep(45);
                if (K((int)d[2])) saw = true;
            }
            Thread.Sleep(80);
            Mouse(false, cx + (int)d[0], cy + (int)d[1]);
            Thread.Sleep(200);
            Check("推" + d[3], saw && !K((int)d[2]),
                "发出=" + saw + " 复位=" + (!K((int)d[2])));
        }

        // ---------------- 按钮 ----------------
        Console.WriteLine();
        Console.WriteLine("=== 自定义按钮 ===");
        var names = new List<string>(cfg.Keys);
        names.Sort(StringComparer.OrdinalIgnoreCase);
        foreach (string sec in names)
        {
            if (!sec.StartsWith("button.", StringComparison.OrdinalIgnoreCase)) continue;
            var b = cfg[sec];
            int x = I(b, "x", 0); int y = I(b, "y", 0);
            int w = I(b, "w", 110), h = I(b, "h", 110);
            string label = b.ContainsKey("label") ? b["label"] : sec;
            string keyName = b.ContainsKey("key") ? b["key"] : "SPACE";
            int vk = KeyVk(keyName);

            int bx = x + w / 2, by = y + h / 2;

            if (!OwnedByUs(bx, by))
            {
                Console.WriteLine("  SKIP  " + label + "(" + keyName + ")  无法测试：坐标 (" +
                    bx + "," + by + ") 被其它窗口遮挡（系统浮层），换个位置即可");
                continue;
            }

            Mouse(true, bx, by); Thread.Sleep(300);
            bool down = K(vk);
            Mouse(false, bx, by); Thread.Sleep(250);
            bool up = !K(vk);
            Check("按钮 " + label + "(" + keyName + ")", down && up,
                "按下=" + down + " 复位=" + up);
        }

        // ---------------- 锁定 ----------------
        Console.WriteLine();
        Console.WriteLine("=== 锁定按钮 ===");
        int lockX = cx, lockY = cy - radius - lockGap - lockR;

        Mouse(true, lockX, lockY); Thread.Sleep(250);
        bool clickEffect = K(0x57);
        Mouse(false, lockX, lockY); Thread.Sleep(200);
        Check("直接点击无效", !clickEffect, "W=" + clickEffect + "（应为 False）");

        Mouse(true, cx, cy); Thread.Sleep(140);
        bool sawLock = false;
        for (int i = 1; i <= 14; i++)
        {
            Mouse(true, cx, cy - (cy - lockY) * i / 14);
            Thread.Sleep(55);
            if (K(0x57)) sawLock = true;
        }
        Thread.Sleep(150);
        Mouse(false, lockX, lockY); Thread.Sleep(400);
        bool held = K(0x57);
        Check("拖入锁定区", sawLock, "拖动中出现 W=" + sawLock);
        Check("松手后保持", held, "W=" + held + "（一直前进）");

        Mouse(true, cx, cy); Thread.Sleep(250);
        bool unlocked = !K(0x57);
        Mouse(false, cx, cy); Thread.Sleep(250);
        Check("触摸摇杆解锁", unlocked, "W 已停=" + unlocked);

        Console.WriteLine();
        Console.WriteLine("=== 结果 ===");
        Console.WriteLine("  通过 " + _pass + " / " + (_pass + _fail));
        Console.WriteLine(_fail == 0 ? "★ 全部通过" : "✗ 有 " + _fail + " 项失败");
    }
}
