using System;
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
    /// 只能靠 RenderTargetBitmap 离屏渲染来验证菜单、关于框等 WPF 界面。
    /// 同时会断言菜单弹层真的能打开（popup.IsOpen）。
    /// </summary>
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
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
                FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI"),
                FontSize = 12.5
            };

            var menu = new Menu();
            var built = ComicConverter.MenuBuilder.Build(menu, delegate { });

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

            // 1) 菜单栏
            Save(menu, Path.Combine(outDir, "menu-bar.png"));

            // 2) 打开「文件」菜单，渲染它的弹层
            var fileMenu = (MenuItem)menu.Items[0];
            fileMenu.IsSubmenuOpen = true;
            Pump(500);

            var popup = fileMenu.Template.FindName("PART_Popup", fileMenu) as System.Windows.Controls.Primitives.Popup;
            Console.WriteLine("popup open = " + (popup != null && popup.IsOpen));

            if (popup != null && popup.Child is FrameworkElement child)
            {
                child.UpdateLayout();
                Pump(300);
                Console.WriteLine("popup child size = " + child.ActualWidth + "x" + child.ActualHeight);
                Save(child, Path.Combine(outDir, "menu-file.png"));
                fileMenu.IsSubmenuOpen = false;
                Pump(200);
            }

            // 3) 「查看」菜单（带勾选项）
            var viewMenu = (MenuItem)menu.Items[2];
            foreach (var o in viewMenu.Items)
            {
                var mi = o as MenuItem;
                if (mi != null && mi.Tag is ComicConverter.AppMenu.Item it && it.Group == "reading" && it.Value == "auto")
                    mi.IsChecked = true;
            }
            viewMenu.IsSubmenuOpen = true;
            Pump(500);
            var vp = viewMenu.Template.FindName("PART_Popup", viewMenu) as System.Windows.Controls.Primitives.Popup;
            if (vp != null && vp.Child is FrameworkElement vc)
            {
                vc.UpdateLayout();
                Pump(300);
                Save(vc, Path.Combine(outDir, "menu-view.png"));
                viewMenu.IsSubmenuOpen = false;
                Pump(150);
            }


            // 5) 「帮助」菜单（关于 + 邮箱）
            var helpMenu = (MenuItem)menu.Items[4];
            helpMenu.IsSubmenuOpen = true;
            Pump(500);
            var hp = helpMenu.Template.FindName("PART_Popup", helpMenu) as System.Windows.Controls.Primitives.Popup;
            if (hp != null && hp.Child is FrameworkElement hc)
            {
                hc.UpdateLayout(); Pump(250);
                Save(hc, Path.Combine(outDir, "menu-help.png"));
                helpMenu.IsSubmenuOpen = false; Pump(150);
            }

            // 4) 关于对话框
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
                                  + " blocks=" + doc.Document.Blocks.Count
                                  + " asm=" + System.IO.File.GetLastWriteTime(typeof(ComicConverter.AboutDialog).Assembly.Location).ToString("HH:mm:ss"));
            }

            Save((FrameworkElement)about.Content, Path.Combine(outDir, "about.png"));
            Console.WriteLine("about size = " + ((FrameworkElement)about.Content).ActualWidth + "x"
                                               + ((FrameworkElement)about.Content).ActualHeight);

            // 6) 关于框「点击邮箱后」的状态：反射触发私有 Email_Click
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

            win.Close();
            app.Shutdown();
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
