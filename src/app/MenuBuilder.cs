using System;
using System.Collections.Generic;
using System.Windows.Controls;
using System.Windows.Media;

namespace ComicConverter
{
    /// <summary>
    /// 把 AppMenu 的数据表变成真实的 MenuItem。
    /// 抽出来是为了能被独立渲染验证（不依赖 WebView2 主窗口）。
    /// </summary>
    public static class MenuBuilder
    {
        public sealed class Built
        {
            /// <summary>所有可点击子项（不含分隔线与一级标题），顺序同数据表。</summary>
            public readonly List<MenuItem> Items = new List<MenuItem>();
        }

        public static Built Build(Menu target, Action<AppMenu.Item> onItem)
        {
            var built = new Built();
            Brush iconBrush = Application_Resource("InkBrush");

            foreach (var group in AppMenu.Build())
            {
                var top = new MenuItem { Header = group.Key };

                foreach (var it in group.Value)
                {
                    if (it.Text == "-" || it.Command == null)
                    {
                        top.Items.Add(new Separator());
                        continue;
                    }

                    var mi = new MenuItem
                    {
                        Header = it.Text,
                        InputGestureText = it.Gesture,
                        IsCheckable = it.Group != null,
                        Tag = it
                    };
                    if (it.Group == null && it.Icon != null)
                        mi.Icon = Icons.Get(it.Icon, iconBrush);

                    var captured = it;
                    mi.Click += delegate { onItem(captured); };

                    top.Items.Add(mi);
                    built.Items.Add(mi);
                }

                target.Items.Add(top);
            }

            return built;
        }

        static Brush Application_Resource(string key)
        {
            var app = System.Windows.Application.Current;
            var b = app == null ? null : app.TryFindResource(key) as Brush;
            return b ?? Brushes.DimGray;
        }
    }
}