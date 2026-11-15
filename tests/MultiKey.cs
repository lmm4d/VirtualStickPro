// 多键同时触发测试 —— 坐标全部从 touchkeys.ini 读，且自动跳过被遮挡的位置
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

internal static class MultiKey2
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

    static uint _tkPid;

    static void Raw(int x, int y, uint extra)
    {
        int sw = GetSystemMetrics(0), sh = GetSystemMetrics(1);
        INPUT i = new INPUT();
        i.type = 0;
        i.mi.dx = (int)((long)x * 65535 / Math.Max(1, sw - 1));
        i.mi.dy = (int)((long)y * 65535 / Math.Max(1, sh - 1));
        i.mi.dwFlags = ABSOLUTE | MOVE | extra;
        SendInput(1, new INPUT[] { i }, Marshal.SizeOf(typeof(INPUT)));
    }

    static void Move(int x, int y) { Raw(x, y, 0); }
    static void Down(int x, int y) { Raw(x, y, LEFTDOWN); }
    static void Up(int x, int y) { Raw(x, y, LEFTUP); }

    static bool K(int vk) { return (GetAsyncKeyState(vk) & 0x8000) != 0; }

    static bool Owned(int x, int y)
    {
        POINT p = new POINT(); p.X = x; p.Y = y;
        uint pid; GetWindowThreadProcessId(WindowFromPoint(p), out pid);
        return pid == _tkPid;
    }

    static int _pass, _fail;
    static void Check(string n, bool ok, string d)
    {
        Console.WriteLine((ok ? "  OK  " : "  FAIL") + "  " + n.PadRight(30) + d);
        if (ok) _pass++; else _fail++;
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

    static Dictionary<string, Dictionary<string, string>> ReadIni(string path)
    {
        var r = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> cur = null;
        foreach (string raw in File.ReadAllLines(path))
        {
            string l = raw.Trim();
            if (l.Length == 0 || l[0] == ';' || l[0] == '#') continue;
            if (l[0] == '[' && l[l.Length - 1] == ']')
            {
                cur = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                r[l.Substring(1, l.Length - 2).Trim()] = cur;
                continue;
            }
            if (cur == null) continue;
            int eq = l.IndexOf('=');
            if (eq > 0) cur[l.Substring(0, eq).Trim()] = l.Substring(eq + 1).Trim();
        }
        return r;
    }

    static int I(Dictionary<string, string> d, string k, int def)
    {
        string v; if (!d.TryGetValue(k, out v)) return def;
        int n; return int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) ? n : def;
    }

    public static void Main()
    {
        var procs = System.Diagnostics.Process.GetProcessesByName("TouchKeys");
        if (procs.Length == 0) { Console.WriteLine("TouchKeys 未运行"); return; }
        _tkPid = (uint)procs[0].Id;

        string ini = @"C:\Temp\tkc\touchkeys.ini";
        if (!File.Exists(ini)) { Console.WriteLine("找不到 " + ini); return; }

        var cfg = ReadIni(ini);
        var st = cfg["stick"];
        int cx = I(st, "cx", 200), cy = I(st, "cy", 700), radius = I(st, "radius", 105);
        int push = Math.Max(50, radius - 25);

        Console.WriteLine("=== 多键同时触发 ===");
        Console.WriteLine("摇杆 (" + cx + "," + cy + ") 半径 " + radius);
        Console.WriteLine();

        // ---------- 1) 斜向双键 ----------
        Down(cx, cy);
        Thread.Sleep(140);
        for (int i = 1; i <= 10; i++)
        {
            Move(cx - push * i / 10, cy - push * i / 10);
            Thread.Sleep(40);
        }
        Thread.Sleep(120);
        Check("斜向 左上 = W+A", K(0x57) && K(0x41),
            "W=" + K(0x57) + " A=" + K(0x41));

        Up(cx - push, cy - push);
        Thread.Sleep(200);
        Move(700, 300);
        Thread.Sleep(450);

        // ---------- 2) 按钮（自动找没被遮挡的）----------
        var names = new List<string>(cfg.Keys);
        names.Sort(StringComparer.OrdinalIgnoreCase);

        ButtonItem2 found = null;
        foreach (string sec in names)
        {
            if (!sec.StartsWith("button.", StringComparison.OrdinalIgnoreCase)) continue;
            var b = cfg[sec];
            int bx = I(b, "x", 0) + I(b, "w", 100) / 2;
            int by = I(b, "y", 0) + I(b, "h", 100) / 2;
            if (!Owned(bx, by)) continue;
            found = new ButtonItem2
            {
                Label = b.ContainsKey("label") ? b["label"] : sec,
                Vk = KeyVk(b.ContainsKey("key") ? b["key"] : "SPACE"),
                X = bx,
                Y = by
            };
            break;
        }

        if (found != null)
        {
            Move(found.X, found.Y);
            Thread.Sleep(180);
            Down(found.X, found.Y);
            Thread.Sleep(300);
            bool d = K(found.Vk);
            Up(found.X, found.Y);
            Thread.Sleep(250);
            bool u = !K(found.Vk);
            Check("按钮 " + found.Label + " 独立", d && u, "按下=" + d + " 释放=" + u);
        }
        else
        {
            Console.WriteLine("  SKIP  所有按钮都被系统浮层遮挡，跳过（挪一下位置即可）");
        }

        Move(700, 300);
        Thread.Sleep(400);

        // ---------- 3) 方向切换 ----------
        Down(cx, cy);
        Thread.Sleep(140);
        for (int i = 1; i <= 10; i++)
        {
            Move(cx - push * i / 10, cy - push * i / 10);
            Thread.Sleep(40);
        }
        Thread.Sleep(100);
        bool wa = K(0x57) && K(0x41);

        for (int i = 1; i <= 12; i++)
        {
            Move(cx - push + (2 * push) * i / 12, cy - push + (2 * push) * i / 12);
            Thread.Sleep(40);
        }
        Thread.Sleep(150);
        bool sd = K(0x53) && K(0x44);
        bool oldCleared = !K(0x57) && !K(0x41);
        Check("斜向切换 左->右下", wa && sd && oldCleared,
            "WA=" + wa + " SD=" + sd + " 旧键已松=" + oldCleared);

        Up(cx + push, cy + push);
        Thread.Sleep(250);

        // ---------- 4) 全部释放 ----------
        Move(700, 300);
        Thread.Sleep(400);
        bool clean = !K(0x57) && !K(0x41) && !K(0x53) && !K(0x44);
        Check("结束后无残留按键", clean, "干净=" + clean);

        Console.WriteLine();
        Console.WriteLine("=== 结果 ===");
        Console.WriteLine("  通过 " + _pass + " / " + (_pass + _fail));
        Console.WriteLine(_fail == 0 ? "★ 全部通过" : "✗ 有 " + _fail + " 项失败");
    }

    class ButtonItem2
    {
        public string Label;
        public int Vk;
        public int X, Y;
    }
}
