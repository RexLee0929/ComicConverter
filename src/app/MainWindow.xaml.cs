using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;

namespace ComicConverter
{
    public partial class MainWindow : Window
    {
        /// <summary>命令 id → 菜单项。用于回填勾选状态。</summary>
        readonly Dictionary<string, MenuItem> _byCommand = new Dictionary<string, MenuItem>();

        /// <summary>分组 → 当前选中值（mode / reading / target）。</summary>
        readonly Dictionary<string, string> _checked = new Dictionary<string, string>();

        public MainWindow()
        {
            InitializeComponent();

            TxtVersion.Text = "版本 " + Brand.Version;
            BuildMenu();

            Loaded += OnLoaded;
            Closed += delegate { Api.Stop(); };
        }

        // ------------------------------------------------------------ 菜单

        /// <summary>
        /// 菜单结构完全来自 AppMenu.Build()，XAML 里不写死任何一项：
        /// 加功能 = 在 AppMenu 里加一条 + 在本类的 Run 里加一个分支。
        /// </summary>
        void BuildMenu()
        {
            var built = MenuBuilder.Build(MainMenu, Run);
            foreach (var mi in built.Items)
            {
                var it = mi.Tag as AppMenu.Item;
                if (it != null) _byCommand[it.Command] = mi;
            }

            SetCheck("mode", "convert");
            SetCheck("reading", "auto");
            SetCheck("target", "both");
        }

        /// <summary>同组内互斥勾选，并记住当前值。</summary>
        void SetCheck(string group, string value)        {
            _checked[group] = value;
            foreach (var mi in _byCommand.Values)
            {
                var it = mi.Tag as AppMenu.Item;
                if (it != null && it.Group == group)
                    mi.IsChecked = it.Value == value;
            }
        }

        // ------------------------------------------------------------ 分发

        /// <summary>菜单命令的唯一分发点。新增菜单项时在这里加一个 case。</summary>
        void Run(AppMenu.Item it)
        {
            // 可勾选项：只更新勾号并同步到界面，真正的状态由页面回写
            if (it.Group != null)
            {
                SetCheck(it.Group, it.Value);
                if (it.Group == "mode") Js("UI.setMode('" + it.Value + "')");
                else if (it.Group == "reading") Js("UI.setReading('" + it.Value + "')");
                else if (it.Group == "target") Js("UI.setTarget('" + it.Value + "')");
                return;
            }

            switch (it.Command)
            {
                case "add-source": Js("UI.addSource()"); break;
                case "pick-out": Js("UI.pickOut()"); break;

                case "open-out": OpenFolder(GetCfgString("out")); break;
                case "open-appdir": OpenFolder(AppDomain.CurrentDomain.BaseDirectory); break;
                case "open-config-dir": OpenFolder(Path.GetDirectoryName(Api.ConfigPath)); break;
                case "open-cache": OpenFolder(CacheDir); break;
                case "open-log": OpenPath(App.LogPath); break;

                case "clear-log": Js("UI.clearLog()"); break;
                case "clear-preview": Js("UI.clearPreview()"); break;

                case "clear-cache": ClearCache(); break;

                case "copy-email": CopyEmail(); break;

                case "reset-config": ResetConfig(); break;

                case "reload": Js("UI.reload()"); break;

                case "about": ShowAbout(); break;

                case "webview2":
                    OpenUrl("https://developer.microsoft.com/microsoft-edge/webview2/");
                    break;

                case "exit": Close(); break;
            }
        }

        // ------------------------------------------------------------ 小工具

        void Js(string script)
        {
            try
            {
                if (Web.CoreWebView2 != null) Web.CoreWebView2.ExecuteScriptAsync(script);
            }
            catch { }
        }

