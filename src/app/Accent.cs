using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows;
using System.Windows.Media;
using DColor = System.Drawing.Color;
using MColor = System.Windows.Media.Color;

namespace ComicConverter
{
    /// <summary>
    /// 品牌色唯一来源是程序图标（app.ico）。
    /// 按钮、链接、勾选标记、选中态都从这里取色；换图标整套界面自动换色，
    /// 代码里不写死任何品牌色字面值（中性灰阶除外）。
    /// 规则见《程序本体设计规范.md》§三「配色」。
    /// </summary>
    public static class Accent
    {
        /// <summary>兜底种子：只在图标取不出主色时使用。</summary>
        static readonly DColor FallbackSeed = DColor.FromArgb(0x1B, 0x8F, 0x72);

        /// <summary>强调色：主按钮底色、链接、勾选标记。</summary>
        public static System.Windows.Media.Color Brand { get; private set; }
        /// <summary>强调色按下态（压暗）。</summary>
        public static System.Windows.Media.Color BrandDark { get; private set; }
        /// <summary>强调色浅底（选中胶囊、轻提示）。</summary>
        public static System.Windows.Media.Color Soft { get; private set; }
        /// <summary>强调色微光悬停底。</summary>
        public static System.Windows.Media.Color Hover { get; private set; }

        public static string BrandHex { get { return Hex(Brand); } }
        public static string BrandDarkHex { get { return Hex(BrandDark); } }
        public static string SoftHex { get { return Hex(Soft); } }

        static SolidColorBrush _brandBrush;
        /// <summary>共享画刷，避免每次创建。</summary>
        public static SolidColorBrush BrandBrush
        {
            get
            {
                if (_brandBrush == null)
                {
                    _brandBrush = new SolidColorBrush(Brand);
                    _brandBrush.Freeze();
                }
                return _brandBrush;
            }
        }

        static Accent()
        {
            Apply(FallbackSeed);   // 静态初值与兜底路径走同一套派生
        }

        /// <summary>启动时调用一次：读图标 → 取主色 → 派生整套颜色。</summary>
        public static void Init()
        {
            DColor seed;
            if (!TrySeed(LoadIcon(), out seed)) seed = FallbackSeed;
            Apply(seed);

            _brandBrush = null;
            if (Application.Current != null)
            {
                Application.Current.Resources["AccentBrush"] = BrandBrush;
                Application.Current.Resources["AccentSoftBrush"] = Frozen(Soft);
                Application.Current.Resources["AccentHoverBrush"] = Frozen(Hover);
                Application.Current.Resources["AccentDarkBrush"] = Frozen(BrandDark);
            }
        }

        static SolidColorBrush Frozen(System.Windows.Media.Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        static void Apply(DColor seed)
        {
            double h, s;
            ToHsl(seed, out h, out s, out _);
            s = Math.Max(s, 0.35);                        // 灰调图标也要有颜色

            Brand = FromHsl(h, Math.Min(s * 1.05, 0.95), 0.245);
            BrandDark = FromHsl(h, Math.Min(s * 1.10, 0.95), 0.175);
            Soft = FromHsl(h, 0.32, 0.93);
            Hover = FromHsl(h, 0.28, 0.96);
        }

        static Bitmap LoadIcon()
        {
            try
            {
                var res = Application.GetResourceStream(new Uri("pack://application:,,,/app.ico"));
                if (res == null) return null;
                using (var ico = new Icon(res.Stream, new System.Drawing.Size(64, 64)))
                    return ico.ToBitmap();
            }
            catch { return null; }
        }

        // ---------------------------------------------------------- 取主色

        class Bucket
        {
            public int N;
            public double SumH, SumS;
        }

        static bool TrySeed(Bitmap src, out DColor seed)
        {
            seed = DColor.Empty;
            if (src == null) return false;

            using (var bmp = new Bitmap(64, 64, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.Clear(DColor.Transparent);
                    g.DrawImage(src, new Rectangle(0, 0, 64, 64));
                }

                var hist = new Dictionary<int, Bucket>();
                for (int y = 0; y < 64; y++)
                {
                    for (int x = 0; x < 64; x++)
                    {
                        var p = bmp.GetPixel(x, y);
                        if (p.A < 200) continue;                      // 半透明毛边
                        double h, s, l;
                        ToHsl(p, out h, out s, out l);
                        if (l > 0.92 || l < 0.12) continue;           // 近白、近黑
                        if (s < 0.18) continue;                       // 灰阶

                        int hb = (int)(h / 10.0);
                        int sb = (int)(s / 0.1);
                        int lb = (int)(l / 0.1);
                        int key = (hb * 100 + sb) * 100 + lb;

                        Bucket b;
                        if (!hist.TryGetValue(key, out b)) hist[key] = b = new Bucket();
                        b.N++; b.SumH += h; b.SumS += s;
                    }
                }

                if (hist.Count == 0) return false;

                int bestKey = 0, bestN = -1;
                foreach (var kv in hist)
                    if (kv.Value.N > bestN) { bestN = kv.Value.N; bestKey = kv.Key; }

                // 相邻桶加权平均（同色相的明暗过渡不要拉低饱和度）
                double sumH = 0, sumS = 0; int total = 0;
                int bh = bestKey / 10000;
                foreach (var kv in hist)
                {
                    int hb = kv.Key / 10000;
                    int d = Math.Abs(hb - bh);
                    if (d > 1 && d < 35) continue;               // 跨 350° 也算相邻
                    sumH += kv.Value.SumH; sumS += kv.Value.SumS; total += kv.Value.N;
                }
                if (total == 0) return false;

                double H = sumH / total / 360.0;
                double S = Math.Min(sumS / total, 0.95);
                var rgb = HslToRgb(H, S, 0.45);
                seed = DColor.FromArgb(rgb.R, rgb.G, rgb.B);
                return true;
            }
        }

        // ---------------------------------------------------------- HSL

        static void ToHsl(DColor c, out double h, out double s, out double l)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            l = (max + min) / 2.0;
            if (Math.Abs(max - min) < 1e-6) { h = 0; s = 0; return; }

            double d = max - min;
            s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);
            if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
            else if (max == g) h = (b - r) / d + 2;
            else h = (r - g) / d + 4;
            h *= 60;
        }

        static System.Windows.Media.Color FromHsl(double hDeg, double s, double l)
        {
            var c = HslToRgb(hDeg / 360.0, s, l);
            return System.Windows.Media.Color.FromRgb(c.R, c.G, c.B);
        }

        static DColor HslToRgb(double h, double s, double l)
        {
            if (s <= 0) { byte v = (byte)Math.Round(l * 255); return DColor.FromArgb(v, v, v); }
            double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            double p = 2 * l - q;
            return DColor.FromArgb(
                (byte)Math.Round(Hue(p, q, h + 1.0 / 3) * 255),
                (byte)Math.Round(Hue(p, q, h) * 255),
                (byte)Math.Round(Hue(p, q, h - 1.0 / 3) * 255));
        }

        static double Hue(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6) return p + (q - p) * 6 * t;
            if (t < 0.5) return q;
            if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
            return p;
        }

        public static string Hex(System.Windows.Media.Color c)
        {
            return "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
        }
    }
}