using System;
using System.Reflection;

namespace ComicConverter
{
    /// <summary>程序名、版本、作者信息的唯一出口（版本号来自 src/version.props）。</summary>
    public static class Brand
    {
        /// <summary>程序名，只用英文。</summary>
        public const string Name = "ComicConverter";

        /// <summary>一句话说明，仅作副标题，不参与命名。</summary>
        public const string Tagline = "EPUB → CBZ 漫画库整理";

        public const string Author = "Rex Lee";
        public const string Email = "rexlee@rexleepro.com";

        static string _version;

        /// <summary>形如 1.1.0。</summary>
        public static string Version
        {
            get
            {
                if (_version == null)
                {
                    var asm = typeof(Brand).Assembly;

                    var info = (AssemblyInformationalVersionAttribute)Attribute.GetCustomAttribute(
                        asm, typeof(AssemblyInformationalVersionAttribute));
                    if (info != null && !string.IsNullOrEmpty(info.InformationalVersion))
                    {
                        var v = info.InformationalVersion;
                        int plus = v.IndexOf('+');          // 去掉 SourceLink 附带的 build 元数据
                        if (plus > 0) v = v.Substring(0, plus);
                        _version = v.Trim();
                    }

                    if (string.IsNullOrEmpty(_version))
                    {
                        var nv = asm.GetName().Version;
                        _version = nv == null ? "0.0.0" : nv.Major + "." + nv.Minor + "." + nv.Build;
                    }
                }
                return _version;
            }
        }
    }
}
