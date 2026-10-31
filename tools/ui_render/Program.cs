using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ComicRenderer
{
    /// <summary>
    /// 界面离屏渲染器（开发工具，不进安装包）。
    /// 用法: dotnet run -c Release --project tools/ui_render -- <输出目录>
    /// 说明：本机/沙箱里 WPF 窗口用 PrintWindow、屏幕 BitBlt 抓出来全是纯黑，
    /// 只能靠 RenderTargetBitmap 离屏渲染来验证菜单、关于框、错误框。
    /// 同时按规范 §十 做源码级自检：菜单结构四条断言 + 弹层真的能打开。
    /// </summary>
    static class Program
    {
        static readonly List<string> Failures = new List<string>();

        [STAThread]
        static int Main(string[] args)
        {
            string outDir = args.Length > 0 ? args[0] : ".";
            Directory.CreateDirectory(outDir);

            var app = new Application();
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/ComicConverter;component/Theme.xaml")
            });

            ComicConverter.Accent.Init();
            Console.WriteLine("accent = " + ComicConverter.Accent.BrandHex);

            var win = new Window
            {
                Width = 560, Height = 420,
                WindowStyle = WindowStyle.None,
                ShowInTaskbar = false,
                Left = -4000, Top = 0,
                Background = (Brush)app.FindResource("WindowBrush"),
                FontFamily = new FontFamily("Microsoft YaHei UI"),
                FontSize = 12.5
            };

            var menu = new Menu();
            ComicConverter.MenuBuilder.Build(menu, delegate { });

            var root = new DockPanel();
            DockPanel.SetDock(menu, Dock.Top);
            root.Children.Add(menu);
            root.Children.Add(new Border
            {
                Background = (Brush)app.FindResource("WindowBrush"),
                Margin = new Thickness(14)
            });
            win.Content = root;

            win.Show();
            Pump(400);
            win.UpdateLayout();
            Pump(200);

            // ---- 菜单结构自检（规范 §十：二级栏数、每栏非空、帮助在、about 在）
            CheckMenu(menu);

            // 1) 菜单栏
            Save(menu, Path.Combine(outDir, "menu-bar.png"));

            // 2) 逐个展开一级菜单并渲染弹层。按标题找，不按索引 —— 加一栏就错位的写法不要。
            ShotPopup(menu, "转换", outDir, "menu-convert.png", false);
            ShotPopup(menu, "输出", outDir, "menu-output.png", true);
            ShotPopup(menu, "工具", outDir, "menu-tools.png", false);
            ShotPopup(menu, "帮助", outDir, "menu-help.png", false);

            // 2b) 启动画面图标：BrandIcon 必须给出 ico 里的最大帧。
            //     WPF 的 ICO 解码器会把帧序重排（首帧只有 48px），
            //     直接 Source="app.ico" 拿 48px 源去填 60px 启动位会发虚。
            var bi = ComicConverter.BrandIcon.Source;
            if (bi == null)
                Failures.Add("BrandIcon.Source 取不到（" + ComicConverter.BrandIcon.LastError + "）");
            else
            {
                Console.WriteLine("brand icon = " + bi.PixelWidth + "x" + bi.PixelHeight);
                if (bi.PixelWidth < 256)
                    Failures.Add("BrandIcon 只取到 " + bi.PixelWidth + "px 帧（应挑最大帧 256）");
                var splash = new StackPanel();
                splash.Children.Add(new Image { Source = bi, Width = 60, Height = 60 });
                splash.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                splash.Arrange(new Rect(0, 0, 60, 60));
                splash.UpdateLayout();
                Save(splash, Path.Combine(outDir, "splash-icon.png"));
            }

            // 3) 关于对话框
            var about = new ComicConverter.AboutDialog { ShowInTaskbar = false, Left = -4000, Top = 0 };
            about.Show();
            Pump(600);
            about.UpdateLayout();
            Pump(300);
            Console.WriteLine("about win=" + about.Width + "x" + about.Height + " resize=" + about.ResizeMode);
            var doc = about.FindName("Doc") as RichTextBox;
            if (doc != null)
            {
                Console.WriteLine("doc extent=" + doc.ExtentHeight + " viewport=" + doc.ViewportHeight
                                  + " overflow=" + (doc.ExtentHeight > doc.ViewportHeight + 0.5));
                Console.WriteLine("doc padding=" + doc.Padding + " border=" + doc.BorderThickness
                                  + " pagePadding=" + doc.Document.PagePadding
                                  + " docFont=" + doc.FontFamily + "/" + doc.FontSize);
                var first = doc.Document.Blocks.FirstBlock as System.Windows.Documents.Paragraph;
                var last = doc.Document.Blocks.LastBlock as System.Windows.Documents.Paragraph;
                Console.WriteLine("first margin=" + (first == null ? "-" : first.Margin.ToString())
                                  + " last margin=" + (last == null ? "-" : last.Margin.ToString())
                                  + " blocks=" + doc.Document.Blocks.Count);

                // PagePadding 必须是 0：RichTextBox 会把它复位成 5,0,5,0，正文于是整体右移 5px
                var pad = doc.Document.PagePadding;
                if (Math.Abs(pad.Left) > 0.01 || Math.Abs(pad.Right) > 0.01 || Math.Abs(pad.Top) > 0.01)
                    Failures.Add("关于框 PagePadding 被复位成 " + pad + "（正文会右移 " + pad.Left + "px）");
            }

            Save((FrameworkElement)about.Content, Path.Combine(outDir, "about.png"));
            Console.WriteLine("about size = " + ((FrameworkElement)about.Content).ActualWidth + "x"
                                               + ((FrameworkElement)about.Content).ActualHeight);

            // 4) 关于框「点击邮箱后」的状态：反射触发私有 Email_Click
            var click = typeof(ComicConverter.AboutDialog).GetMethod(
                "Email_Click", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (click != null)
            {
                click.Invoke(about, new object[] { null, null });
                Pump(300);
                about.UpdateLayout();
                Pump(200);
                Save((FrameworkElement)about.Content, Path.Combine(outDir, "about-copied.png"));
            }
            about.Close();

            // 5) 错误框（唯一保留的自绘对话框）：带「打开日志 / 关闭」两个按钮
            var err = new ComicConverter.MsgDialog(null, "刚才的操作没有完成",
                "写入 D:\\Library\\out\\某系列 v01.cbz 失败：该目录是只读的。\n\n"
                + "错误详情已写入 error.log，程序可以继续使用。",
                "D:\\Library\\error.log")
            { ShowInTaskbar = false };
            err.Show();
            err.Left = -4000;
            err.Top = 0;
            Pump(600);
            err.UpdateLayout();
            Pump(300);
            Console.WriteLine("err-dlg size=" + err.Width + "x" + err.Height + " title=\"" + err.Title + "\"");
            Save((FrameworkElement)err.Content, Path.Combine(outDir, "dlg-error.png"));
            err.Close();

            win.Close();
            app.Shutdown();

            if (Failures.Count > 0)
            {
                Console.WriteLine();
                foreach (var f in Failures) Console.WriteLine("! " + f);
                return 1;
            }
            Console.WriteLine("self-check: OK");
            return 0;
        }

        /// <summary>
        /// 菜单结构断言（规范 §十）：一级菜单 ≥ 2 栏、每栏至少一项可用、
        /// 存在「帮助」栏且其下有 about 命令。挡的是「凑数空菜单」「删着删着把帮助删了」。
        /// </summary>
        static void CheckMenu(Menu menu)
        {
            if (menu.Items.Count < 2)
                Failures.Add("一级菜单只有 " + menu.Items.Count + " 栏（应 ≥ 2）");

            MenuItem help = null;
            foreach (var o in menu.Items)
            {
                var top = o as MenuItem;
                if (top == null) { Failures.Add("一级菜单里出现了非 MenuItem 的东西"); continue; }

                string header = Convert.ToString(top.Header);
                int usable = 0;
                foreach (var c in top.Items)
                {
                    var mi = c as MenuItem;
                    if (mi != null && mi.IsEnabled) usable++;
                }
                if (usable == 0) Failures.Add("「" + Clean(header) + "」展开后没有可用命令（空菜单）");
                if (header != null && header.Contains("帮助")) help = top;
            }

            if (help == null) Failures.Add("找不到「帮助」一级菜单");
            else
            {
                bool about = false;
                foreach (var c in help.Items)
                {
                    var mi = c as MenuItem;
                    var it = mi == null ? null : mi.Tag as ComicConverter.AppMenu.Item;
                    if (it != null && it.Command == "about") about = true;
                }
                if (!about) Failures.Add("「帮助」里找不到 about 命令");
            }

            Console.WriteLine("menu cols = " + menu.Items.Count
                              + " / help=" + (help != null) + " / fails=" + Failures.Count);
        }

        /// <summary>展开标题包含 <paramref name="word"/> 的一级菜单，把弹层单独渲染成图。</summary>
        static void ShotPopup(Menu menu, string word, string outDir, string file, bool checkReading)
        {
            MenuItem top = null;
            foreach (var o in menu.Items)
            {
                var mi = o as MenuItem;
                var header = mi == null ? null : Convert.ToString(mi.Header);
                if (header != null && header.Contains(word)) { top = mi; break; }
            }
            if (top == null)
            {
                Failures.Add("找不到「" + word + "」菜单");
                return;
            }

            if (checkReading)
            {
                // 勾选态：让「自动判断」显示强调色勾，验证同组互斥与图标/勾互斥
                foreach (var o in top.Items)
                {
                    var mi = o as MenuItem;
                    if (mi != null && mi.Tag is ComicConverter.AppMenu.Item it
                        && it.Group == "reading" && it.Value == "auto") mi.IsChecked = true;
                }
            }

            top.IsSubmenuOpen = true;
            Pump(500);

            var popup = top.Template.FindName("PART_Popup", top) as System.Windows.Controls.Primitives.Popup;
            if (popup == null || !popup.IsOpen)
            {
                // 偶发：宿主忙时 500ms 不够弹层展开，收起重开一次再判失败
                top.IsSubmenuOpen = false;
                Pump(200);
                top.IsSubmenuOpen = true;
                Pump(900);
                popup = top.Template.FindName("PART_Popup", top) as System.Windows.Controls.Primitives.Popup;
            }
            if (popup == null || !popup.IsOpen)
            {
                Failures.Add("「" + word + "」弹层没打开（PART_Popup 缺失或未展开）");
                top.IsSubmenuOpen = false;
                return;
            }

            if (popup.Child is FrameworkElement child)
            {
                child.UpdateLayout();
                Pump(300);
                Console.WriteLine("popup(" + word + ") = " + child.ActualWidth + "x" + child.ActualHeight);
                Save(child, Path.Combine(outDir, file));
            }

            top.IsSubmenuOpen = false;
            Pump(200);
        }

        static string Clean(string header)
        {
            if (string.IsNullOrEmpty(header)) return "";
            int i = header.IndexOf('(');
            return i > 0 ? header.Substring(0, i) : header;
        }

        static void Pump(int ms)
        {
            var frame = new DispatcherFrame();
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
            t.Tick += delegate { t.Stop(); frame.Continue = false; };
            t.Start();
            Dispatcher.PushFrame(frame);
        }

        static void Save(FrameworkElement el, string path)
        {
            el.UpdateLayout();
            // 外边距也要算进去：VisualBrush 只取元素自身的内容区，不画 Margin，
            // 漏掉的话截出来的图会"按钮贴底、卡片右边框缺失"，看着像布局 bug。
            var m = el.Margin;
            int w = (int)Math.Ceiling(el.ActualWidth + m.Left + m.Right);
            int h = (int)Math.Ceiling(el.ActualHeight + m.Top + m.Bottom);
            if (w <= 0 || h <= 0) { Console.WriteLine("skip " + path); return; }

            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, w, h));
                dc.DrawRectangle(new VisualBrush(el), null,
                                 new Rect(m.Left, m.Top, el.ActualWidth, el.ActualHeight));
            }
            rtb.Render(dv);

            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using (var fs = File.Create(path)) enc.Save(fs);
            Console.WriteLine("saved " + Path.GetFileName(path) + " " + w + "x" + h);
        }
    }
}
