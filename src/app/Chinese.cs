using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace ComicConverter
{
    /// <summary>繁转简：基于 OpenCC 的纯文本词表，最长匹配。不依赖任何外部库。</summary>
    public static class Chinese
    {
        static Dictionary<char, char> _chars;
        static Dictionary<string, string> _phrases;
        static int _maxLen = 2;
        static bool _ready;

        static readonly Regex Kana = new Regex("[\u3041-\u3096\u30a1-\u30fa\u30fc-\u30ff]", RegexOptions.Compiled);

        public static bool IsJapanese(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            int kana = 0, total = 0;
            foreach (var c in s)
            {
                if (char.IsWhiteSpace(c)) continue;
                total++;
                if (Kana.IsMatch(c.ToString())) kana++;
            }
            if (kana == 0) return false;
            return kana >= 3 || (double)kana / Math.Max(total, 1) >= 0.25;
        }

        static void Init()
        {
            if (_ready) return;
            _chars = new Dictionary<char, char>();
            _phrases = new Dictionary<string, string>();

            LoadChars("TWVariants.txt");
            LoadPhrases("TSPhrases.txt");
            LoadChars("TSCharacters.txt");

            foreach (var k in _phrases.Keys)
                if (k.Length > _maxLen) _maxLen = k.Length;
            _ready = true;
        }

        static string Res(string name)
        {
            var asm = Assembly.GetExecutingAssembly();
            var resName = "ComicConverter.Data." + name;
            using (var s = asm.GetManifestResourceStream(resName))
            {
                if (s == null) return null;
                using (var r = new StreamReader(s, Encoding.UTF8))
                    return r.ReadToEnd();
            }
        }

        static void LoadChars(string file)
        {
            var txt = Res(file);
            if (txt == null) return;
            foreach (var raw in txt.Split('\n'))
            {
                var line = raw.Trim('\r', ' ', '\t');
                if (line.Length == 0 || line[0] == '#') continue;
                var parts = line.Split('\t');
                if (parts.Length < 2) continue;
                var key = parts[0].Trim();
                var vals = parts[1].Trim().Split(' ');
                if (key.Length != 1 || vals.Length == 0 || vals[0].Length == 0) continue;
                if (!_chars.ContainsKey(key[0])) _chars[key[0]] = vals[0][0];
            }
        }

        static void LoadPhrases(string file)
        {
            var txt = Res(file);
            if (txt == null) return;
            foreach (var raw in txt.Split('\n'))
            {
                var line = raw.Trim('\r', ' ', '\t');
                if (line.Length == 0 || line[0] == '#') continue;
                var parts = line.Split('\t');
                if (parts.Length < 2) continue;
                var key = parts[0].Trim();
                var vals = parts[1].Trim().Split(' ');
                if (key.Length < 2 || vals.Length == 0 || vals[0].Length == 0) continue;
                if (!_phrases.ContainsKey(key)) _phrases[key] = vals[0];
            }
        }

        public static string ToSimplified(string s, bool enable)
        {
            if (!enable || string.IsNullOrEmpty(s) || IsJapanese(s)) return s;
            Init();
            var sb = new StringBuilder(s.Length + 8);
            int i = 0;
            while (i < s.Length)
            {
                bool hit = false;
                int max = Math.Min(_maxLen, s.Length - i);
                for (int len = max; len >= 2; len--)
                {
                    string v;
                    if (_phrases.TryGetValue(s.Substring(i, len), out v))
                    {
                        sb.Append(v);
                        i += len;
                        hit = true;
                        break;
                    }
                }
                if (hit) continue;

                char c = s[i];
                char cv;
                sb.Append(_chars.TryGetValue(c, out cv) ? cv : c);
                i++;
            }
            return sb.ToString();
        }
    }
}
