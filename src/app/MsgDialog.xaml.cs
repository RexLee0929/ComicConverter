using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace ComicConverter
{
    /// <summary>
    /// 程序的自绘错误框 —— 「能不弹就不弹」阶梯里剩下的那一档（规范 §七）：
    /// 未捕获异常这类「必须让用户知道、且内容区已经表达不了」的场合才用。
    ///
    /// 其余反馈一律不弹窗：
    ///   · 成功 / 已复制 / 已保存 → 状态栏或页面提示条（级①）
    ///   · 目录不存在 / 打不开 → 页面提示条（级①）+ error.log
    ///   · 清除缓存 / 恢复默认这类不可逆操作 → 页面底部内联确认条（级③，见 Web/index.html）
    /// 所以这里只保留一个错误入口，没有「确定/取消」式的通用询问框。
    /// </summary>
    public partial class MsgDialog : Window
    {
        /// <summary>同屏只允许一个：异常连发时不能叠出第二个框（§七.1「弹窗叠弹窗」）。</summary>
        static bool _showing;

        readonly string _logPath;

        public MsgDialog(Window owner, string title, string body, string logPath)
        {
            InitializeComponent();

            _logPath = logPath;
            if (owner != null && owner.IsVisible) Owner = owner;
            else WindowStartupLocation = WindowStartupLocation.CenterScreen;

            // 标题栏只写程序名（与关于框同构），卡片内标题才说这件事本身
            Title = Brand.Name;
            TitleText.Text = string.IsNullOrEmpty(title) ? "操作没有完成" : title;
            BodyText.Text = body ?? "";
            IconHost.Content = Icons.Get("alert", Brush("DangerBrush"), 24);

            // 没有日志路径时只剩一个「关闭」：回车与 Esc 都落到它上面
            bool hasLog = !string.IsNullOrEmpty(logPath);
            BtnMain.Visibility = hasLog ? Visibility.Visible : Visibility.Collapsed;
            if (!hasLog)
            {
                BtnMain.IsDefault = false;
                BtnClose.Content = "关闭";
                BtnClose.IsDefault = true;
            }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            WindowChrome.Apply(this);      // 标题栏跟主窗口同一套，别一大一小两种标题
        }

        static Brush Brush(string key)
        {
            return Application.Current.TryFindResource(key) as Brush ?? Brushes.Red;
        }

        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);
            FitToContent(0);
        }

        /// <summary>
        /// 宽固定、高按内容拟合（规范 §七.2，禁用 SizeToContent）。
        /// 高度 = 非客户区 + 卡片外边距/内边距 + 标题 + 正文真实高度 + 按钮行。
        /// 正文超过 BodyScroll 的 MaxHeight 时按上限算，正文改在框内滚动，窗口不会顶到屏幕外。
        /// 量两轮：出现滚动条会改变可用宽度，正文要重新折行。
        /// </summary>
        void FitToContent(int pass)
        {
            double nonClient = ActualHeight
                             - (Card.ActualHeight + Card.Margin.Top + Card.Margin.Bottom);

            double body = Math.Min(BodyScroll.ExtentHeight, BodyScroll.MaxHeight);

            double need = nonClient
                        + Card.Margin.Top + Card.Margin.Bottom
                        + Card.Padding.Top + Card.Padding.Bottom
                        + TitleText.Margin.Top + TitleText.ActualHeight + TitleText.Margin.Bottom
                        + body
                        + ButtonRow.Margin.Top + ButtonRow.ActualHeight;

            double cap = SystemParameters.WorkArea.Height - 80;
            Height = Math.Max(MinHeight, Math.Min(cap, Math.Ceiling(need)));

            if (pass + 1 < 2)
                Dispatcher.BeginInvoke(new Action(delegate { FitToContent(pass + 1); }),
                                       DispatcherPriority.Background);
        }

        void Main_Click(object sender, RoutedEventArgs e)
        {
            OpenLog();
            Close();
        }

        void Alt_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        void OpenLog()
        {
            try
            {
                if (!string.IsNullOrEmpty(_logPath)) Process.Start("explorer.exe", "\"" + _logPath + "\"");
            }
            catch { }
        }

        // ------------------------------------------------------------ 对外

        /// <summary>
        /// 报一个「用户必须知道」的错误：带错误详情与「打开日志」入口。
        /// 已有框在屏幕上时直接返回 —— 后来者只进 error.log，不叠窗。
        /// </summary>
        public static void Error(Window owner, string title, string body, string logPath)
        {
            if (_showing) return;

            _showing = true;
            try
            {
                new MsgDialog(owner, title, body, logPath).ShowDialog();
            }
            finally { _showing = false; }
        }
    }
}
