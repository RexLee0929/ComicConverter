using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;

namespace ComicConverter
{
    public partial class MainWindow : Window
    {
        /// <summary>命令 id → 菜单项。用于回填勾选状态。</summary>
        readonly Dictionary<string, MenuItem> _byCommand = new Dictionary<string, MenuItem>();

        /// <summary>分组 → 当前选中值（mode / reading / target）。</summary>
        readonly Dictionary<string, string> _checked = new Dictionary<string, string>();

        /// <summary>状态栏中段瞬时提示的定时器（1.8 秒自消，规范 §六）。</summary>
        DispatcherTimer _toast;

        public MainWindow()
        {
            InitializeComponent();

            // 标题栏 = 程序名 + 空格 + 版本号（版本唯一来源是 version.props，不写字面量）
            Title = Brand.Name + " " + Brand.Version;
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
        void SetCheck(string group, string value)
        {
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

                // 与左栏三个按钮同一条路，菜单只是快捷入口
                case "preview": Js("UI.preview()"); break;
                case "start": Js("UI.start()"); break;
                case "stop": Js("UI.stop()"); break;

                case "open-out": OpenFolder(GetCfgString("out")); break;
                case "open-appdir": OpenFolder(AppDomain.CurrentDomain.BaseDirectory); break;
                case "open-config-dir": OpenFolder(Path.GetDirectoryName(Api.ConfigPath)); break;
                case "open-cache": OpenFolder(CacheDir); break;
                case "open-log": OpenPath(App.LogPath); break;

                case "clear-log": Js("UI.clearLog()"); break;
                case "clear-preview": Js("UI.clearPreview()"); break;

                // 不可逆操作不弹窗：交给页面底部的内联确认条（规范 §七 级③）
                case "clear-cache": Js("UI.confirm('clear-cache')"); break;
                case "reset-config": Js("UI.confirm('reset-config')"); break;

                case "copy-email": CopyEmail(); break;

                case "reload": Js("UI.reload()"); break;

                case "about": ShowAbout(); break;

                case "webview2":
                    OpenUrl("https://developer.microsoft.com/microsoft-edge/webview2/");
                    break;

                case "exit": Close(); break;
            }
        }

        /// <summary>
        /// 页面替宿主发起的命令（页面内联确认点了「确认」、页面里的快捷键）走这里，
        /// 与菜单命令同一个分发器，避免两套逻辑各写一遍。
        /// </summary>
        void RunCommand(string command)
        {
            if (string.IsNullOrEmpty(command)) return;
            Run(new AppMenu.Item { Text = command, Command = command });
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

        /// <summary>状态栏中段的瞬时提示：成功、已复制、已保存一类「知道了就行」的反馈（§六 级①）。</summary>
        public void Toast(string msg)
        {
            if (StSaved == null) return;

            if (_toast == null)
            {
                _toast = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.8) };
                _toast.Tick += delegate
                {
                    _toast.Stop();
                    StSaved.Foreground = (Brush)FindResource("DimBrush");
                    StSaved.Text = "";
                };
            }

            StSaved.Foreground = (Brush)FindResource("DimBrush");
            StSaved.Text = msg ?? "";
            _toast.Stop();
            _toast.Start();
        }

        /// <summary>
        /// 未捕获异常的出口（规范 §七.4）：写 error.log + 状态栏红字 + 自绘错误框。
        /// 绝不允许「弹个框说完蛋了然后界面啥也没变」，也不静默吞掉。
        /// </summary>
        public void ReportError(string detail)
        {
            App.Dump(detail);

            var danger = (Brush)FindResource("DangerBrush");
            StPhase.Text = "出错";
            StPhase.Foreground = danger;
            Toast("出错：详情已写入 error.log");

            MsgDialog.Error(this, "刚才的操作没有完成",
                FirstLine(detail) + "\n\n错误详情已写入 error.log，程序可以继续使用。",
                App.LogPath);
        }

        /// <summary>取异常消息的第一行、压到一句话长度，正文只留「发生了什么」。</summary>
        static string FirstLine(string s)
        {
            if (string.IsNullOrEmpty(s)) return "程序遇到了未预料的情况。";
            var one = s.Replace("\r", " ").Replace("\n", " ").Trim();
            if (one.Length > 110) one = one.Substring(0, 110) + "…";
            return one;
        }

        void OpenFolder(string dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            {
                Toast("目录还不存在：" + (string.IsNullOrEmpty(dir) ? "未设置" : dir));
                App.Dump("open folder: " + dir);
                return;
            }
            OpenPath(dir);
        }