        static string CacheDir
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ComicConverter");
            }
        }

        /// <summary>读取 config.json 里的一个字符串字段（菜单要用输出目录等）。</summary>
        string GetCfgString(string key)
        {
            try
            {
                var cfg = Api.LoadConfig();
                return Core.GetStr(cfg, key, "");
            }
            catch { return ""; }
        }

        void OpenFolder(string dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            {
                MessageBox.Show(this, "目录还不存在：\n" + (dir ?? ""), Brand.Name,
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            OpenPath(dir);
        }

        void OpenPath(string path)
        {
            try { Process.Start("explorer.exe", "\"" + path + "\""); }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, Brand.Name,
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        void OpenUrl(string url)
        {
            try { Process.Start(url); }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, Brand.Name,
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>复制作者邮箱。点击邮箱不打开邮件客户端，只复制到剪贴板。</summary>
        void CopyEmail()
        {
            bool ok = Api.CopyToClipboard(Brand.Email);
            Js("UI.toast(" + JsString(ok ? "已复制：" + Brand.Email : "复制失败") + ")");
            if (!ok)
                MessageBox.Show(this, "无法写入剪贴板，请手动复制：\n" + Brand.Email, Brand.Name,
                    MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        void ClearCache()
        {
            var f = Path.Combine(CacheDir, "origin-cache.json");
            if (!File.Exists(f))
            {
                MessageBox.Show(this, "还没有阅读方向缓存。", Brand.Name,
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var r = MessageBox.Show(this,
                "清除后，下次判断阅读方向需要重新联网查证。\n\n确定要清除吗？",
                Brand.Name, MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;

            try
            {
                File.Delete(f);
                MessageBox.Show(this, "已清除阅读方向缓存。", Brand.Name,
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, Brand.Name,
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        void ResetConfig()
        {
            var r = MessageBox.Show(this,
                "将把界面设置恢复为默认值，并立即生效。\n\n确定要恢复吗？",
                Brand.Name, MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;

            try { Api.SaveConfig(Api.DefaultConfig()); } catch { }
            SetCheck("mode", "convert");
            SetCheck("reading", "auto");
            SetCheck("target", "both");
            Js("UI.reload()");
        }

        void ShowAbout()
        {
            var dlg = new AboutDialog { Owner = this };
            dlg.ShowDialog();
        }

        static string JsString(string s)
        {
            return "'" + (s ?? "").Replace("\\", "\\\\").Replace("'", "\\'").Replace("\r", "").Replace("\n", "\\n") + "'";
        }

        // ------------------------------------------------------------ 窗口外观

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;

                int round = 2;                       // DWMWA_WINDOW_CORNER_PREFERENCE: ROUND
                DwmSetWindowAttribute(hwnd, 33, ref round, 4);

                int border = Rgb(0xE8, 0xE8, 0xE8);  // DWMWA_BORDER_COLOR
                DwmSetWindowAttribute(hwnd, 34, ref border, 4);

                int caption = Rgb(0xF3, 0xF3, 0xF3); // DWMWA_CAPTION_COLOR
                DwmSetWindowAttribute(hwnd, 35, ref caption, 4);

                int text = Rgb(0x1B, 0x1B, 0x1B);    // DWMWA_TEXT_COLOR
                DwmSetWindowAttribute(hwnd, 36, ref text, 4);
            }
            catch { }
        }

        /// <summary>Win11 的 DWM 用 COLORREF（0x00BBGGRR），不是 RGB。</summary>
        private static int Rgb(int r, int g, int b)
        {
            return (b << 16) | (g << 8) | r;
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            Api.ConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");

            if (!Api.Start())
            {
                TxtSplash.Text = "本地服务启动失败（端口 8765-8804 全被占用）。\n请关闭占用端口的程序后重试。";
                return;
            }

            try
            {
                var udf = Path.Combine(CacheDir, "WebView2");
                Directory.CreateDirectory(udf);

                var env = await CoreWebView2Environment.CreateAsync(null, udf);
                await Web.EnsureCoreWebView2Async(env);

                Web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                Web.CoreWebView2.Settings.IsStatusBarEnabled = false;
                Web.CoreWebView2.Settings.AreDevToolsEnabled = false;
                Web.CoreWebView2.Settings.IsZoomControlEnabled = false;

                // 菜单勾选状态跟随界面回填，避免菜单与面板不一致
                Web.CoreWebView2.WebMessageReceived += OnWebMessage;

                Web.NavigationCompleted += delegate
                {
                    Splash.Visibility = Visibility.Collapsed;
                    SyncMenuFromPage();
                };
                Web.Source = new Uri("http://127.0.0.1:" + Api.Port);
            }
            catch (WebView2RuntimeNotFoundException)
            {
                ShowRuntimeHelp();
            }
            catch (Exception ex)
            {
                if (ex is DllNotFoundException || ex is TypeInitializationException)
                    ShowRuntimeHelp();
                else
                    TxtSplash.Text = "WebView2 初始化失败：" + ex.Message;
            }
        }

        /// <summary>页面把界面上的模式/方向/目标回传，菜单勾号跟着走。</summary>
        void OnWebMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                var msg = e.TryGetWebMessageAsString();
                if (string.IsNullOrEmpty(msg)) return;

                var parsed = MiniJson.Parse(msg) as Dictionary<string, object>;
                if (parsed == null) return;

                if (parsed.ContainsKey("mode")) SetCheck("mode", Convert.ToString(parsed["mode"]));
                if (parsed.ContainsKey("reading")) SetCheck("reading", Convert.ToString(parsed["reading"]));
                if (parsed.ContainsKey("target")) SetCheck("target", Convert.ToString(parsed["target"]));
                if (parsed.ContainsKey("saved")) StSaved.Text = Convert.ToString(parsed["saved"]);
                if (parsed.ContainsKey("phase")) StPhase.Text = Convert.ToString(parsed["phase"]);
                if (parsed.ContainsKey("count")) StCount.Text = Convert.ToString(parsed["count"]);
                if (parsed.ContainsKey("current")) StCurrent.Text = Convert.ToString(parsed["current"]);
                if (parsed.ContainsKey("targetText")) StTarget.Text = "目标 " + parsed["targetText"];
                if (parsed.ContainsKey("readingText")) StReading.Text = "方向 " + parsed["readingText"];
                if (parsed.ContainsKey("jobs")) StJobs.Text = "并发 " + parsed["jobs"];
            }
            catch { }
        }

        /// <summary>启动后主动问一次页面当前状态，把菜单与状态栏对齐。</summary>
        void SyncMenuFromPage()
        {
            Js("UI.report()");
        }

        /// <summary>Win10 没装 WebView2 Runtime 时的提示，给出下载入口而不是干等。</summary>
        void ShowRuntimeHelp()
        {
            TxtSplash.Text = "缺少 WebView2 运行时\n\n程序界面由 WebView2 渲染，需要先安装它。\n"
                           + "Win11 已自带；Win10 可从微软官网下载 “Evergreen Bootstrapper”。";

            var btn = new System.Windows.Controls.Button
            {
                Content = "打不开？点这里下载 WebView2 运行时",
                Margin = new Thickness(0, 14, 0, 0),
                Padding = new Thickness(14, 6, 14, 6),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            // 品牌色一律走资源字典（Accent 派生），代码里不写色值——规范 §三
            btn.SetResourceReference(System.Windows.Controls.Button.BackgroundProperty, "CardBrush");
            btn.SetResourceReference(System.Windows.Controls.Button.BorderBrushProperty, "AccentBrush");
            btn.SetResourceReference(System.Windows.Controls.Button.ForegroundProperty, "AccentBrush");
            btn.Click += delegate
            {
                OpenUrl("https://developer.microsoft.com/microsoft-edge/webview2/");
            };

            if (Splash.Child is System.Windows.Controls.StackPanel sp) sp.Children.Add(btn);
        }
    }
}