using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace ComicConverter
{
    /// <summary>
    /// 原生标题栏的 DWM 定制（规范 §四）：主窗口、关于框、自绘对话框共用同一套。
    /// 对话框不另立样式 —— 主窗口是原生标题栏，对话框也必须原生、同色，
    /// 否则同一个程序里会同时出现「白标题栏」和「系统灰标题栏」两种观感（§七.2）。
    /// Win10 1809 之前不支持这些属性，失败就退回系统默认，不抛异常。
    ///
    /// 另承担「窗口该多大」的两个测量原语（规范 §10.4：宽度必须运行时量）：
    ///   · NaturalWidth / NaturalWidthLines —— 内容里最长的一行（不折行）有多宽
    ///   · HorizontalInset                  —— 窗口边框水平方向吃掉多少（DIP）
    /// 有了这两个，对话框就能「按内容反推窗口宽」，而不是把某个数抄进 XAML。
    /// </summary>
    public static class WindowChrome
    {
        public static void Apply(Window w)
        {
            if (w == null) return;
            try
            {
                var hwnd = new WindowInteropHelper(w).Handle;
                if (hwnd == IntPtr.Zero) return;

                int round = 2;                       // DWMWA_WINDOW_CORNER_PREFERENCE: ROUND
                DwmSetWindowAttribute(hwnd, 33, ref round, 4);

                int border = Rgb(0xE8, 0xE8, 0xE8);  // DWMWA_BORDER_COLOR
                DwmSetWindowAttribute(hwnd, 34, ref border, 4);

                int caption = Rgb(0xF3, 0xF3, 0xF3); // DWMWA_CAPTION_COLOR = 内容区底色
                DwmSetWindowAttribute(hwnd, 35, ref caption, 4);

                int text = Rgb(0x1B, 0x1B, 0x1B);    // DWMWA_TEXT_COLOR
                DwmSetWindowAttribute(hwnd, 36, ref text, 4);
            }
            catch { }
        }

        /// <summary>Win11 的 DWM 用 COLORREF（0x00BBGGRR），不是 RGB。</summary>
        static int Rgb(int r, int g, int b)
        {
            return (b << 16) | (g << 8) | r;
        }

        // ------------------------------------------------------------ 尺寸测量

        /// <summary>
        /// 一行文本「不折行」时的自然宽（DIP）。这就是规范 §七.2 说的
        /// 「最长的那一行不可断内容」的量法；正文能不能排得开由调用方拿这个数去比。
        /// </summary>
        public static double NaturalWidth(string line, Typeface face, double size, double pixelsPerDip)
        {
            if (string.IsNullOrEmpty(line)) return 0;
            var ft = new FormattedText(line, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                                       face, size, Brushes.Black, pixelsPerDip);
            return ft.WidthIncludingTrailingWhitespace;
        }

        /// <summary>
        /// 多行文本取最长那一行。硬换行的行各算各的 —— 把几行拼成一段再量会偏大
        /// （快捷键块那种「标题 + 若干行」的段尤其明显）。
        /// </summary>
        public static double NaturalWidthLines(string text, Typeface face, double size, double pixelsPerDip)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            double max = 0;
            foreach (var line in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                double w = NaturalWidth(line.Trim(), face, size, pixelsPerDip);
                if (w > max) max = w;
            }
            return max;
        }

        /// <summary>
        /// 窗口边框（非客户区）水平方向吃掉多少 DIP。
        /// 必须在 OnSourceInitialized 之后调 —— 那时才有 HWND，能拿 GetWindowRect 减 GetClientRect。
        /// 不写死这个数：它随 DPI 和系统版本变，量出来的才准。
        /// </summary>
        public static double HorizontalInset(Window w)
        {
            if (w == null) return 0;
            try
            {
                var hwnd = new WindowInteropHelper(w).Handle;
                if (hwnd == IntPtr.Zero) return 0;

                RECT wr, cr;
                if (!GetWindowRect(hwnd, out wr) || !GetClientRect(hwnd, out cr)) return 0;

                double px = (wr.Right - wr.Left) - (cr.Right - cr.Left);
                double scale = VisualTreeHelper.GetDpi(w).DpiScaleX;
                return scale > 0 ? px / scale : px;
            }
            catch { return 0; }
        }

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")]
        static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

        [DllImport("user32.dll")]
        static extern bool GetClientRect(IntPtr hwnd, out RECT rect);

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);
    }
}
