// 把 alpha-check.png 叠到深色游戏截图上，看实际观感
using System;
using System.Drawing;
using System.Drawing.Imaging;

internal static class Preview
{
    public static void Main()
    {
        string src = @"C:\Temp\tkc\alpha-check.png";
        using (Bitmap o = new Bitmap(src))
        {
            int w = o.Width, h = o.Height;

            // 造一个深色"游戏画面"背景
            using (Bitmap bg = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bg))
                {
                    using (System.Drawing.Drawing2D.LinearGradientBrush lg =
                        new System.Drawing.Drawing2D.LinearGradientBrush(
                            new Rectangle(0, 0, w, h),
                            Color.FromArgb(58, 74, 96),
                            Color.FromArgb(22, 28, 38),
                            System.Drawing.Drawing2D.LinearGradientMode.Vertical))
                    {
                        g.FillRectangle(lg, 0, 0, w, h);
                    }
                    // 加些纹理方块，模拟场景
                    Random r = new Random(7);
                    for (int i = 0; i < 120; i++)
                    {
                        int bw = r.Next(30, 160), bh = r.Next(20, 120);
                        int bx = r.Next(0, Math.Max(1, w - bw)), by = r.Next(0, Math.Max(1, h - bh));
                        int a = r.Next(10, 40);
                        using (SolidBrush b = new SolidBrush(Color.FromArgb(a, 120, 150, 180)))
                            g.FillRectangle(b, bx, by, bw, bh);
                    }
                }

                // 叠加控件层
                using (Graphics g = Graphics.FromImage(bg))
                    g.DrawImage(o, 0, 0, w, h);

                bg.Save(@"C:\Temp\tkc\preview-dark.png", ImageFormat.Png);
            }
            Console.WriteLine("saved preview-dark.png");
        }
    }
}
