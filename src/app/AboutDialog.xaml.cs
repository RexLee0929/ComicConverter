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

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            WindowChrome.Apply(this);   // 关于框也是主窗口的一层，标题栏必须同色（§七.2）
        }

        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);
            FitToContent();
        }
    }
}
