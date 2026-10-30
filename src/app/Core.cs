using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ComicConverter
{
    public class Item
    {
        public string Src;
        public string Series = "";
        public int? Vol;
        public string Label = "";
        public bool IsSpecial;
        public int Pages;
        public string Title = "";
        public Dictionary<string, string> Meta = new Dictionary<string, string>();

        /// <summary>页面 高/宽 的中位数，用来识别长条页（条漫）。0 表示没量到。</summary>
        public double TallRatio;

        /// <summary>始发国家/地区：JP / KR / CN / TW / HK，判不出为 null。</summary>
        public string Country;
        public string CountrySource = "none";

        /// <summary>rtl / ltr / ttb。</summary>
        public string Reading = "rtl";
        public string ReadingSource = "default";
    }

    public class ConvertResult
    {
        public string Dst;
        public string Series;
        public int Vol;
        public int Pages;
        public double Mb;
        public bool Skip;
        public string Err;
    }

    public static class Core
    {
        static readonly string[] ImgExt = { ".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp" };
        static readonly HashSet<string> Junk = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "createby.png", "createby.jpg", "createby.jpeg", "logo.png" };

        static readonly Regex HalfBad = new Regex("[<>:\"/\\\\|?*\\x00-\\x1f]", RegexOptions.Compiled);
        static readonly Regex SpecialRe = new Regex("番外|外傳|外传|特別篇|特别篇|短篇", RegexOptions.Compiled);
        static readonly Regex Spaces = new Regex("\\s+", RegexOptions.Compiled);

        static readonly string[] VolPatterns =
        {
            @"第\s*0*(\d{1,4})\s*[卷话話集]",
            @"[卷]\s*0*(\d{1,4})",
            @"\bv(?:ol)?\.?\s*0*(\d{1,4})\b",
            @"#\s*0*(\d{1,4})"
        };

        static readonly Dictionary<char, char> FullBad = new Dictionary<char, char>
        {
            {'：','_'},{'？','_'},{'＊','_'},{'／','_'},{'＼','_'},
            {'＜','_'},{'＞','_'},{'｜','_'},{'＂','_'},{'﹡','_'},
            {'　',' '}
        };

        // ---------------------------------------------------------- 文本
        public static string SafeName(string s, int maxLen)
        {
            if (string.IsNullOrEmpty(s)) return "Unknown";
            var sb = new StringBuilder(s.Length);
            foreach (var c in s) sb.Append(FullBad.ContainsKey(c) ? FullBad[c] : c);
            var t = HalfBad.Replace(sb.ToString(), "_");
            t = Spaces.Replace(t, " ").Trim(' ', '.', '-', '_');
            if (t.Length == 0) return "Unknown";
            return t.Length > maxLen ? t.Substring(0, maxLen) : t;
        }

        static readonly Regex JapanKana = new Regex("[\u3041-\u3096\u30a1-\u30fa\u30fc-\u30ff]", RegexOptions.Compiled);

        // ---------------------------------------------------------- 命名解析
        public static int? FindVolume(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            foreach (var p in VolPatterns)
            {
                var m = Regex.Match(text, p, RegexOptions.IgnoreCase);
                if (m.Success) return int.Parse(m.Groups[1].Value);
            }
            var m2 = Regex.Match(text, @"^\s*0*(\d{1,4})");
            if (m2.Success) return int.Parse(m2.Groups[1].Value);
            return null;
        }

        static string StripVol(string seg)
        {
            var t = Regex.Replace(seg, @"第\s*0*\d{1,4}\s*[卷话話集]", "");
            t = Regex.Replace(t, @"[卷]\s*0*\d{1,4}", "");
            t = Regex.Replace(t, @"\bv(?:ol)?\.?\s*0*\d{1,4}\b", "", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"#\s*0*\d{1,4}", "");
            t = Regex.Replace(t, @"^\s*0*\d{1,4}", "");
            return t.Trim(' ', '-', '_', '·', '~', '～');
        }

        public static void SplitStem(string stem, out string series, out int? vol, out string label, out bool special)
        {
            var s = Regex.Replace(stem, @"\.(kepub|epub|cbz)$", "", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\.kepub$", "", RegexOptions.IgnoreCase);
            special = SpecialRe.IsMatch(s);
            series = ""; label = ""; vol = null;

            var m = Regex.Match(s, @"^\[[^\]]+\]\[([^\]]+)\](.*)$");
            if (m.Success)
            {
                series = m.Groups[1].Value.Trim();
                var rest = m.Groups[2].Value.Trim();
                vol = FindVolume(rest);
                label = StripVol(rest);
                return;
            }

            var parts = s.Split(new[] { " - " }, StringSplitOptions.RemoveEmptyEntries)
                         .Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
            if (parts.Count >= 2)
            {
                series = Regex.Replace(parts[0], @"^\[[^\]]+\]", "").Trim();
                if (series.Length == 0) series = parts[0];

                List<string> body;
                if (parts.Count == 2 && FindVolume(parts[1]).HasValue) body = new List<string> { parts[1] };
                else body = parts.Skip(1).Take(parts.Count - 2).ToList();
                if (body.Count == 0) body = new List<string> { parts[1] };

                foreach (var seg in body)
                {
                    var v = FindVolume(seg);
                    if (v.HasValue && !vol.HasValue) { vol = v; label = StripVol(seg); }
                    else if (label.Length == 0) { label = StripVol(seg); if (label.Length == 0) label = seg.Trim(); }
                }
                return;
            }

            vol = FindVolume(s);
            var head = Regex.Split(s, @"[卷]\s*0*\d+|第\s*0*\d+\s*[卷话話]")[0];
            series = (head.Length > 0 ? head : s).Trim(' ', '-', '_');
        }

        public static List<int> NaturalKey(string s)
        {
            var res = new List<int>();
            var sb = new StringBuilder();
            foreach (var c in s)
            {
                if (char.IsDigit(c)) sb.Append(c);
                else { if (sb.Length > 0) { res.Add(int.Parse(sb.ToString())); sb.Clear(); } res.Add(c); }
            }
            if (sb.Length > 0) res.Add(int.Parse(sb.ToString()));
            return res;
        }

        static int CompareNatural(string a, string b)
        {
            var ka = NaturalKey(a); var kb = NaturalKey(b);
            for (int i = 0; i < Math.Min(ka.Count, kb.Count); i++)
                if (ka[i] != kb[i]) return ka[i].CompareTo(kb[i]);
            return ka.Count.CompareTo(kb.Count);
        }

        public static void AssignVolumes(List<Item> items)
        {
            var groups = new Dictionary<string, List<Item>>();
            foreach (var it in items)
            {
                var key = it.Series + "\u0001" + (it.IsSpecial ? "1" : "0");
                if (!groups.ContainsKey(key)) groups[key] = new List<Item>();
                groups[key].Add(it);
            }

            foreach (var g in groups.Values)
            {
                var known = g.Where(x => x.Vol.HasValue).Select(x => x.Vol.Value).ToList();
                bool allKnown = known.Count == g.Count;
                bool unique = known.Distinct().Count() == known.Count;
                if (allKnown && unique) continue;

                if (known.Count == 0 || !unique)
                {
                    g.Sort((a, b) => CompareNatural(Path.GetFileNameWithoutExtension(a.Src),
                                                    Path.GetFileNameWithoutExtension(b.Src)));
                    for (int i = 0; i < g.Count; i++) g[i].Vol = i + 1;
                }
                else
                {
                    int max = known.Max();
                    var rest = g.Where(x => !x.Vol.HasValue)
                                .OrderBy(x => Path.GetFileNameWithoutExtension(x.Src), Comparer<string>.Create(CompareNatural))
                                .ToList();
                    foreach (var it in rest) it.Vol = ++max;
                }
            }
        }

        // ---------------------------------------------------------- OPF
        public static void ParseOpf(string xml, Dictionary<string, string> meta,
                                    Dictionary<string, string> manifest, List<string> spine)
        {
            var doc = XDocument.Parse(xml);
            var md = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "metadata");
            if (md != null)
            {
                foreach (var e in md.Elements())
                {
                    var ln = e.Name.LocalName;
                    var v = e.Value.Trim();
                    if (ln == "title" && !meta.ContainsKey("title") && v.Length > 0) meta["title"] = v;
                    else if (ln == "creator" && !meta.ContainsKey("writer") && v.Length > 0) meta["writer"] = v;
                    else if (ln == "publisher" && !meta.ContainsKey("publisher") && v.Length > 0) meta["publisher"] = v;
                    else if (ln == "date" && !meta.ContainsKey("date") && v.Length > 0) meta["date"] = v;
                    else if (ln == "language" && !meta.ContainsKey("language") && v.Length > 0) meta["language"] = v;
                    else if (ln == "description" && v.Length > 0) meta["summary"] = v;
                    else if (ln == "subject" && v.Length > 0)
                        meta["subject"] = meta.ContainsKey("subject") ? meta["subject"] + ", " + v : v;
                    else if (ln == "rights" && v.Length > 0) meta["rights"] = v;
                    else if (ln == "identifier")
                    {
                        var scheme = (string)e.Attribute(XName.Get("scheme", "http://www.idpf.org/2007/opf"));
                        if (scheme != null && !meta.ContainsKey("source_id") &&
                            (scheme.Equals("ISBN", StringComparison.OrdinalIgnoreCase) ||
                             scheme.Equals("MOXBID", StringComparison.OrdinalIgnoreCase)))
                            meta["source_id"] = scheme + ":" + v;
                    }
                    else if (ln == "meta")
                    {
                        var name = (string)e.Attribute("name");
                        var content = (string)e.Attribute("content");
                        if (name == "calibre:series" && content != null) meta["calibre_series"] = content.Trim();
                        else if (content != null &&
                                 (name == "comicconverter:origin" || name == "cc:origin"))
                            meta["origin"] = content.Trim();
                    }
                }
            }

            foreach (var e in doc.Descendants().Where(x => x.Name.LocalName == "item"))
            {
                var id = (string)e.Attribute("id");
                var href = (string)e.Attribute("href");
                if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(href) && !manifest.ContainsKey(id))
                    manifest[id] = href;
            }

            foreach (var e in doc.Descendants().Where(x => x.Name.LocalName == "itemref"))
            {
                var idref = (string)e.Attribute("idref");
                if (!string.IsNullOrEmpty(idref)) spine.Add(idref);
            }
        }

        static string NormZipPath(string baseDir, string href)
        {
            var combined = string.IsNullOrEmpty(baseDir) ? href : baseDir + "/" + href;
            var parts = new List<string>();
            foreach (var seg in combined.Split('/'))
            {
                if (seg == "." || seg.Length == 0) continue;
                if (seg == "..") { if (parts.Count > 0) parts.RemoveAt(parts.Count - 1); }
                else parts.Add(seg);
            }
            return string.Join("/", parts);
        }

        public static List<string> LoadPages(ZipArchive zip, Dictionary<string, string> manifest,
                                             List<string> spine, string opfPath, bool dropJunk)
        {
            var names = new HashSet<string>(zip.Entries.Select(e => e.FullName), StringComparer.Ordinal);
            var baseDir = opfPath.Contains("/") ? opfPath.Substring(0, opfPath.LastIndexOf('/')) : "";
            var pages = new List<string>();
            var imgRe = new Regex("<(?:img|image)\\b[^>]*?(?:src|xlink:href)\\s*=\\s*\"([^\"]+)\"", RegexOptions.Compiled);

            foreach (var idref in spine)
            {
                string href;
                if (!manifest.TryGetValue(idref, out href)) continue;
                var page = NormZipPath(baseDir, href);
                if (!names.Contains(page)) continue;

                string doc;
                try { doc = ReadText(zip.GetEntry(page)); }
                catch { continue; }

                var m = imgRe.Match(doc);
                if (!m.Success) continue;
                var src = m.Groups[1].Value.Trim();
                if (src.StartsWith("data:") || src.Contains("://")) continue;

                var dir = page.Contains("/") ? page.Substring(0, page.LastIndexOf('/')) : "";
                var img = NormZipPath(dir, src);
                if (!names.Contains(img)) continue;
                if (!ImgExt.Contains(Path.GetExtension(img).ToLowerInvariant())) continue;
                if (dropJunk && Junk.Contains(Path.GetFileName(img))) continue;
                pages.Add(img);
            }
            return pages;
        }

        public static string ReadText(ZipArchiveEntry e)
        {
            using (var s = e.Open())
            using (var r = new StreamReader(s, Encoding.UTF8, true))
                return r.ReadToEnd();
        }

        // ---------------------------------------------------------- 输出命名
        public static string SeriesFolder(Item it, Dictionary<string, object> cfg)
        {
            var name = it.Series;
            if (it.IsSpecial && GetStr(cfg, "special", "series") == "series")
                name = name + " " + GetStr(cfg, "special_suffix", "番外");
            return SafeName(name, 120);
        }

        public static string BuildFileName(Item it, Dictionary<string, object> cfg)
        {
            var tpl = GetStr(cfg, "name_template", "{series} v{vol:02d}");
            int vol = it.Vol ?? 1;
            var core = tpl.Replace("{series}", it.Series)
                          .Replace("{vol:02d}", vol.ToString("D2"))
                          .Replace("{vol3}", vol.ToString("D3"))
                          .Replace("{vol}", vol.ToString())
                          .Replace("{num}", vol.ToString());
            core = SafeName(core, 110);
            var label = GetBool(cfg, "keep_label", true) ? it.Label : "";
            if (!string.IsNullOrEmpty(label)) core = core + " (" + SafeName(label, 40) + ")";
            return core + ".cbz";
        }

        public static string BuildComicInfo(Item it, Dictionary<string, object> cfg, int pageCount)
        {
            int vol = it.Vol ?? 1;
            object number = vol;
            if (it.IsSpecial && GetStr(cfg, "special", "series") == "number" && GetStr(cfg, "target", "both") != "kavita")
                number = vol > 1 ? (object)(vol - 0.5) : 0.5;

            var lang = "zh";
            string l;
            if (it.Meta.TryGetValue("language", out l) && !string.IsNullOrEmpty(l))
                lang = (l == "zh-CN" || l == "zh-TW" || l == "zh-HK") ? "zh" : l;

            var mode = string.IsNullOrEmpty(it.Reading) ? GetStr(cfg, "reading", "auto") : it.Reading;
            if (mode != "rtl" && mode != "ltr" && mode != "ttb") mode = "rtl";
            var manga = mode == "rtl" ? "YesAndRightToLeft" : "No";

            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n");
            sb.Append("<ComicInfo xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\">\n");
            sb.Append("  <Title>").Append(Esc(it.Title.Length > 0 ? it.Title : it.Series)).Append("</Title>\n");
            sb.Append("  <Series>").Append(Esc(it.Series)).Append("</Series>\n");
            sb.Append("  <Number>").Append(number).Append("</Number>\n");
            sb.Append("  <Volume>").Append(vol).Append("</Volume>\n");
            sb.Append("  <PageCount>").Append(pageCount).Append("</PageCount>\n");
            sb.Append("  <LanguageISO>").Append(Esc(lang)).Append("</LanguageISO>\n");
            sb.Append("  <Manga>").Append(manga).Append("</Manga>\n");
            // 下面两个不是 ComicInfo 标准字段，Kavita / Komga 会忽略；
            // 写在这里是为了下次换目录重跑时直接复用判定，不再联网。
            if (!string.IsNullOrEmpty(it.Country))
                sb.Append("  <OriginCountry>").Append(it.Country).Append("</OriginCountry>\n");
            sb.Append("  <ReadingMode>").Append(mode).Append("</ReadingMode>\n");

            AppendTag(sb, "Writer", it.Meta, "writer");
            AppendTag(sb, "Publisher", it.Meta, "publisher");
            AppendTag(sb, "Summary", it.Meta, "summary");
            AppendTag(sb, "Genre", it.Meta, "subject");

            string date;
            if (it.Meta.TryGetValue("date", out date))
            {
                var m = Regex.Match(date, "(\\d{4})");
                if (m.Success) sb.Append("  <Year>").Append(m.Groups[1].Value).Append("</Year>\n");
            }

            if (it.IsSpecial) sb.Append("  <Format>Special</Format>\n");

            var notes = new List<string>();
            string sid, rights;
            if (it.Meta.TryGetValue("source_id", out sid) && !string.IsNullOrEmpty(sid)) notes.Add(sid);
            if (it.Meta.TryGetValue("rights", out rights) && !string.IsNullOrEmpty(rights)) notes.Add(rights);
            if (notes.Count > 0) sb.Append("  <Notes>").Append(Esc(string.Join(" | ", notes))).Append("</Notes>\n");

            sb.Append("</ComicInfo>");
            return sb.ToString();
        }

        static void AppendTag(StringBuilder sb, string tag, Dictionary<string, string> meta, string key)
        {
            string v;
            if (meta.TryGetValue(key, out v) && !string.IsNullOrEmpty(v))
                sb.Append("  <").Append(tag).Append(">").Append(Esc(v))
                  .Append("</").Append(tag).Append(">\n");
        }

        static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }

        // ---------------------------------------------------------- 扫描
        public static List<Item> ScanSources(Dictionary<string, object> cfg, List<string> errors, Action<string> log)
        {
            var items = new List<Item>();
            var files = new List<string>();
            foreach (var s in GetList(cfg, "sources"))
            {
                if (!Directory.Exists(s)) continue;
                files.AddRange(Directory.GetFiles(s, "*.epub", SearchOption.AllDirectories)
                                        .OrderBy(x => x, StringComparer.Ordinal));
            }
            int limit = GetInt(cfg, "limit", 0);
            if (limit > 0 && files.Count > limit) files = files.Take(limit).ToList();

            bool useT2s = GetStr(cfg, "t2s", "tw2s").Length > 0;
            bool dropJunk = GetBool(cfg, "drop_junk", true);

            foreach (var f in files)
            {
                try
                {
                    using (var fs = File.OpenRead(f))
                    using (var zip = new ZipArchive(fs, ZipArchiveMode.Read))
                    {
                        var opfEntry = zip.Entries.Where(e => e.FullName.ToLowerInvariant().EndsWith(".opf"))
                                                  .OrderBy(e => e.FullName.Length).FirstOrDefault();
                        if (opfEntry == null) throw new Exception("无 OPF");

                        var meta = new Dictionary<string, string>();
                        var manifest = new Dictionary<string, string>();
                        var spine = new List<string>();
                        ParseOpf(ReadText(opfEntry), meta, manifest, spine);
                        var pages = LoadPages(zip, manifest, spine, opfEntry.FullName, dropJunk);
                        if (pages.Count < 3) throw new Exception("页数异常(" + pages.Count + ")");

                        var stem = Regex.Replace(Path.GetFileName(f), @"\.(kepub\.)?epub$", "", RegexOptions.IgnoreCase);
                        string series, label; int? vol; bool special;
                        SplitStem(stem, out series, out vol, out label, out special);
                        if (string.IsNullOrEmpty(series))
                        {
                            string cs;
                            series = meta.TryGetValue("calibre_series", out cs) && cs.Length > 0 ? cs :
                                     meta.TryGetValue("title", out cs) ? cs : stem;
                        }

                        string title;
                        var it = new Item
                        {
                            Src = f,
                            IsSpecial = special,
                            Pages = pages.Count,
                            Vol = vol,
                            TallRatio = Origin.TallRatio(zip, pages, 9)
                        };
                        it.Series = useT2s ? Chinese.ToSimplified(SafeName(series, 120), true) : SafeName(series, 120);
                        it.Label = useT2s ? Chinese.ToSimplified(label, true) : label;
                        it.Title = useT2s ? Chinese.ToSimplified(meta.TryGetValue("title", out title) && title.Length > 0 ? title : series, true)
                                          : (meta.TryGetValue("title", out title) && title.Length > 0 ? title : series);

                        foreach (var k in new[] { "writer", "publisher", "summary", "subject" })
                        {
                            string v;
                            if (meta.TryGetValue(k, out v))
                                it.Meta[k] = useT2s ? Chinese.ToSimplified(v, true) : v;
                        }
                        foreach (var k in new[] { "date", "language", "source_id", "rights", "origin" })
                        {
                            string v;
                            if (meta.TryGetValue(k, out v)) it.Meta[k] = v;
                        }

                        items.Add(it);
                    }
                }
                catch (Exception ex)
                {
                    errors.Add(Path.GetFileName(f) + "：" + ex.Message);
                }
            }

            AssignVolumes(items);
            ResolveReading(items, cfg, log);
            return items;
        }

        /// <summary>
        /// 按系列判定阅读方向：先看页面比例（长条页 → 上到下），
        /// 再按已有元数据 / 语言 / 出版社 / 标题文字 / 联网查证定始发国，
        /// 国家换算成左右开本。手动指定方向时只做离线判定，不联网。
        /// </summary>
        public static void ResolveReading(List<Item> items, Dictionary<string, object> cfg, Action<string> log)
        {
            var reading = GetStr(cfg, "reading", "auto");

            var groups = new Dictionary<string, List<Item>>(StringComparer.Ordinal);
            foreach (var it in items)
            {
                if (!groups.ContainsKey(it.Series)) groups[it.Series] = new List<Item>();
                groups[it.Series].Add(it);
            }

            var det = new OriginDetector();
            foreach (var kv in groups)
            {
                var g = kv.Value;
                var langs = new List<string>();
                string publisher = null, origin = null, author = null;
                double ratio = 0;

                foreach (var it in g)
                {
                    string v;
                    if (it.Meta.TryGetValue("language", out v) && !string.IsNullOrEmpty(v) && !langs.Contains(v))
                        langs.Add(v);
                    if (publisher == null && it.Meta.TryGetValue("publisher", out v)) publisher = v;
                    if (origin == null && it.Meta.TryGetValue("origin", out v)) origin = v;
                    if (author == null && it.Meta.TryGetValue("writer", out v)) author = v;
                    if (it.TallRatio > ratio) ratio = it.TallRatio;
                }

                DetectResult r;
                try
                {
                    r = det.Detect(kv.Key, author, ratio, langs, origin, publisher, reading, log);
                }
                catch (Exception ex)
                {
                    r = new DetectResult
                    {
                        Mode = reading == "auto" ? "rtl" : reading,
                        ModeSource = "default"
                    };
                    if (log != null) log("阅读方向判定失败（" + kv.Key + "）：" + ex.Message);
                }

                foreach (var it in g)
                {
                    it.Country = r.Country;
                    it.CountrySource = r.CountrySource;
                    it.Reading = r.Mode;
                    it.ReadingSource = r.ModeSource;
                }
            }
            det.Save();
        }

        // ---------------------------------------------------------- 配置取值
        public static string GetStr(Dictionary<string, object> c, string k, string def)
        {
            object v;
            if (c != null && c.TryGetValue(k, out v) && v != null)
            {
                var s = v.ToString();
                return s.Length > 0 ? s : def;
            }
            return def;
        }

        public static bool GetBool(Dictionary<string, object> c, string k, bool def)
        {
            object v;
            if (c == null || !c.TryGetValue(k, out v) || v == null) return def;
            if (v is bool) return (bool)v;
            bool r;
            return bool.TryParse(v.ToString(), out r) ? r : def;
        }

        public static int GetInt(Dictionary<string, object> c, string k, int def)
        {
            object v;
            if (c == null || !c.TryGetValue(k, out v) || v == null) return def;
            try { return Convert.ToInt32(v); } catch { return def; }
        }

        public static List<string> GetList(Dictionary<string, object> c, string k)
        {
            var res = new List<string>();
            object v;
            if (c == null || !c.TryGetValue(k, out v) || v == null) return res;
            var arr = v as object[];
            if (arr != null) { foreach (var o in arr) if (o != null) res.Add(o.ToString()); return res; }
            var arr2 = v as System.Collections.ArrayList;
            if (arr2 != null) { foreach (var o in arr2) if (o != null) res.Add(o.ToString()); return res; }
            var s = v.ToString();
            if (s.Length > 0) res.Add(s);
            return res;
        }

        // ---------------------------------------------------------- 转换
        public static ConvertResult ConvertOne(Item it, Dictionary<string, object> cfg, Action<string> log)
        {
            var res = new ConvertResult { Series = it.Series, Vol = it.Vol ?? 1 };
            var outRoot = GetStr(cfg, "out", "");
            var folder = Path.Combine(outRoot, SeriesFolder(it, cfg));
            var dst = Path.Combine(folder, BuildFileName(it, cfg));
            res.Dst = dst;

            if (File.Exists(dst) && !GetBool(cfg, "overwrite", false)) { res.Skip = true; return res; }

            try
            {
                Directory.CreateDirectory(folder);
                using (var src = ZipFile.OpenRead(it.Src))
                {
                    var opfEntry = src.Entries.Where(e => e.FullName.ToLowerInvariant().EndsWith(".opf"))
                                              .OrderBy(e => e.FullName.Length).FirstOrDefault();
                    var meta = new Dictionary<string, string>();
                    var manifest = new Dictionary<string, string>();
                    var spine = new List<string>();
                    ParseOpf(ReadText(opfEntry), meta, manifest, spine);
                    var pages = LoadPages(src, manifest, spine, opfEntry.FullName, GetBool(cfg, "drop_junk", true));
                    res.Pages = pages.Count;

                    var level = GetStr(cfg, "compress", "store") == "deflate"
                                ? CompressionLevel.Optimal : CompressionLevel.NoCompression;

                    if (File.Exists(dst)) File.Delete(dst);
                    using (var fs = File.Create(dst))
                    using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
                    {
                        for (int i = 0; i < pages.Count; i++)
                        {
                            var entry = zip.CreateEntry((i + 1).ToString("D4") + Path.GetExtension(pages[i]).ToLowerInvariant(), level);
                            using (var es = entry.Open())
                            using (var ss = src.GetEntry(pages[i]).Open())
                                ss.CopyTo(es);
                        }
                        var ci = zip.CreateEntry("ComicInfo.xml", CompressionLevel.Optimal);
                        using (var es = ci.Open())
                        {
                            var bytes = new UTF8Encoding(false).GetBytes(BuildComicInfo(it, cfg, pages.Count));
                            es.Write(bytes, 0, bytes.Length);
                        }
                    }
                }
                res.Mb = Math.Round(new FileInfo(dst).Length / 1048576.0, 1);
            }
            catch (Exception ex)
            {
                res.Err = ex.Message;
            }
            return res;
        }
    }
}
