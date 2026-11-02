using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace ComicConverter
{
    /// <summary>
    /// 「帮助 → 关于」。三段式布局（内容框 / 联系条 / 按钮栏），与 txtFormat 同构：
    /// 内容框是可选中的富文本（对应 QTextBrowser），宽度固定 640、高度随内容撑开，
    /// 因此默认呈现「宽比高长」的横向形状，并设有最小尺寸。
    /// 联系方式只有一处入口：点邮箱胶囊即复制——胶囊变色 + 右侧短暂出现「已复制到剪贴板」。
    /// </summary>
    public partial class AboutDialog : Window
    {
        readonly DispatcherTimer _reset;

        /// <summary>
        /// 宽度区间（规范 §七.2）：下限 320 免得短内容挤成一条缝，上限 640 是正文阅读行宽
        /// —— 超过就换行，不把窗口撑宽。区间内的落点由 MeasureWidth() 按内容算出来。
        /// </summary>
        const double MinContentWidth = 320;
        const double MaxContentWidth = 640;

        public string Author { get { return Brand.Author; } }
        public string Email { get { return Brand.Email; } }

        public AboutDialog()
        {
            InitializeComponent();
            DataContext = this;

            EmailIcon.Content = Icons.Get("mail", FindBrush("MutedBrush"), 13);
            BuildDocument();

            // 复制反馈出现 1.6 秒后自动移除
            _reset = new DispatcherTimer { Interval = System.TimeSpan.FromSeconds(1.6) };
            _reset.Tick += delegate
            {
                _reset.Stop();
                Chip(false);
                CopyHint.Text = "";
            };

        }

        // ------------------------------------------------------------ 内容

        /// <summary>关于文案在 src/app/ABOUT.md 里维护；这里只做占位符替换与渲染。</summary>
        void BuildDocument()
        {
            string md = ReadAbout()
                .Replace("{NAME}", Brand.Name)
                .Replace("{VERSION}", Brand.Version)
                .Replace("{AUTHOR}", Brand.Author)
                .Replace("{TAGLINE}", Brand.Tagline);

            Doc.Document = AboutDoc.Parse(md,
                FindBrush("AccentBrush"), FindBrush("InkBrush"),
                FindBrush("MutedBrush"), FindBrush("DimBrush"));

            // RichTextBox 会在文档挂上去之后的布局里把 PagePadding 复位成模板值 5,0,5,0，
            // 正文于是整体右移 5px、和卡片的左内边距对不齐。光在赋值后设一次不管用
            // （赋值时文档还没上屏），必须在 Loaded 之后补压一次，之后每次自适应高度也顺手压一次。
            Doc.Loaded += delegate { ZeroPadding(); };
            ZeroPadding();
        }

        /// <summary>把富文本的页内边距压成 0，缩进全部由卡片内边距和段落缩进决定。</summary>
        void ZeroPadding()
        {
            if (Doc.Document != null) Doc.Document.PagePadding = new Thickness(0);
        }

        static string _aboutMd;

        static string ReadAbout()
        {
            if (_aboutMd != null) return _aboutMd;
            var asm = typeof(AboutDialog).Assembly;
            using (var s = asm.GetManifestResourceStream("ComicConverter.ABOUT.md"))
            {
                if (s == null) return _aboutMd = Brand.Name + " " + Brand.Version;
                using (var r = new System.IO.StreamReader(s, System.Text.Encoding.UTF8))
                    return _aboutMd = r.ReadToEnd();
            }
        }

        // ------------------------------------------------------------ 复制反馈

        static Brush FindBrush(string key)
        {
            return Application.Current.TryFindResource(key) as Brush ?? Brushes.DimGray;
        }

        void Chip(bool active)
        {
            EmailChip.Background = active ? FindBrush("AccentSoftBrush") : FindBrush("CardBrush");
            EmailChip.BorderBrush = active ? FindBrush("AccentBrush") : FindBrush("BorderBrush");
            EmailText.Foreground = active ? FindBrush("AccentBrush") : FindBrush("InkBrush");
        }

        void Email_Click(object sender, MouseButtonEventArgs e)
        {
            if (!Api.CopyToClipboard(Brand.Email))
            {
                CopyHint.Foreground = FindBrush("DimBrush");
                CopyHint.Text = "复制失败，请手动选中邮箱文字复制";
                _reset.Stop();
                _reset.Start();
                return;
            }

            // 邮箱文字保持不变，只让小框变色；反馈文字出现在右侧，随后自动移除
            Chip(true);
            CopyHint.Foreground = FindBrush("AccentBrush");
            CopyHint.Text = "已复制到剪贴板";
            _reset.Stop();
            _reset.Start();
        }

        void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        // ------------------------------------------------------------ 高度自适应

        int _fitPasses;

        /// <summary>
        /// 按富文本真实内容高度把窗口校正到刚好放下（上下两个方向都能调）。
        /// 不用 SizeToContent——WPF 对 RichTextBox 量出来的高度偏小，会把
        /// 联系条与按钮栏挤出窗口；这里在渲染完成后读 ExtentHeight / ViewportHeight
        /// 自己算，内容增删后不需要改任何写死的尺寸。
        /// </summary>
        void FitToContent()
        {
            ZeroPadding();      // 布局过程会把它复位，每轮都补一次

            double delta = Doc.ExtentHeight - Doc.ViewportHeight;
            if (Math.Abs(delta) > 0.5)
                Height = Math.Ceiling(ActualHeight + delta);

            // 再校一遍：第一遍调整后布局会有一次回落，Background 优先级跑完才算稳定
            if (++_fitPasses < 3)
                Dispatcher.BeginInvoke(new Action(FitToContent), DispatcherPriority.Background);
        }

        /// <summary>
        /// 按内容量出窗口宽（规范 §10.4：宽度必须运行时量，不许写死常量）。
        /// 三步：① 逐行量富文本里最长那一行的自然宽；② 加上卡片外边距/边距与窗口边框
        /// （这些数从控件自身属性读，C# 里不抄任何尺寸）；③ 夹到 320~640 —— 640 是阅读行宽
        /// 上限，正文超了就换行，不把窗口撑宽（§七.2）。改 ABOUT.md 的文案，宽度自动跟着变。
        /// 调用时机必须是 OnSourceInitialized：那时已有 HWND 能量出边框，又还没绘制，不会看到跳变。
        /// </summary>
        void MeasureWidth()
        {
            double natural = NaturalWidthOfDocument();
            if (natural <= 0) return;              // 量不出来就保持初始宽，不要缩成一条缝

            double chrome = DocCard.Margin.Left + DocCard.Margin.Right
                          + DocCard.BorderThickness.Left + DocCard.BorderThickness.Right
                          + DocCard.Padding.Left + DocCard.Padding.Right
                          + WindowChrome.HorizontalInset(this);

            Width = Math.Min(MaxContentWidth, Math.Max(MinContentWidth, Math.Ceiling(natural + chrome)));

            // 宽度是刚刚才定下来的，WPF 自己那次居中用的是改宽之前的尺寸 —— 自己重摆一次水平位置，
            // 否则框会偏在一边（竖直方向由启动位置决定，高度稍后由 FitToContent 定）。
            if (WindowStartupLocation == WindowStartupLocation.CenterOwner && Owner != null && Owner.IsVisible)
                Left = Owner.Left + (Owner.ActualWidth - Width) / 2;
            else
            {
                var wa = SystemParameters.WorkArea;
                Left = wa.Left + (wa.Width - Width) / 2;
            }
        }

        /// <summary>富文本里最长的一行（按段取，段内再按硬换行拆行——整段拼起来量会偏大）。</summary>
        double NaturalWidthOfDocument()
        {
            if (Doc == null || Doc.Document == null) return 0;

            var flow = Doc.Document;
            var family = Doc.FontFamily ?? new FontFamily("Microsoft YaHei UI");
            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            double max = 0;

            foreach (var blk in flow.Blocks)
            {
                var p = blk as Paragraph;
                if (p == null) continue;

                double size = p.FontSize;
                if (double.IsNaN(size) || size <= 0) size = flow.FontSize;
                var face = new Typeface(family, FontStyles.Normal, p.FontWeight, FontStretches.Normal);

                string text = new TextRange(p.ContentStart, p.ContentEnd).Text;
                double w = WindowChrome.NaturalWidthLines(text, face, size, dpi);
                if (w > max) max = w;
            }
            return max;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            WindowChrome.Apply(this);   // 关于框也是主窗口的一层，标题栏必须同色（§七.2）
            MeasureWidth();             // 先量宽再上屏：宽度由内容决定，不是从 XAML 里读的
        }

        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);
            FitToContent();
        }
    }
}
