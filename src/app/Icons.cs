using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Markup;

namespace ComicConverter
{
    /// <summary>
    /// 内置 Fluent 风格线性图标（描边 24×24，运行时缩到 16×16），不依赖图片资源。
    /// 路径数据与 txtFormat 同源，保证两个程序的菜单观感一致。
    /// 用法：Icons.Get("folder", 画刷)。
    /// </summary>
    public static class Icons
    {
        static readonly Dictionary<string, string[]> Paths = new Dictionary<string, string[]>
        {
            { "plus",     new[] { "M12 5v14M5 12h14" } },
            { "folder",   new[] { "M3 7a1 1 0 0 1 1-1h5l2 2h8a1 1 0 0 1 1 1v9a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1z" } },
            { "open",     new[] { "M4 8h11l-3-3", "M20 16H7l3 3" } },
            { "window",   new[] { "M5 4h14a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2z", "M3 9h18" } },
            { "close",    new[] { "M6 6l12 12M18 6L6 18" } },
            { "clean",    new[] { "M5 19h14", "M7 15l7-9 4 3-7 9z", "M10 18l-1.5 2" } },
            { "list",     new[] { "M8 6h12M8 12h12M8 18h12", "M4.5 6h.01M4.5 12h.01M4.5 18h.01" } },
            { "refresh",  new[] { "M20 12a8 8 0 1 1-2.3-5.6", "M20 4v4h-4" } },
            { "settings", new[] { "M4 7h9M17 7h3M4 12h3M11 12h9M4 17h9M21 17h-1", "M15 5v4M9 10v4M15 15v4" } },
            { "mail",     new[] { "M3 7.5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2v9a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z", "M3.6 9l7.3 5.2a2 2 0 0 0 2.2 0L20.4 9" } },
            { "info",     new[] { "M12 3.5a8.5 8.5 0 1 1 0 17 8.5 8.5 0 0 1 0-17z", "M12 11v6", "M12 7.6h.01" } },
            { "alert",    new[] { "M12 3.5a8.5 8.5 0 1 1 0 17 8.5 8.5 0 0 1 0-17z", "M12 7.8v5.4", "M12 16.4h.01" } },
            { "convert",  new[] { "M4 9h13l-3-3", "M20 15H7l3 3" } },
            { "replace",  new[] { "M4 8h11l-3-3", "M20 16H9l3 3" } },
            { "trash",    new[] { "M4 7h16", "M9 7V4h6v3", "M6 7l1 13h10l1-13" } },
            { "db",       new[] { "M12 3c-3.9 0-7 1.3-7 3s3.1 3 7 3 7-1.3 7-3-3.1-3-7-3z", "M5 6v12c0 1.7 3.1 3 7 3s7-1.3 7-3V6", "M5 12c0 1.7 3.1 3 7 3s7-1.3 7-3" } },
            { "file",     new[] { "M7 3h7l4 4v14H7z", "M14 3v4h4" } },
            { "download", new[] { "M12 4v11", "M7.5 10.5L12 15l4.5-4.5", "M4 19h16" } },
            { "copy",     new[] { "M11 9h9a2 2 0 0 1 2 2v9a2 2 0 0 1-2 2h-9a2 2 0 0 1-2-2v-9a2 2 0 0 1 2-2z", "M15 9V6a2 2 0 0 0-2-2H6a2 2 0 0 0-2 2v7a2 2 0 0 0 2 2h3" } },
            { "check",    new[] { "M4.5 12.5l5 5L19.5 7" } },
            { "eye",      new[] { "M2.5 12s3.6-6.5 9.5-6.5S21.5 12 21.5 12 17.9 18.5 12 18.5 2.5 12 2.5 12z", "M12 15a3 3 0 1 0 0-6 3 3 0 0 0 0 6z" } },
            { "play",     new[] { "M8 5.5l11 6.5-11 6.5z" } },
            { "stop",     new[] { "M7 7h10v10H7z" } },
        };

        /// <summary>生成一个 16×16 的线性图标；名字不存在时返回 null。</summary>
        public static UIElement Get(string name, Brush stroke, double size = 16)
        {
            string[] list;
            if (name == null || !Paths.TryGetValue(name, out list)) return null;

            var canvas = new Canvas { Width = 24, Height = 24 };
            foreach (var d in list)
            {
                var p = new System.Windows.Shapes.Path
                {
                    Data = Geometry.Parse(d),
                    Stroke = stroke,
                    StrokeThickness = 1.6,
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

        /// <summary>勾选标记（选中态，用强调色）。</summary>
        public static UIElement Check(Brush stroke, double size = 14)
        {
            return Get("check", stroke, size);
        }
    }
}