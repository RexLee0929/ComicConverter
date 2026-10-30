using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ComicConverter
{
    /// <summary>已有 CBZ 在 Kavita / Komga 命名规则间互转，图片字节不动。</summary>
    public static class Repack
    {
        public static string RepackOne(string path, Dictionary<string, object> cfg)
        {
            string infoXml = null;
            var names = new List<string>();

            using (var src = ZipFile.OpenRead(path))
            {
                foreach (var e in src.Entries) names.Add(e.FullName);
                var ci = src.Entries.FirstOrDefault(e =>
                    Path.GetFileName(e.FullName).Equals("ComicInfo.xml", StringComparison.OrdinalIgnoreCase));
                if (ci == null) return "无 ComicInfo.xml";
                infoXml = Core.ReadText(ci);
            }

            var mSeries = Regex.Match(infoXml, "<Series>(.*?)</Series>", RegexOptions.Singleline);
            var mVol = Regex.Match(infoXml, "<Volume>(.*?)</Volume>", RegexOptions.Singleline);
            var series = mSeries.Success ? Unescape(mSeries.Groups[1].Value).Trim() : Path.GetFileName(Path.GetDirectoryName(path));
            int vol = 1;
            if (mVol.Success)
            {
                double d;
                if (double.TryParse(mVol.Groups[1].Value.Trim(), out d)) vol = (int)d;
            }

            var tpl = Core.GetStr(cfg, "target", "both") == "komga"
                ? Core.GetStr(cfg, "name_template_komga", "{series} #{vol3}")
                : Core.GetStr(cfg, "name_template", "{series} v{vol:02d}");

            var core = tpl.Replace("{series}", series)
                          .Replace("{vol:02d}", vol.ToString("D2"))
                          .Replace("{vol3}", vol.ToString("D3"))
                          .Replace("{vol}", vol.ToString())
                          .Replace("{num}", vol.ToString());
            core = Core.SafeName(core, 110);

            var outRoot = Core.GetStr(cfg, "out", "");
            if (outRoot.Length == 0) outRoot = Path.GetDirectoryName(path);
            var dst = Path.Combine(outRoot, Core.SafeName(series, 120), core + ".cbz");

            if (string.Equals(Path.GetFullPath(dst), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
                return "same";

            Directory.CreateDirectory(Path.GetDirectoryName(dst));

            var newInfo = Regex.Replace(infoXml, "<Number>.*?</Number>", "<Number>" + vol + "</Number>", RegexOptions.Singleline);
            if (!newInfo.Contains("<Number>"))
                newInfo = newInfo.Replace("</ComicInfo>", "  <Number>" + vol + "</Number>\n</ComicInfo>");

            var level = Core.GetStr(cfg, "compress", "store") == "deflate"
                ? CompressionLevel.Optimal : CompressionLevel.NoCompression;

            using (var src = ZipFile.OpenRead(path))
            using (var fs = File.Create(dst))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                foreach (var e in src.Entries)
                {
                    if (e.FullName.EndsWith("/")) continue;
                    if (Path.GetFileName(e.FullName).Equals("ComicInfo.xml", StringComparison.OrdinalIgnoreCase)) continue;
                    var ne = zip.CreateEntry(e.FullName, level);
                    using (var os = ne.Open())
                    using (var ss = e.Open())
                        ss.CopyTo(os);
                }
                var ci = zip.CreateEntry("ComicInfo.xml", CompressionLevel.Optimal);
                using (var os = ci.Open())
                {
                    var bytes = new UTF8Encoding(false).GetBytes(newInfo);
                    os.Write(bytes, 0, bytes.Length);
                }
            }
            return null;
        }

        static string Unescape(string s)
        {
            return s.Replace("&lt;", "<").Replace("&gt;", ">").Replace("&quot;", "\"").Replace("&amp;", "&");
        }
    }
}
