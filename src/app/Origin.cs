using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace ComicConverter
{
    /// <summary>一个系列的阅读方向判定结果。</summary>
    public sealed class DetectResult
    {
        /// <summary>始发国家/地区，ISO 3166-1 alpha-2；判定不出为 null。</summary>
        public string Country;

        /// <summary>国家来自哪里：meta / cache / lang / publisher / script / web / none / netfail。</summary>
        public string CountrySource = "none";

        /// <summary>rtl / ltr / ttb。</summary>
        public string Mode = "rtl";

        /// <summary>manual / shape / country / default。</summary>
        public string ModeSource = "default";
    }

    /// <summary>国家与阅读方向的映射、页面长宽比的判读。</summary>
    public static class Origin
    {
        public const string JP = "JP", KR = "KR", CN = "CN", TW = "TW", HK = "HK";

        public static string CountryName(string c)
        {
            if (c == JP) return "日本";
            if (c == KR) return "韩国";
            if (c == CN) return "中国";
            if (c == TW) return "中国台湾";
            if (c == HK) return "中国香港";
            return "未判定";
        }

        public static string ModeName(string m)
        {
            if (m == "rtl") return "右开本";
            if (m == "ltr") return "左开本";
            if (m == "ttb") return "上到下";
            return "右开本";
        }

        /// <summary>国家 → 阅读方向。日漫右开，中/港台/韩漫左开。</summary>
        public static string ModeFor(string country)
        {
            if (country == JP) return "rtl";
            if (country == KR || country == CN || country == TW || country == HK) return "ltr";
            return "rtl";
        }

        /// <summary>把各种写法的国家/地区归一化成两个字母的代码。</summary>
        public static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            var t = s.Trim().ToUpperInvariant();
            switch (t)
            {
                case "JP": case "JPN": case "JAPAN": case "日本": return JP;
                case "KR": case "KOR": case "KOREA": case "SOUTH KOREA": case "韩国": case "韓國": return KR;
                case "CN": case "CHN": case "CHINA": case "中国": case "中國": case "中国大陆": return CN;
                case "TW": case "TWN": case "TAIWAN": case "台湾": case "臺灣": case "台灣": return TW;
                case "HK": case "HKG": case "HONG KONG": case "香港": return HK;
            }
            return null;
        }

        // ---------------------------------------------------------- 页面长宽比
        /// <summary>
        /// 抽样读出页面的 高/宽 中位数。条漫（长条页）通常在 3 以上，
        /// 单页漫画在 1.4~1.6，所以 2.0 是很安全的分界线。
        /// </summary>
        public static double TallRatio(ZipArchive zip, List<string> pages, int sample)
        {
            if (pages == null || pages.Count == 0) return 0;

            var res = new List<double>();
            int step = Math.Max(1, pages.Count / Math.Max(1, sample));
            for (int i = 0; i < pages.Count && res.Count < sample; i += step)
            {
                int w, h;
                if (!TrySize(zip, pages[i], out w, out h)) continue;
                if (w <= 0 || h <= 0) continue;
                res.Add((double)h / w);
            }
            if (res.Count == 0) return 0;
            res.Sort();
            return res[res.Count / 2];
        }

        static bool TrySize(ZipArchive zip, string name, out int w, out int h)
        {
            w = h = 0;
            try
            {
                var e = zip.GetEntry(name);
                if (e == null) return false;

                var buf = new byte[70000];
                int n = 0;
                using (var s = e.Open())
                {
                    int r;
                    while (n < buf.Length && (r = s.Read(buf, n, buf.Length - n)) > 0) n += r;
                }
                return TryParseSize(buf, n, out w, out h);
            }
            catch { return false; }
        }

        static int BE32(byte[] b, int i)
        {
            return (b[i] << 24) | (b[i + 1] << 16) | (b[i + 2] << 8) | b[i + 3];
        }

        static bool TryParseSize(byte[] b, int n, out int w, out int h)
        {
            w = h = 0;
            if (n < 16) return false;

            // PNG
            if (b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47)
            {
                if (n < 24) return false;
                w = BE32(b, 16); h = BE32(b, 20);
                return w > 0 && h > 0;
            }

            // GIF
            if (b[0] == 'G' && b[1] == 'I' && b[2] == 'F')
            {
                w = b[6] | (b[7] << 8);
                h = b[8] | (b[9] << 8);
                return w > 0 && h > 0;
            }

            // JPEG：走到 SOF 段
            if (b[0] == 0xFF && b[1] == 0xD8)
            {
                int i = 2;
                while (i + 9 < n)
                {
                    if (b[i] != 0xFF) { i++; continue; }
                    byte m = b[i + 1];
                    if (m == 0xFF) { i++; continue; }
                    if (m == 0xD8 || m == 0x01 || (m >= 0xD0 && m <= 0xD7)) { i += 2; continue; }
                    if (m == 0xD9 || m == 0xDA) break;
                    int len = (b[i + 2] << 8) | b[i + 3];
                    if (len < 2) break;
                    if (m >= 0xC0 && m <= 0xCF && m != 0xC4 && m != 0xC8 && m != 0xCC)
                    {
                        h = (b[i + 5] << 8) | b[i + 6];
                        w = (b[i + 7] << 8) | b[i + 8];
                        return w > 0 && h > 0;
                    }
                    i += 2 + len;
                }
                return false;
            }

            // WebP
            if (b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F' &&
                b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P' && n >= 30)
            {
                var tag = Encoding.ASCII.GetString(b, 12, 4);
                if (tag == "VP8X")
                {
                    w = (b[24] | (b[25] << 8) | (b[26] << 16)) + 1;
                    h = (b[27] | (b[28] << 8) | (b[29] << 16)) + 1;
                    return true;
                }
                if (tag == "VP8 ")
                {
                    int o = -1;
                    for (int i = 12; i + 10 < n; i++)
                        if (b[i] == 0x9D && b[i + 1] == 0x01 && b[i + 2] == 0x2A) { o = i + 3; break; }
                    if (o < 0) return false;
                    w = (b[o] | (b[o + 1] << 8)) & 0x3FFF;
                    h = (b[o + 2] | (b[o + 3] << 8)) & 0x3FFF;
                    return w > 0 && h > 0;
                }
                if (tag == "VP8L")
                {
                    w = ((b[21] | (b[22] << 8)) & 0x3FFF) + 1;
                    h = (((b[22] >> 6) | (b[23] << 2) | (b[24] << 10)) & 0x3FFF) + 1;
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>
    /// 判定「这一本漫画是哪个国家/地区出的」，再换算成阅读方向。
    /// 优先用现成的元数据与本地缓存，实在没有才联网（Wikidata），
    /// 并且一次扫描里有次数上限、超时熔断，不给用户添麻烦。
    /// </summary>
    public sealed class OriginDetector
    {
        const int WebBudgetPerScan = 150;
        const int TimeoutMs = 6000;

        static readonly TimeSpan WebWindow = TimeSpan.FromSeconds(120);

        // Wikidata 里几个稳定的 QID：国家/地区、语言、漫画类目
        static readonly Dictionary<string, string> CountryQid = new Dictionary<string, string>
        {
            {"Q17", Origin.JP}, {"Q884", Origin.KR}, {"Q148", Origin.CN},
            {"Q865", Origin.TW}, {"Q8646", Origin.HK}
        };

        static readonly Dictionary<string, string> LangQid = new Dictionary<string, string>
        {
            {"Q5287", Origin.JP}, {"Q9176", Origin.KR}, {"Q7850", Origin.CN}
        };

        static readonly HashSet<string> ComicClass = new HashSet<string>
        {
            "Q21198342",   // 日本系列漫画
            "Q137637896",  // 中国系列漫画
            "Q137644978",  // 香港系列漫画
            "Q74262765",   // 韩国系列漫画
            "Q213369",     // 网络漫画
            "Q1004",       // 漫画
            "Q8274"        // 日本漫画
        };

        static readonly HashSet<string> NotComicClass = new HashSet<string>
        {
            "Q11424",      // 电影
            "Q5398426",    // 电视剧
            "Q117467246",  // 电视动画
            "Q202866",     // 动画电影
            "Q1107",       // 日本动画
            "Q7889"        // 电子游戏
        };

        static readonly Regex Kana = new Regex("[\u3041-\u3096\u30a1-\u30fa\u30fc-\u30ff]", RegexOptions.Compiled);
        static readonly Regex Hangul = new Regex("[\u1100-\u11ff\u3130-\u318f\uac00-\ud7a3]", RegexOptions.Compiled);
        static readonly Regex Noise = new Regex("[\\s\\-_·・:：!！?？'\"“”‘’]+", RegexOptions.Compiled);

        static readonly string[] JpPublishers = {
            "集英社", "講談社", "讲谈社", "小学館", "小學館", "白泉社", "双葉社", "雙葉社",
            "秋田書店", "秋田书店", "スクウェア", "KADOKAWA", "角川", "竹書房", "少年画報",
            "一迅社", "LEED", "実業之日本社", "芳文社", "マッグガーデン", "新潮社", "日本"
        };
        static readonly string[] TwPublishers = {
            "東立", "东立", "尖端", "長鴻", "长鸿", "青文", "台灣角川", "台湾角川", "東販", "东贩", "大然"
        };
        static readonly string[] CnPublishers = {
            "bilibili", "哔哩哔哩", "嗶哩嗶哩", "快看", "騰訊動漫", "腾讯动漫", "有妖气", "漫友", "知音漫客"
        };
        static readonly string[] HkPublishers = {
            "玉皇朝", "天下出版", "香港", "文化傳信", "文化传信"
        };

        static readonly string CachePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ComicConverter", "origin-cache.json");

        readonly Dictionary<string, string> _cache =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        bool _dirty;
        int _budget = WebBudgetPerScan;
        int _fails;
        bool _netOff;
        DateTime _deadline = DateTime.MinValue;

        public OriginDetector()
        {
            LoadCache();
        }

        /// <summary>本次扫描里联网查了多少次（便于日志）。</summary>
        public int WebLookups { get; private set; }

        // ---------------------------------------------------------- 缓存
        void LoadCache()
        {
            try
            {
                if (!File.Exists(CachePath)) return;
                var d = MiniJson.Parse(File.ReadAllText(CachePath, Encoding.UTF8)) as Dictionary<string, object>;
                if (d == null) return;
                var items = d.ContainsKey("items") ? d["items"] as Dictionary<string, object> : null;
                if (items == null) return;
                foreach (var kv in items)
                    if (kv.Value != null) _cache[kv.Key] = kv.Value.ToString();
            }
            catch { }
        }

        public void Save()
        {
            if (!_dirty) return;
            try
            {
                var items = new Dictionary<string, object>();
                foreach (var kv in _cache) items[kv.Key] = kv.Value;
                var root = new Dictionary<string, object>
                {
                    {"version", 1},
                    {"updated", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")},
                    {"items", items}
                };
                Directory.CreateDirectory(Path.GetDirectoryName(CachePath));
                File.WriteAllText(CachePath, MiniJson.Serialize(root), new UTF8Encoding(false));
                _dirty = false;
            }
            catch { }
        }

        static string Key(string series)
        {
            return Noise.Replace(series ?? "", "").Trim().ToLowerInvariant();
        }

        // ---------------------------------------------------------- 主流程
        /// <summary>
        /// tall 为页面 高/宽 中位数（0 表示没量到），languages 为源文件里出现的 dc:language。
        /// </summary>
        public DetectResult Detect(string series, string author, double tall,
                                   IEnumerable<string> languages, string metaOrigin,
                                   string publisher, string cfgReading, Action<string> log)
        {
            var r = new DetectResult();

            // 1) 国家/地区
            string country = null, src = "none";
            if (!string.IsNullOrEmpty(metaOrigin))
            {
                country = Origin.Normalize(metaOrigin);
                if (country != null) src = "meta";
            }

            string cached;
            bool negCached = false;
            if (country == null && _cache.TryGetValue(Key(series), out cached))
            {
                if (string.IsNullOrEmpty(cached)) negCached = true;
                else { country = cached; src = "cache"; }
            }

            if (country == null) country = FromLanguage(languages, ref src);
            if (country == null) country = FromPublisher(publisher, ref src);
            if (country == null) country = FromScript(series, ref src);

            if (country == null && negCached) src = "cache-miss";

            // 手动指定方向时只做离线判定：不去联网打扰用户
            bool allowNet = string.IsNullOrEmpty(cfgReading) || cfgReading == "auto";

            if (country == null && !negCached && allowNet)
            {
                string fs;
                country = Lookup(series, author, out fs);
                src = country == null ? (fs ?? "none") : "web";

                // 只有「网络正常但确实查不到」才写空值缓存，避免断网时把结果固化成未知
                if (src == "web" || src == "web-miss")
                {
                    var k = Key(series);
                    if (k.Length > 0 && !_cache.ContainsKey(k))
                    {
                        _cache[k] = country ?? "";
                        _dirty = true;
                    }
                }
            }

            r.Country = country;
            r.CountrySource = src;

            // 2) 阅读方向
            if (!string.IsNullOrEmpty(cfgReading) && cfgReading != "auto")
            {
                r.Mode = cfgReading;
                r.ModeSource = "manual";
            }
            else if (tall >= 2.0)
            {
                r.Mode = "ttb";
                r.ModeSource = "shape";
            }
            else if (country != null)
            {
                r.Mode = Origin.ModeFor(country);
                r.ModeSource = "country";
            }
            else
            {
                r.Mode = "rtl";
                r.ModeSource = "default";
            }

            if (log != null)
            {
                if (r.ModeSource == "manual") { /* 手动指定不刷日志 */ }
                else if (r.CountrySource == "web")
                    log("阅读方向：" + series + " → " + Origin.CountryName(r.Country) + "，"
                        + Origin.ModeName(r.Mode) + "（联网查证）");
                else if (r.ModeSource == "shape" && r.Country == null)
                    log("阅读方向：" + series + " → 长条页，判为上到下");
                else if (r.CountrySource != "cache" && r.CountrySource != "none")
                    log("阅读方向：" + series + " → " + Origin.CountryName(r.Country) + "，"
                        + Origin.ModeName(r.Mode));
            }
            return r;
        }

        // ---------------------------------------------------------- 离线线索
        string FromLanguage(IEnumerable<string> languages, ref string src)
        {
            if (languages == null) return null;
            foreach (var raw in languages)
            {
                var l = (raw ?? "").Trim().ToLowerInvariant();
                string c = null;
                if (l == "ja" || l.StartsWith("ja-")) c = Origin.JP;
                else if (l == "ko" || l.StartsWith("ko-")) c = Origin.KR;
                else if (l == "zh-tw" || l == "zh-hk" || l == "zh-hant") c = l == "zh-hk" ? Origin.HK : Origin.TW;
                else if (l == "zh" || l.StartsWith("zh-")) c = Origin.CN;

                if (c != null) { src = "lang"; return c; }
            }
            return null;
        }

        string FromPublisher(string publisher, ref string src)
        {
            if (string.IsNullOrEmpty(publisher)) return null;
            var p = publisher;
            foreach (var k in HkPublishers) if (p.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) { src = "publisher"; return Origin.HK; }
            foreach (var k in TwPublishers) if (p.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) { src = "publisher"; return Origin.TW; }
            foreach (var k in CnPublishers) if (p.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) { src = "publisher"; return Origin.CN; }
            foreach (var k in JpPublishers) if (p.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) { src = "publisher"; return Origin.JP; }
            return null;
        }

        string FromScript(string series, ref string src)
        {
            if (string.IsNullOrEmpty(series)) return null;
            if (Kana.IsMatch(series)) { src = "script"; return Origin.JP; }
            if (Hangul.IsMatch(series)) { src = "script"; return Origin.KR; }
            return null;
        }

        // ---------------------------------------------------------- 联网
        string Lookup(string series, string author, out string src)
        {
            src = "none";
            if (_netOff || _budget <= 0) { src = "netoff"; return null; }
            if (_deadline == DateTime.MinValue) _deadline = DateTime.Now + WebWindow;
            if (DateTime.Now > _deadline) { _netOff = true; src = "netoff"; return null; }

            _budget--;

            var term = CleanTitle(series);
            if (term.Length == 0) { src = "none"; return null; }

            var country = LookupTitle(term);
            if (country == null && !string.IsNullOrEmpty(author)) country = LookupAuthor(author);

            if (country == null)
            {
                src = _fails >= 3 ? "netfail" : "web-miss";
                return null;
            }

            WebLookups++;
            src = "web";
            return country;
        }

        static string CleanTitle(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var t = Regex.Replace(s, @"[\(（\[【].*?[\)）\]】]", " ");
            t = Regex.Replace(t, @"\b(v|vol|volume)\s*\d+", " ", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"第\s*\d+\s*[卷话話集]", " ");
            return t.Trim(' ', '-', '_', '·');
        }

        static readonly Regex Cjk = new Regex("[\u3040-\u30ff\u3400-\u9fff\uac00-\ud7af]", RegexOptions.Compiled);

        /// <summary>中文/日文/韩文标题优先查 zh、ja，拉丁标题优先查 en，省一次请求。</summary>
        static string[] Langs(string term)
        {
            return Cjk.IsMatch(term) ? new[] { "zh", "ja" } : new[] { "en", "zh", "ja" };
        }

        string LookupTitle(string term)
        {
            foreach (var lang in Langs(term))
            {
                List<Hit> hits;
                try { hits = Search(term, lang); }
                catch { _fails++; if (_fails >= 3) _netOff = true; return null; }

                if (hits.Count == 0) continue;
                _fails = 0;

                var c = ScoreEntities(hits, term);
                if (c != null) return c;
            }
            return null;
        }

        string LookupAuthor(string author)
        {
            var name = author.Trim();
            if (name.Length < 2) return null;

            // 常见分隔符只取第一个作者
            foreach (var sep in new[] { "，", "," , "、", ";", "；", "/", "|" })
            {
                int i = name.IndexOf(sep);
                if (i > 0) { name = name.Substring(0, i).Trim(); break; }
            }

            foreach (var lang in Langs(name))
            {
                List<Hit> hits;
                try { hits = Search(name, lang); }
                catch { return null; }

                if (hits.Count == 0) continue;
                var ids = new List<string>();
                foreach (var h in hits)
                {
                    if (ids.Count >= 3) break;
                    if (SameName(h.Label, name)) ids.Add(h.Id);
                }
                if (ids.Count == 0) continue;

                var ents = Entities(ids);
                foreach (var id in ids)
                {
                    Dictionary<string, object> ent;
                    if (!ents.TryGetValue(id, out ent)) continue;

                    var country = CountryOf(ent);
                    if (country != null) return country;
                }
            }
            return null;
        }

        string ScoreEntities(List<Hit> hits, string term)
        {
            var ids = new List<string>();
            foreach (var h in hits)
            {
                if (ids.Count >= 6) break;
                ids.Add(h.Id);
            }
            if (ids.Count == 0) return null;

            var ents = Entities(ids);

            string bestCountry = null;
            int best = 0;

            foreach (var h in hits)
            {
                Dictionary<string, object> ent;
                if (!ents.TryGetValue(h.Id, out ent)) continue;

                int score = 0;
                if (SameName(h.Label, term)) score += 6;

                var classes = ClaimIds(ent, "P31");
                foreach (var c in classes)
                {
                    if (ComicClass.Contains(c)) { score += 4; break; }
                }
                foreach (var c in classes)
                {
                    if (NotComicClass.Contains(c)) { score -= 10; break; }
                }

                var country = CountryOf(ent);
                if (country != null) score += 3;

                var desc = Description(ent);
                if (desc.Length > 0)
                {
                    if (desc.IndexOf("漫畫", StringComparison.Ordinal) >= 0 ||
                        desc.IndexOf("漫画", StringComparison.Ordinal) >= 0 ||
                        desc.IndexOf("comic", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        desc.IndexOf("webtoon", StringComparison.OrdinalIgnoreCase) >= 0) score += 3;
                    if (desc.IndexOf("電影", StringComparison.Ordinal) >= 0 ||
                        desc.IndexOf("电影", StringComparison.Ordinal) >= 0 ||
                        desc.IndexOf("電視", StringComparison.Ordinal) >= 0 ||
                        desc.IndexOf("动画", StringComparison.Ordinal) >= 0 ||
                        desc.IndexOf("動畫", StringComparison.Ordinal) >= 0 ||
                        desc.IndexOf("遊戲", StringComparison.Ordinal) >= 0 ||
                        desc.IndexOf("游戏", StringComparison.Ordinal) >= 0 ||
                        desc.IndexOf("film", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        desc.IndexOf("video game", StringComparison.OrdinalIgnoreCase) >= 0) score -= 10;
                }

                if (country != null && score > best)
                {
                    best = score;
                    bestCountry = country;
                }
            }
            return best >= 5 ? bestCountry : null;
        }

        static string CountryOf(Dictionary<string, object> ent)
        {
            foreach (var id in ClaimIds(ent, "P495"))
            {
                string c;
                if (CountryQid.TryGetValue(id, out c)) return c;
            }
            foreach (var prop in new[] { "P407", "P364" })
            {
                foreach (var id in ClaimIds(ent, prop))
                {
                    string c;
                    if (LangQid.TryGetValue(id, out c)) return c;
                }
            }
            foreach (var id in ClaimIds(ent, "P27"))
            {
                string c;
                if (CountryQid.TryGetValue(id, out c)) return c;
            }
            return null;
        }

        static bool SameName(string a, string b)
        {
            var x = NormName(a);
            var y = NormName(b);
            if (x.Length == 0 || y.Length == 0) return false;
            return x == y || x.StartsWith(y) || y.StartsWith(x);
        }

        static string NormName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var t = s;
            try { t = Chinese.ToSimplified(t, true); } catch { }
            return Noise.Replace(t, "").ToLowerInvariant();
        }

        // ---------------------------------------------------------- HTTP + JSON
        class Hit
        {
            public string Id, Label, Desc;
        }

        static string HttpGet(string url)
        {
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { }

            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "ComicConverter/" + Brand.Version + " (+https://github.com/rexlee/ComicConverter)";
            req.Accept = "application/json";
            req.Timeout = TimeoutMs;
            req.ReadWriteTimeout = TimeoutMs;
            req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            req.KeepAlive = false;

            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var s = resp.GetResponseStream())
            using (var r = new StreamReader(s, Encoding.UTF8))
                return r.ReadToEnd();
        }

        static string ApiUrl(params string[] pairs)
        {
            var sb = new StringBuilder("https://www.wikidata.org/w/api.php?action=");
            bool first = true;
            foreach (var p in pairs)
            {
                sb.Append(first ? "" : "&").Append(p);
                first = false;
            }
            sb.Append("&format=json&origin=*");
            return sb.ToString();
        }

        static List<Hit> Search(string term, string lang)
        {
            var url = ApiUrl("wbsearchentities",
                          "language=" + lang,
                          "uselang=" + lang,
                          "limit=7",
                          "search=" + Uri.EscapeDataString(term));

            var root = MiniJson.Parse(HttpGet(url)) as Dictionary<string, object>;
            var res = new List<Hit>();
            if (root == null) return res;

            var arr = root.ContainsKey("search") ? root["search"] as object[] : null;
            if (arr == null) return res;

            foreach (var o in arr)
            {
                var d = o as Dictionary<string, object>;
                if (d == null) continue;
                var id = Str(d, "id");
                if (string.IsNullOrEmpty(id)) continue;
                res.Add(new Hit
                {
                    Id = id,
                    Label = Str(d, "label") ?? "",
                    Desc = Str(d, "description") ?? ""
                });
            }
            return res;
        }

        static Dictionary<string, Dictionary<string, object>> Entities(List<string> ids)
        {
            var res = new Dictionary<string, Dictionary<string, object>>();
            if (ids.Count == 0) return res;

            var url = ApiUrl("wbgetentities",
                          "props=claims|labels|descriptions|aliases",
                          "languages=zh|ja|en",
                          "ids=" + string.Join("|", ids.ToArray()));

            var root = MiniJson.Parse(HttpGet(url)) as Dictionary<string, object>;
            if (root == null) return res;

            var ents = root.ContainsKey("entities") ? root["entities"] as Dictionary<string, object> : null;
            if (ents == null) return res;

            foreach (var kv in ents)
            {
                var d = kv.Value as Dictionary<string, object>;
                if (d != null) res[kv.Key] = d;
            }
            return res;
        }

        static string Str(Dictionary<string, object> d, string k)
        {
            object v;
            if (d == null || !d.TryGetValue(k, out v) || v == null) return null;
            return v.ToString();
        }

        static List<string> ClaimIds(Dictionary<string, object> ent, string prop)
        {
            var res = new List<string>();
            if (ent == null) return res;

            var claims = Str2Dic(ent, "claims");
            if (claims == null) return res;

            object node;
            if (!claims.TryGetValue(prop, out node)) return res;

            var arr = node as object[];
            if (arr == null) return res;

            foreach (var o in arr)
            {
                var c = o as Dictionary<string, object>;
                var ms = Str2Dic(c, "mainsnak");
                var dv = Str2Dic(ms, "datavalue");
                var val = Str2Dic(dv, "value");
                var id = Str(val, "id");
                if (!string.IsNullOrEmpty(id)) res.Add(id);
            }
            return res;
        }

        static Dictionary<string, object> Str2Dic(Dictionary<string, object> d, string k)
        {
            if (d == null) return null;
            object v;
            return d.TryGetValue(k, out v) ? v as Dictionary<string, object> : null;
        }

        static string Description(Dictionary<string, object> ent)
        {
            var desc = Str2Dic(ent, "descriptions");
            if (desc == null) return "";
            foreach (var lang in new[] { "zh", "zh-hans", "zh-hant", "en" })
            {
                var d = Str2Dic(desc, lang);
                var v = Str(d, "value");
                if (!string.IsNullOrEmpty(v)) return v;
            }
            return "";
        }
    }
}
