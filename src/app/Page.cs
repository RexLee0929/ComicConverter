using System.IO;
using System.Reflection;
using System.Text;

namespace ComicConverter
{
    /// <summary>内嵌的界面 HTML。</summary>
    public static class Page
    {
        static string _html;

        public static string Html
        {
            get
            {
                if (_html != null) return _html;
                try
                {
                    var asm = Assembly.GetExecutingAssembly();
                    using (var s = asm.GetManifestResourceStream("ComicConverter.Web.index.html"))
                    {
                        if (s == null) return "<h1>界面资源缺失</h1>";
                        using (var r = new StreamReader(s, Encoding.UTF8))
                            _html = r.ReadToEnd();
                    }
                    // 网页里的品牌色也由图标派生（Accent.Init 在窗口出现前已算好）
                    _html = _html
                        .Replace("#0F6E56", Accent.BrandHex)
                        .Replace("#0B5A46", Accent.BrandDarkHex)
                        .Replace("#E6F2EE", Accent.SoftHex);
                }
                catch
                {
                    _html = "<h1>界面加载失败</h1>";
                }
                return _html;
            }
        }
    }
}
