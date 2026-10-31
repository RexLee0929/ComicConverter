using System;
using System.Windows;
using System.Windows.Media.Imaging;

namespace ComicConverter
{
    /// <summary>
    /// 从 app.ico 里取「最大的那一帧」当品牌图标。
    /// WPF 的 ICO 解码器会把帧序重排（本机实测：48, 64, 32, 24, 128, 16, 256），
    /// 直接写 Source="app.ico" 拿到的永远是首帧 48px —— 放到 60px 的启动位、
    /// 150% DPI 的标题栏上就会被放大发虚。这里用 BitmapDecoder 遍历全部帧、
    /// 按 PixelWidth 挑最大的，启动画面和窗口/任务栏图标都走这一条路。
    /// </summary>
    public static class BrandIcon
    {
        static BitmapSource _source;

        /// <summary>取帧失败时的原因（诊断用；成功后为 null）。</summary>
        public static string LastError { get; private set; }

        /// <summary>品牌图标最大帧（已 Freeze，可跨线程用）；取不到为 null。</summary>
        public static BitmapSource Source
        {
            get { if (_source == null) _source = LoadLargest(); return _source; }
        }

        static BitmapSource LoadLargest()
        {
            LastError = null;
            try
            {
                // 显式写 component URI：相对的 pack://application:,,,/app.ico
                // 按入口程序集解析，在 ui_render 这类外壳工具里会找不到资源
                var res = Application.GetResourceStream(
                    new Uri("pack://application:,,,/ComicConverter;component/app.ico"));
                if (res == null) { LastError = "GetResourceStream 返回 null"; return null; }
                using (res.Stream)
                {
                    var dec = BitmapDecoder.Create(res.Stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                    BitmapFrame best = null;
                    foreach (BitmapFrame f in dec.Frames)
                        if (best == null || f.PixelWidth > best.PixelWidth) best = f;
                    if (best != null) best.Freeze();
                    else LastError = "解码器没有给出任何帧";
                    return best;
                }
            }
            catch (Exception ex)
            {
                LastError = ex.GetType().Name + ": " + ex.Message;
                return null;
            }
        }
    }
}