        void OpenPath(string path)
        {
            try { Process.Start("explorer.exe", "\"" + path + "\""); }
            catch (Exception ex) { ReportError("打开 " + path + " 失败：" + ex.Message); }
        }

        void OpenUrl(string url)
        {
            try { Process.Start(url); }
            catch (Exception ex) { ReportError("打开网页失败：" + url + " — " + ex.Message); }
        }

        /// <summary>复制作者邮箱。点击邮箱不打开邮件客户端，只复制到剪贴板。</summary>
        void CopyEmail()
        {
            if (Api.CopyToClipboard(Brand.Email))
            {
                Toast("已复制：" + Brand.Email);
                return;
            }
            Toast("剪贴板不可用，请手动复制：" + Brand.Email);
            App.Dump("clipboard unavailable: " + Brand.Email);
        }

        /// <summary>清缓存：删除动作已经在页面里确认过，这里只做事 + 状态栏反馈。</summary>
        void ClearCache()
        {
            var f = Path.Combine(CacheDir, "origin-cache.json");
            if (!File.Exists(f)) { Toast("还没有阅读方向缓存"); return; }

            try
            {
                File.Delete(f);
                Toast("已清除阅读方向缓存");     // 成功提示不弹窗（§七.1 禁止「点确定后又弹一个」）
            }
            catch (Exception ex)
            {
                ReportError("清除缓存失败：" + ex.Message);
            }
        }

        void ResetConfig()
        {
            try { Api.SaveConfig(Api.DefaultConfig()); }
            catch (Exception ex) { ReportError("恢复默认设置失败：" + ex.Message); return; }

            SetCheck("mode", "convert");
            SetCheck("reading", "auto");
            SetCheck("target", "both");
            Js("UI.reload()");
            Toast("已恢复默认设置");
        }

        void ShowAbout()
        {
            var dlg = new AboutDialog { Owner = this };
            dlg.ShowDialog();
        }

        // ------------------------------------------------------------ 窗口外观

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            WindowChrome.Apply(this);
        }

        async void OnLoaded(object sender, RoutedEventArgs e)
        {
            Api.ConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");

            if (!Api.Start())
            {
                TxtSplash.Text = "本地服务启动失败（端口 8765-8804 全被占用）。\n请关闭占用端口的程序后重试。";
                return;
            }

            // 首次启动就落盘一份默认配置：安装器/卸载器靠安装目录里有没有 config.json
            // 决定要不要显示「覆盖现有配置」「保留配置文件」。只有改设置才写文件的话，
            // 从没动过设置的用户重装时永远看不到这些选项，配置的保留/覆盖也就无从谈起。
            if (!File.Exists(Api.ConfigPath))
            {
                try { Api.SaveConfig(Api.DefaultConfig()); } catch { }
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

                // 页面里的 alert/confirm 一律不弹浏览器原生框：整页已经不可用时才由宿主开自绘框（§七.4）
                Web.CoreWebView2.ScriptDialogOpening += OnScriptDialog;

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

        /// <summary>页面里遗留的 alert / confirm：取消原生框，改走宿主的错误框或状态栏。</summary>
        void OnScriptDialog(object sender, CoreWebView2ScriptDialogOpeningEventArgs e)
        {
            try
            {
                e.Accept();          // confirm 一律当「确认」（页面已改用内联确认条，这里只是兜底）
                if (e.Kind == CoreWebView2ScriptDialogKind.Alert)
                    Toast(e.Message);
            }
            catch { }
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

                // 页面发起的动作：内联确认后的清除/恢复、页面快捷键
                if (parsed.ContainsKey("act"))
                {
                    var act = Convert.ToString(parsed["act"]);
                    if (act == "clear-cache") ClearCache();
                    else if (act == "reset-config") ResetConfig();
                    else RunCommand(act);
                    return;
                }

                if (parsed.ContainsKey("mode")) SetCheck("mode", Convert.ToString(parsed["mode"]));
                if (parsed.ContainsKey("reading")) SetCheck("reading", Convert.ToString(parsed["reading"]));
                if (parsed.ContainsKey("target")) SetCheck("target", Convert.ToString(parsed["target"]));
                if (parsed.ContainsKey("saved")) StSaved.Text = Convert.ToString(parsed["saved"]);
                if (parsed.ContainsKey("phase"))
                {
                    StPhase.Text = Convert.ToString(parsed["phase"]);
                    StPhase.Foreground = (Brush)FindResource("InkBrush");   // 恢复正常态，撤掉出错红
                }
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
