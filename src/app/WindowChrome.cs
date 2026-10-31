using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ComicConverter
{
    /// <summary>
    /// 原生标题栏的 DWM 定制（规范 §四）：主窗口、关于框、自绘对话框共用同一套。
    /// 对话框不另立样式 —— 主窗口是原生标题栏，对话框也必须原生、同色，
    /// 否则同一个程序里会同时出现「白标题栏」和「系统灰标题栏」两种观感（§七.2）。
    /// Win10 1809 之前不支持这些属性，失败就退回系统默认，不抛异常。
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

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);
    }
}
