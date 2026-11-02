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
            string outDir = args.Length > 0 ? args[0] : DefaultOutDir();
            Directory.CreateDirectory(outDir);

            var app = new Application();
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/ComicConverter;component/Theme.xaml")
            });

            ComicConverter.Accent.Init();
            Console.WriteLine("accent = " + ComicConverter.Accent.BrandHex);
            try
            {
                CheckIconRendering();
            }
            catch (Exception ex)
            {
                Failures.Add("图标渲染自检异常：" + ex);
            }

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
            double aboutWidth = about.Width;
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

                // ---- 宽度出处（规范 §七.2：常量必须是「量过之后落下来的数」）----
                // 逐段按「不换行」量自然宽，取最宽的那段 —— XAML 里 Width=640 的依据就写在下面这行输出里。
                ReportNaturalWidth(doc);
                Console.WriteLine("doc width = " + Math.Round(doc.ActualWidth, 1)
                                  + "  (= 640 - 窗口 margin 20x2 - 卡片边框 1x2 - 卡片 padding 14x2)");
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
            double errWidth = err.Width;
            var body = err.FindName("BodyText") as TextBlock;
            if (body != null)
                Console.WriteLine("dlg body width = " + Math.Round(body.ActualWidth, 1)
                                  + "  (窗口 " + Math.Round(err.Width, 1) + " 里扣掉卡片与图标列之后)");
            Save((FrameworkElement)err.Content, Path.Combine(outDir, "dlg-error.png"));
            err.Close();

            // 6) 宽度口径（规范 §10.4）：宽度必须运行时按内容量 —— 同一程序里不同的框宽度要不同。
            //    拿同一个错误框、换一句更短的正文，宽度应当跟着变；两者相等就说明又写死了一个数。
            //    顺带验「改宽之后有没有把居中带歪」：量宽发生在 OnSourceInitialized，
            //    WPF 自己那次居中用的是改宽前的尺寸，所以对话框必须自己重摆水平位置。
            var brief = new ComicConverter.MsgDialog(win, "输出目录不可写", "换个目录再试。", null)
                        { ShowInTaskbar = false };
            brief.Show();
            double centerLeft = brief.Left;
            brief.Left = -4000;
            brief.Top = 0;
            Pump(500);
            brief.UpdateLayout();
            Pump(200);

            double wantLeft = win.Left + (win.ActualWidth - brief.Width) / 2;
            Console.WriteLine("width: long=" + Math.Round(errWidth, 1) + " brief=" + Math.Round(brief.Width, 1)
                              + " about=" + Math.Round(aboutWidth, 1)
                              + " | center left=" + Math.Round(centerLeft, 1)
                              + " want=" + Math.Round(wantLeft, 1));
            Save((FrameworkElement)brief.Content, Path.Combine(outDir, "dlg-error-brief.png"));
            brief.Close();

            if (Math.Abs(errWidth - brief.Width) < 1)
                Failures.Add("两个不同内容的错误框宽度相同（" + Math.Round(errWidth, 1)
                             + "），宽度像是写死的，不是按内容量的（§10.4）");
            foreach (double w in new[] { errWidth, brief.Width, aboutWidth })
            {
                if (w < 320 - 0.5 || w > 640 + 0.5)
                    Failures.Add("对话框宽度 " + Math.Round(w, 1) + " 落在 320~640 之外（§七.2）");
            }
            if (Math.Abs(centerLeft - wantLeft) > 1)
                Failures.Add("对话框改宽后没有重新居中：left=" + Math.Round(centerLeft, 1)
                             + " want=" + Math.Round(wantLeft, 1));

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
        /// 实际程序图标逐键离屏渲染断言（图标规范 §十一 闸 5）。
        /// 仅检查 Get 未抛异常或返回非空控件不够：WPF 路径可能静默渲染为空白。
        /// </summary>
        static void CheckIconRendering()
        {
            ComicConverter.Icons.FailOnMissing = true;
            int passed = 0;
            int total = 0;

            try
            {
                ComicConverter.Icons.Get("__icon-selftest-missing-key__", Brushes.Black, 16);
                Failures.Add("未知图标键没有触发自检失败（FailOnMissing 未生效）");
            }
            catch (KeyNotFoundException)
            {
                // 预期：未知键不能静默缺失。
            }
            foreach (var key in ComicConverter.Icons.Keys)
            {
                total++;
                try
                {
                    var element = ComicConverter.Icons.Get(key, Brushes.Black, 16) as FrameworkElement;
                    if (element == null)
                    {
                        Failures.Add("图标 " + key + " 返回空控件");
                        continue;
                    }

                    element.Measure(new Size(16, 16));
                    element.Arrange(new Rect(0, 0, 16, 16));
                    element.UpdateLayout();

                var bitmap = new RenderTargetBitmap(64, 64, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(element);
                var pixels = new int[64 * 64];
                bitmap.CopyPixels(pixels, 64 * 4, 0);
                int visible = 0;
                int minX = 64, minY = 64, maxX = -1, maxY = -1;
                for (int y = 0; y < 64; y++)
                {
                    for (int x = 0; x < 64; x++)
                    {
                        int alpha = (int)((uint)pixels[y * 64 + x] >> 24);
                        if (alpha <= 8) continue;
                        visible++;
                        minX = Math.Min(minX, x);
                        minY = Math.Min(minY, y);
                        maxX = Math.Max(maxX, x);
                        maxY = Math.Max(maxY, y);
                    }
                }

                if (visible == 0 || maxX <= minX || maxY <= minY)
                    Failures.Add("图标 " + key + " 渲染后没有可见墨迹（pixels=" + visible
                                 + " bounds=" + minX + "," + minY + ".." + maxX + "," + maxY + ")");
                else
                {
                    Console.WriteLine("icon " + key + ": " + visible + " pixels, bounds "
                                      + minX + "," + minY + ".." + maxX + "," + maxY);
                    passed++;
                }
                }
                catch (Exception ex)
                {
                    Failures.Add("图标 " + key + " 渲染失败：" + ex.GetType().Name + " " + ex.Message);
                }
            }

            if (total == 0)
                Failures.Add("图标渲染自检失败：Icons 表为空");
            Console.WriteLine("icon render = " + passed + "/" + total);
        }

        /// <summary>
        /// 菜单结构断言（规范 §十）：一级菜单 ≥ 2 栏、每栏至少一项可用，
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

        /// <summary>
        /// 不给输出目录时落到 tools/_shots —— 别默认写当前目录，否则从仓库根跑一次
        /// 就会在源码目录里散落一堆 png。
        /// </summary>
        static string DefaultOutDir()
        {
            var d = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (d != null)
            {
                if (Directory.Exists(Path.Combine(d.FullName, "src"))
                    && Directory.Exists(Path.Combine(d.FullName, "tools")))
                    return Path.Combine(d.FullName, "tools", "_shots");
                d = d.Parent;
            }
            return ".";
        }

        static void Pump(int ms)
        {
            var frame = new DispatcherFrame();
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
            t.Tick += delegate { t.Stop(); frame.Continue = false; };
            t.Start();
            Dispatcher.PushFrame(frame);
        }

        /// <summary>
        /// 量关于框内容的「不换行自然宽」，给 XAML 里 Width=640 一个可复现的出处（规范 §七.2）。
        /// 逐段用 FormattedText 按段自身的字号/字重量一遍，取最宽的一段打印；
        /// 同时打印正文可用宽，两者一比就知道「640 是量出来的」还是「640 是阅读行宽上限、正文在折行」。
        /// </summary>
        static void ReportNaturalWidth(RichTextBox rtb)
        {
            var flow = rtb.Document;
            var family = rtb.FontFamily ?? new FontFamily("Microsoft YaHei UI");
            double dpi = VisualTreeHelper.GetDpi(rtb).PixelsPerDip;
            double maxW = 0;
            string maxText = "";

            foreach (var blk in flow.Blocks)
            {
                var p = blk as System.Windows.Documents.Paragraph;
                if (p == null) continue;

                double size = p.FontSize;
                if (double.IsNaN(size) || size <= 0) size = flow.FontSize;
                var weight = p.FontWeight;

                // 按「行」量，不按段量：一个段落里可能有多条硬换行（如快捷键块），
                // 段宽是几行拼起来的，不是 §七.2 要的「最长的一行不可断内容」。
                string raw = new System.Windows.Documents.TextRange(p.ContentStart, p.ContentEnd).Text;
                foreach (var line in raw.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
                {
                    string t = line.Trim();
                    if (t.Length == 0) continue;

                    var ft = new FormattedText(t, System.Globalization.CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        new Typeface(family, FontStyles.Normal, weight, FontStretches.Normal),
                        size, Brushes.Black, dpi);
                    double w = ft.WidthIncludingTrailingWhitespace;
                    if (w > maxW) { maxW = w; maxText = t; }
                }
            }

            string head = maxText.Length <= 26 ? maxText : maxText.Substring(0, 26) + "…";
            Console.WriteLine("natural-width max = " + Math.Round(maxW, 1) + "  [" + head + "]");
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
