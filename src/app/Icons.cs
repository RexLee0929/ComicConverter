using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ComicConverter
{
    /// <summary>
    /// 24 栅格线性图标。路径数据来自 icons/Unified（《图标规范》），不依赖图片资源。
    /// 表里只有几何：一条路径 = 路径数据 + 是否实心；没有颜色、没有尺寸、没有用途。
    /// 用法：Icons.Get("folder", 画刷) / Icons.Get("error", DangerBrush, 24)。
    ///
    /// 两条纪律（规范 §六.1 / §三.2）：
    ///   · 缩放的是 24 栅格整体（Canvas 24 + Viewbox），不是墨迹外框 —— 否则「一个加号」
    ///     会比「一个窗口」还大。
    ///   · 笔宽按渲染尺寸现算，不套素材标称的 1.4：等效物理笔宽低于 1.05px 线会塌成灰。
    /// </summary>
    public static class Icons
    {
        /// <summary>一条路径：d = 24 栅格里的几何，Fill = 实心块（圆点 / 开关把手 / 单选内芯）。</summary>
        public readonly struct P
        {
            public readonly string D;
            public readonly bool Fill;
            public P(string d, bool fill) { D = d; Fill = fill; }
        }

        /// <summary>只放程序真正用到的键；没用到的键不许留在表里当"备用"（规范 §八.2）。</summary>
        static readonly Dictionary<string, P[]> Paths = new Dictionary<string, P[]>
        {
            { "plus", new[] { new P("M12 4.5v15M4.5 12h15", false) } },
            { "folder", new[] { new P("M3 9.4A2.4 2.4 0 0 1 5.4 7L7.59 7A1.41 1.41 0 0 1 10 8L10 8A1.41 1.41 0 0 0 12.41 9L18.6 9A2.4 2.4 0 0 1 21 11.4L21 17.6A2.4 2.4 0 0 1 18.6 20L5.4 20A2.4 2.4 0 0 1 3 17.6Z", false) } },
            { "open", new[] { new P("M3 9.4A2.4 2.4 0 0 1 5.4 7L7.59 7A1.41 1.41 0 0 1 10 8L10 8A1.41 1.41 0 0 0 12.41 9L18.6 9A2.4 2.4 0 0 1 21 11.4L21 17.6A2.4 2.4 0 0 1 18.6 20L5.4 20A2.4 2.4 0 0 1 3 17.6Z", false), new P("M9 14.5h7", false), new P("M13.5 11.8l3 2.7-3 2.7", false) } },
            { "window", new[] { new P("M5.4 4.5L18.6 4.5C19.9255 4.5 21 5.57452 21 6.9L21 17.1C21 18.4255 19.9255 19.5 18.6 19.5L5.4 19.5C4.07452 19.5 3 18.4255 3 17.1L3 6.9C3 5.57452 4.07452 4.5 5.4 4.5Z", false), new P("M3 9h18", false) } },
            { "close", new[] { new P("M5.5 5.5l13 13M18.5 5.5l-13 13", false) } },
            { "clean", new[] { new P("M8 20A1.5 1.5 0 0 1 5.44 18.94L4.86 18.36A1.92 1.92 0 0 1 4.86 15.64L12.14 8.36A1.92 1.92 0 0 1 14.86 8.36L18.14 11.64A1.92 1.92 0 0 1 17.93 14.1L10.73 19.14A1.5 1.5 0 0 1 8 20Z", false), new P("M8.6 12.9l5.4 5.4", false), new P("M3 22h18", false) } },
            { "list", new[] { new P("M8.5 6h12.5M8.5 12h12.5M8.5 18h12.5", false), new P("M4 6h.01", false), new P("M4 12h.01", false), new P("M4 18h.01", false) } },
            { "refresh", new[] { new P("M20 12a8 8 0 1 1-2.4-5.7", false), new P("M20 4.5V10h-5.5", false) } },
            { "settings", new[] { new P("M3.5 7.5h10.5M18 7.5h2.5", false), new P("M3.5 16.5h3M10.5 16.5h10", false), new P("M16 5v5", false), new P("M8 14v5", false) } },
            { "mail", new[] { new P("M5.4 5.5L18.6 5.5C19.9255 5.5 21 6.57452 21 7.9L21 16.1C21 17.4255 19.9255 18.5 18.6 18.5L5.4 18.5C4.07452 18.5 3 17.4255 3 16.1L3 7.9C3 6.57452 4.07452 5.5 5.4 5.5Z", false), new P("M3.4 7.2l7.4 5.3a2 2 0 0 0 2.4 0l7.4-5.3", false) } },
            { "about", new[] { new P("M12 3C16.9706 3 21 7.02944 21 12C21 16.9706 16.9706 21 12 21C7.02944 21 3 16.9706 3 12C3 7.02944 7.02944 3 12 3Z", false), new P("M12 3v3.2M12 17.8V21M3 12h3.2M17.8 12H21", false), new P("M12 11v5.5", false), new P("M12 8h.01", false) } },
            { "info", new[] { new P("M12 3C16.9706 3 21 7.02944 21 12C21 16.9706 16.9706 21 12 21C7.02944 21 3 16.9706 3 12C3 7.02944 7.02944 3 12 3Z", false), new P("M12 11.2v5.8", false), new P("M12 7.8h.01", false) } },
            { "error", new[] { new P("M12 3C16.9706 3 21 7.02944 21 12C21 16.9706 16.9706 21 12 21C7.02944 21 3 16.9706 3 12C3 7.02944 7.02944 3 12 3Z", false), new P("M12 7.4v6", false), new P("M12 17h.01", false) } },
            { "convert", new[] { new P("M4.92 6.5L8.08 6.5C9.14039 6.5 10 7.35961 10 8.42L10 15.58C10 16.6404 9.14039 17.5 8.08 17.5L4.92 17.5C3.85961 17.5 3 16.6404 3 15.58L3 8.42C3 7.35961 3.85961 6.5 4.92 6.5Z", false), new P("M15.92 6.5L19.08 6.5C20.1404 6.5 21 7.35961 21 8.42L21 15.58C21 16.6404 20.1404 17.5 19.08 17.5L15.92 17.5C14.8596 17.5 14 16.6404 14 15.58L14 8.42C14 7.35961 14.8596 6.5 15.92 6.5Z", false), new P("M10 12h4", false), new P("M12.3 9.8l2.2 2.2-2.2 2.2", false) } },
            { "replace", new[] { new P("M3.5 8.5h13.5", false), new P("M14 5.5l3 3-3 3", false), new P("M20.5 15.5H7", false), new P("M10 12.5l-3 3 3 3", false) } },
            { "trash", new[] { new P("M3.5 6.5h17", false), new P("M9 6.5V3.5h6v3", false), new P("M6.15 8.65A2.16 2.16 0 0 1 8.16 6.5L15.84 6.5A2.16 2.16 0 0 1 17.85 8.65L17.15 18.85A2.16 2.16 0 0 1 14.84 21L9.16 21A2.16 2.16 0 0 1 6.85 18.85Z", false), new P("M10 11v6M14 11v6", false) } },
            { "cache", new[] { new P("M8.68 6.5L15.32 6.5C16.2478 6.5 17 7.25216 17 8.18L17 15.82C17 16.7478 16.2478 17.5 15.32 17.5L8.68 17.5C7.75216 17.5 7 16.7478 7 15.82L7 8.18C7 7.25216 7.75216 6.5 8.68 6.5Z", false), new P("M10 3v3.5M14 3v3.5M10 17.5V21M14 17.5V21", false), new P("M3 9.5h4M3 14.5h4M17 9.5h4M17 14.5h4", false) } },
            { "file", new[] { new P("M5 5.4A2.4 2.4 0 0 1 7.4 3L14 3L19 8L19 18.6A2.4 2.4 0 0 1 16.6 21L7.4 21A2.4 2.4 0 0 1 5 18.6Z", false), new P("M14 3v5h5", false) } },
            { "download", new[] { new P("M12 4v12.5", false), new P("M7 11.5l5 5 5-5", false), new P("M4 17.5v2A1.5 1.5 0 0 0 5.5 21h13a1.5 1.5 0 0 0 1.5-1.5v-2", false) } },
            { "copy", new[] { new P("M11.4 9L18.6 9C19.9255 9 21 10.0745 21 11.4L21 18.6C21 19.9255 19.9255 21 18.6 21L11.4 21C10.0745 21 9 19.9255 9 18.6L9 11.4C9 10.0745 10.0745 9 11.4 9Z", false), new P("M15 9V5a1 1 0 0 0-1-1H5a1 1 0 0 0-1 1v9a1 1 0 0 0 1 1h3", false) } },
            { "check", new[] { new P("M4.5 12.5l5 5L19.5 7", false) } },
            { "eye", new[] { new P("M2.5 12S6.2 5.5 12 5.5 21.5 12 21.5 12 17.8 18.5 12 18.5 2.5 12 2.5 12z", false), new P("M12 9C13.6569 9 15 10.3431 15 12C15 13.6569 13.6569 15 12 15C10.3431 15 9 13.6569 9 12C9 10.3431 10.3431 9 12 9Z", false) } },
            { "play", new[] { new P("M8 5.92A1.92 1.92 0 0 1 9.6 5.07L18.4 10.93A1.92 1.92 0 0 1 18.4 13.07L9.6 18.93A1.92 1.92 0 0 1 8 18.08Z", false) } },
            { "stop", new[] { new P("M7.92 6L16.08 6C17.1404 6 18 6.85961 18 7.92L18 16.08C18 17.1404 17.1404 18 16.08 18L7.92 18C6.85961 18 6 17.1404 6 16.08L6 7.92C6 6.85961 6.85961 6 7.92 6Z", false) } },
        };

        /// <summary>
        /// 笔宽 = max(1.4, 24 × 1.05 / 尺寸)：18px 及以上直接用素材标称值，
        /// 16px 及以下加粗，保证等效物理笔宽 ≥1.05px（规范 §三.2 的公式）。
        /// </summary>
        static double Thickness(double size)
        {
            return size <= 0 ? 1.4 : Math.Max(1.4, 25.2 / size);
        }

        /// <summary>当前程序图标表中的全部键，供 UI 自检逐个验证实际渲染结果。</summary>
        public static IEnumerable<string> Keys { get { return Paths.Keys; } }

        /// <summary>调试渲染自检期间，未知键必须立即失败，不能静默缺图。</summary>
        public static bool FailOnMissing { get; set; }

        /// <summary>生成一个线性图标；名字不存在时返回 null（调用方自己决定怎么兜底）。</summary>
        public static UIElement Get(string name, Brush stroke, double size = 16)
        {
            P[] parts;
            if (name == null) return null;
            if (!Paths.TryGetValue(name, out parts))
            {
                if (FailOnMissing) throw new KeyNotFoundException("图标表里没有键：「" + name + "」");
                return null;
            }

            var canvas = new Canvas { Width = 24, Height = 24 };   // 24 栅格：缩放比钉死在 size/24
            foreach (var part in parts)
            {
                var p = new System.Windows.Shapes.Path
                {
                    Data = Geometry.Parse(part.D),
                    Stroke = part.Fill ? null : stroke,
                    Fill = part.Fill ? stroke : null,
                    StrokeThickness = Thickness(size),
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    StrokeLineJoin = PenLineJoin.Round,
                    StrokeDashCap = PenLineCap.Round
                };
                canvas.Children.Add(p);
            }

            return new Viewbox
            {
                Width = size,
                Height = size,
                Child = canvas,
                Stretch = Stretch.Uniform,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        /// <summary>勾选标记（同组互斥的选中态，用强调色；尺寸 14，笔宽按公式 =1.8）。</summary>
        public static UIElement Check(Brush stroke, double size = 14)
        {
            return Get("check", stroke, size);
        }
    }
}
