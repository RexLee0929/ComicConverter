using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ComicConverter
{
    /// <summary>极简 JSON：避免引入 System.Web.Extensions（与 WPF 的 XAML 编译器冲突）。</summary>
    public static class MiniJson
    {
        // ------------------------------------------------------------ 序列化
        public static string Serialize(object o)
        {
            var sb = new StringBuilder();
            Write(sb, o);
            return sb.ToString();
        }

        static void Write(StringBuilder sb, object o)
        {
            if (o == null) { sb.Append("null"); return; }
            if (o is string) { WriteString(sb, (string)o); return; }
            if (o is bool) { sb.Append(((bool)o) ? "true" : "false"); return; }
            if (o is int || o is long || o is short) { sb.Append(Convert.ToInt64(o).ToString(CultureInfo.InvariantCulture)); return; }
            if (o is double || o is float || o is decimal)
            {
                double d = Convert.ToDouble(o);
                if (Math.Abs(d - Math.Round(d)) < 1e-9) sb.Append(((long)Math.Round(d)).ToString(CultureInfo.InvariantCulture));
                else sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                return;
            }
            var dict = o as IDictionary;
            if (dict != null)
            {
                sb.Append('{');
                bool first = true;
                foreach (DictionaryEntry de in dict)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    WriteString(sb, Convert.ToString(de.Key));
                    sb.Append(':');
                    Write(sb, de.Value);
                }
                sb.Append('}');
                return;
            }
            var en = o as IEnumerable;
            if (en != null)
            {
                sb.Append('[');
                bool first = true;
                foreach (var item in en)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    Write(sb, item);
                }
                sb.Append(']');
                return;
            }
            WriteString(sb, o.ToString());
        }

        static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        // ------------------------------------------------------------ 解析
        public static object Parse(string s)
        {
            if (s == null) return null;
            int i = 0;
            return ParseValue(s, ref i);
        }

        static void Skip(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r')) i++;
        }

        static object ParseValue(string s, ref int i)
        {
            Skip(s, ref i);
            if (i >= s.Length) return null;
            char c = s[i];
            if (c == '{') return ParseObject(s, ref i);
            if (c == '[') return ParseArray(s, ref i);
            if (c == '"') return ParseString(s, ref i);
            if (c == 't') { i += 4; return true; }
            if (c == 'f') { i += 5; return false; }
            if (c == 'n') { i += 4; return null; }
            return ParseNumber(s, ref i);
        }

        static Dictionary<string, object> ParseObject(string s, ref int i)
        {
            var d = new Dictionary<string, object>();
            i++; // {
            Skip(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return d; }
            while (i < s.Length)
            {
                Skip(s, ref i);
                var key = ParseString(s, ref i);
                Skip(s, ref i);
                if (i < s.Length && s[i] == ':') i++;
                d[key] = ParseValue(s, ref i);
                Skip(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == '}') { i++; break; }
                break;
            }
            return d;
        }

        static object[] ParseArray(string s, ref int i)
        {
            var list = new List<object>();
            i++; // [
            Skip(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return list.ToArray(); }
            while (i < s.Length)
            {
                list.Add(ParseValue(s, ref i));
                Skip(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == ']') { i++; break; }
                break;
            }
            return list.ToArray();
        }

        static string ParseString(string s, ref int i)
        {
            if (i >= s.Length || s[i] != '"') return "";
            i++;
            var sb = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') break;
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) break;
                char e = s[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u':
                        if (i + 4 <= s.Length)
                        {
                            sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16));
                            i += 4;
                        }
                        break;
                    default: sb.Append(e); break;
                }
            }
            return sb.ToString();
        }

        static object ParseNumber(string s, ref int i)
        {
            int start = i;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '-' || s[i] == '+' || s[i] == '.' || s[i] == 'e' || s[i] == 'E')) i++;
            var t = s.Substring(start, i - start);
            long l;
            if (long.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out l)) return l;
            double d;
            if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
            return 0L;
        }
    }
}
