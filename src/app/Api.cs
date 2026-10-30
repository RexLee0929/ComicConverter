using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace ComicConverter
{
    /// <summary>本地 HTTP 服务（TcpListener 实现，避免 HttpListener 的 URL ACL 限制）。</summary>
    public static class Api
    {
        public static int Port = 8765;
        public static string ConfigPath;

        static TcpListener _tcp;
        static Thread _accept;
        static Thread _worker;
        static volatile bool _stop;
        static readonly object Sync = new object();

        static readonly List<Dictionary<string, object>> Logs = new List<Dictionary<string, object>>();
        static bool _running;
        static string _phase = "待命";
        static int _total, _done, _ok, _err, _skip;
        static string _current = "";

        public static Dictionary<string, object> DefaultConfig()
        {
            return new Dictionary<string, object>
            {
                {"sources", new object[0]},
                {"out", ""},
                {"target", "both"},
                {"reading", "auto"},
                {"t2s", "tw2s"},
                {"compress", "store"},
                {"special", "series"},
                {"special_suffix", "番外"},
                {"name_template", "{series} v{vol:02d}"},
                {"name_template_komga", "{series} #{vol3}"},
                {"jobs", 6},
                {"overwrite", false},
                {"drop_junk", true},
                {"keep_label", true},
                {"t2s_label", true},
                {"limit", 0},
                {"mode", "convert"}
            };
        }

        public static Dictionary<string, object> LoadConfig()
        {
            var cfg = DefaultConfig();
            try
            {
                if (ConfigPath != null && File.Exists(ConfigPath))
                {
                    var d = MiniJson.Parse(File.ReadAllText(ConfigPath, Encoding.UTF8)) as Dictionary<string, object>;
                    if (d != null) foreach (var kv in d) cfg[kv.Key] = kv.Value;
                }
            }
            catch { }
            return cfg;
        }

        public static bool SaveConfig(Dictionary<string, object> cfg)
        {
            try
            {
                if (ConfigPath == null) return false;
                File.WriteAllText(ConfigPath, MiniJson.Serialize(cfg), new UTF8Encoding(false));
                return true;
            }
            catch { return false; }
        }

        static void Log(string msg, string level)
        {
            lock (Sync)
            {
                Logs.Add(new Dictionary<string, object>
                {
                    {"t", DateTime.Now.ToString("HH:mm:ss")},
                    {"m", msg != null && msg.Length > 400 ? msg.Substring(0, 400) : (msg ?? "")},
                    {"l", level}
                });
                if (Logs.Count > 900) Logs.RemoveRange(0, 300);
            }
        }

        static void ResetState()
        {
            lock (Sync)
            {
                _running = true; _phase = "准备中";
                _total = 0; _done = 0; _ok = 0; _err = 0; _skip = 0;
                _current = ""; Logs.Clear();
            }
        }

        static void Bump(string field)
        {
            lock (Sync)
            {
                if (field == "ok") _ok++;
                else if (field == "err") _err++;
                else if (field == "skip") _skip++;
                _done++;
            }
        }

        static void Finish(string phase)
        {
            lock (Sync) { _running = false; _phase = phase; }
        }

        public static Dictionary<string, object> Status()
        {
            lock (Sync)
            {
                return new Dictionary<string, object>
                {
                    {"running", _running}, {"phase", _phase},
                    {"total", _total}, {"done", _done},
                    {"ok", _ok}, {"err", _err}, {"skip", _skip},
                    {"current", _current},
                    {"log", Logs.ToArray()}
                };
            }
        }

        // ---------------------------------------------------------------- 任务
        static void RunConvert(Dictionary<string, object> cfg)
        {
            try
            {
                ResetState();
                Log("扫描源目录…", "i");
                var errors = new List<string>();
                var items = Core.ScanSources(cfg, errors, delegate(string m) { Log(m, "i"); });
                lock (Sync) { _total = items.Count; _phase = "转换中"; }
                foreach (var e in errors.Take(80)) Log("跳过 " + e, "w");
                if (items.Count == 0) { Finish("没有可转换的文件"); return; }

                int jobs = Math.Max(1, Core.GetInt(cfg, "jobs", 6));
                Log("共 " + items.Count + " 个文件，" + jobs + " 并发", "i");

                int idx = -1;
                var threads = new List<Thread>();
                for (int t = 0; t < jobs; t++)
                {
                    var th = new Thread(delegate()
                    {
                        while (true)
                        {
                            int i = Interlocked.Increment(ref idx);
                            if (i >= items.Count || _stop) break;
                            var it = items[i];
                            try
                            {
                                var r = Core.ConvertOne(it, cfg, null);
                                if (r.Skip) Bump("skip");
                                else if (r.Err != null) { Bump("err"); Log("✗ " + Path.GetFileName(it.Src) + "：" + r.Err, "e"); }
                                else
                                {
                                    Bump("ok");
                                    lock (Sync) { _current = r.Series + " v" + r.Vol; }
                                    Log("✓ " + Path.GetFileName(r.Dst) + "  " + r.Mb + " MB", "i");
                                }
                            }
                            catch (Exception ex)
                            {
                                Bump("err");
                                Log("✗ " + Path.GetFileName(it.Src) + "：" + ex.Message, "e");
                            }
                        }
                    });
                    th.IsBackground = true;
                    threads.Add(th);
                    th.Start();
                }
                foreach (var th in threads) th.Join();

                int ok, er, sk;
                lock (Sync) { ok = _ok; er = _err; sk = _skip; }
                Log("完成：成功 " + ok + "，跳过 " + sk + "，失败 " + er, er == 0 ? "s" : "w");
                Finish(_stop ? "已停止" : "完成");
            }
            catch (Exception ex)
            {
                Log("致命错误：" + ex.Message, "e");
                Finish("出错");
            }
        }

        static void RunRepack(Dictionary<string, object> cfg)
        {
            try
            {
                ResetState();
                var files = new List<string>();
                foreach (var s in Core.GetList(cfg, "sources"))
                    if (Directory.Exists(s))
                        files.AddRange(Directory.GetFiles(s, "*.cbz", SearchOption.AllDirectories).OrderBy(x => x));

                lock (Sync) { _total = files.Count; _phase = "互转中"; }
                Log("找到 " + files.Count + " 个 CBZ → 目标 " + Core.GetStr(cfg, "target", "both"), "i");
                if (files.Count == 0) { Finish("没有 CBZ"); return; }

                foreach (var f in files)
                {
                    if (_stop) break;
                    try
                    {
                        var r = Repack.RepackOne(f, cfg);
                        if (r == "skip" || r == "same") { Bump("skip"); continue; }
                        if (r != null) { Bump("err"); Log("✗ " + Path.GetFileName(f) + "：" + r, "e"); continue; }
                        Bump("ok");
                        Log("✓ " + Path.GetFileName(f), "i");
                    }
                    catch (Exception ex)
                    {
                        Bump("err");
                        Log("✗ " + Path.GetFileName(f) + "：" + ex.Message, "e");
                    }
                }
                int ok, er, sk;
                lock (Sync) { ok = _ok; er = _err; sk = _skip; }
                Log("完成：成功 " + ok + "，跳过 " + sk + "，失败 " + er, er == 0 ? "s" : "w");
                Finish("完成");
            }
            catch (Exception ex)
            {
                Log("致命错误：" + ex.Message, "e");
                Finish("出错");
            }
        }

        static Dictionary<string, object> RunPreview(Dictionary<string, object> cfg)
        {
            var errors = new List<string>();
            var items = Core.ScanSources(cfg, errors, delegate(string m) { Log(m, "i"); });
            var rows = new List<object>();
            foreach (var it in items)
            {
                rows.Add(new Dictionary<string, object>
                {
                    {"src", Path.GetFileName(it.Src)},
                    {"folder", Core.SeriesFolder(it, cfg)},
                    {"file", Core.BuildFileName(it, cfg)},
                    {"pages", it.Pages},
                    {"vol", it.Vol ?? 0},
                    {"special", it.IsSpecial},
                    {"dir", it.Reading},
                    {"country", it.Country},
                    {"origin", it.CountrySource}
                });
            }
            return new Dictionary<string, object> { { "rows", rows }, { "errors", errors } };
        }

        // ---------------------------------------------------------------- TCP HTTP
        class Req
        {
            public string Method = "";
            public string Path = "";
            public string Body = "";
        }

        public static bool Start()
        {
            for (int i = 0; i < 40; i++)
            {
                try
                {
                    _tcp = new TcpListener(IPAddress.Loopback, Port);
                    _tcp.Start();
                    _accept = new Thread(AcceptLoop) { IsBackground = true };
                    _accept.Start();
                    return true;
                }
                catch { Port++; }
            }
            return false;
        }

        public static void Stop()
        {
            _stop = true;
            try { if (_tcp != null) _tcp.Stop(); } catch { }
        }

        static void AcceptLoop()
        {
            while (true)
            {
                TcpClient c;
                try { c = _tcp.AcceptTcpClient(); }
                catch { return; }
                var t = new Thread(delegate() { Serve(c); }) { IsBackground = true };
                t.Start();
            }
        }

        static void Serve(TcpClient client)
        {
            try
            {
                using (client)
                using (var ns = client.GetStream())
                {
                    ns.ReadTimeout = 120000;
                    ns.WriteTimeout = 120000;
                    var req = ReadRequest(ns);
                    if (req == null) return;

                    int code; string body, type;
                    Route(req, out code, out body, out type);
                    WriteResponse(ns, code, body, type);
                }
            }
            catch (Exception ex)
            {
                // 单个请求出错不能连累整个服务，但要把原因留在日志里
                Log("请求处理失败：" + ex.Message, "e");
            }
        }

        static Req ReadRequest(NetworkStream ns)
        {
            var buf = new List<byte>(4096);
            var one = new byte[1];
            while (true)
            {
                int n;
                try { n = ns.Read(one, 0, 1); }
                catch { return null; }
                if (n <= 0) return null;
                buf.Add(one[0]);
                int c = buf.Count;
                if (c >= 4 && buf[c - 4] == 13 && buf[c - 3] == 10 && buf[c - 2] == 13 && buf[c - 1] == 10) break;
                if (c > 32768) return null;
            }

            var head = Encoding.UTF8.GetString(buf.ToArray());
            var lines = head.Split(new[] { "\r\n" }, StringSplitOptions.None);
            var req = new Req();
            var first = lines[0].Split(' ');
            if (first.Length >= 2) { req.Method = first[0].ToUpperInvariant(); req.Path = first[1]; }

            int len = 0;
            for (int i = 1; i < lines.Length; i++)
            {
                var idx = lines[i].IndexOf(':');
                if (idx <= 0) continue;
                if (lines[i].Substring(0, idx).Trim().Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                    int.TryParse(lines[i].Substring(idx + 1).Trim(), out len);
            }

            if (len > 0)
            {
                var body = new byte[len];
                int read = 0;
                while (read < len)
                {
                    int n;
                    try { n = ns.Read(body, read, len - read); }
                    catch { break; }
                    if (n <= 0) break;
                    read += n;
                }
                req.Body = Encoding.UTF8.GetString(body, 0, read);
            }

            var q = req.Path.IndexOf('?');
            if (q >= 0) req.Path = req.Path.Substring(0, q);
            return req;
        }

        static void WriteResponse(NetworkStream ns, int code, string body, string type)
        {
            var bytes = Encoding.UTF8.GetBytes(body ?? "");
            var head = "HTTP/1.1 " + code + " " +
                       (code == 200 ? "OK" : code == 404 ? "Not Found" : code == 409 ? "Conflict" : "Error") + "\r\n" +
                       "Content-Type: " + type + "\r\n" +
                       "Content-Length: " + bytes.Length + "\r\n" +
                       "Cache-Control: no-store\r\n" +
                       "Connection: close\r\n\r\n";
            var hb = Encoding.ASCII.GetBytes(head);
            ns.Write(hb, 0, hb.Length);
            ns.Write(bytes, 0, bytes.Length);
            ns.Flush();
        }

        static void Route(Req req, out int code, out string body, out string type)
        {
            code = 200; type = "application/json; charset=utf-8"; body = "{}";

            if (req.Method == "GET" && (req.Path == "/" || req.Path == "/index.html"))
            {
                type = "text/html; charset=utf-8";
                body = Page.Html;
                return;
            }
            if (req.Method == "GET" && req.Path == "/api/status")
            {
                body = MiniJson.Serialize(Status());
                return;
            }
            if (req.Method == "GET" && req.Path == "/api/version")
            {
                body = MiniJson.Serialize(new Dictionary<string, object>
                {
                    {"name", Brand.Name},
                    {"version", Brand.Version},
                    {"tagline", Brand.Tagline},
                    {"author", Brand.Author},
                    {"email", Brand.Email}
                });
                return;
            }
            if (req.Method == "GET" && req.Path == "/api/config")
            {
                body = MiniJson.Serialize(new Dictionary<string, object> { { "cfg", LoadConfig() } });
                return;
            }

            if (req.Method == "POST")
            {
                var data = MiniJson.Parse(req.Body) as Dictionary<string, object>;
                if (data == null) data = new Dictionary<string, object>();

                if (req.Path == "/api/config")
                {
                    Dictionary<string, object> cfg;
                    if (data.ContainsKey("cfg") && data["cfg"] is Dictionary<string, object>)
                        cfg = (Dictionary<string, object>)data["cfg"];
                    else cfg = data;
                    body = MiniJson.Serialize(new Dictionary<string, object> { { "ok", SaveConfig(cfg) } });
                    return;
                }
                if (req.Path == "/api/pick")
                {
                    body = MiniJson.Serialize(new Dictionary<string, object> { { "path", PickFolder() } });
                    return;
                }
                if (req.Path == "/api/copy")
                {
                    body = MiniJson.Serialize(new Dictionary<string, object>
                    {
                        {"ok", CopyText(Core.GetStr(data, "text", ""))}
                    });
                    return;
                }
                if (req.Path == "/api/preview")
                {
                    body = MiniJson.Serialize(RunPreview(data));
                    return;
                }
                if (req.Path == "/api/start")
                {
                    if (_running) { code = 409; body = "{\"err\":\"已有任务在跑\"}"; return; }
                    _stop = false;
                    SaveConfig(data);
                    var cfgCopy = data;
                    var mode = Core.GetStr(data, "mode", "convert");
                    _worker = new Thread(delegate() { if (mode == "repack") RunRepack(cfgCopy); else RunConvert(cfgCopy); });
                    _worker.IsBackground = true;
                    _worker.Start();
                    body = "{\"ok\":true}";
                    return;
                }
                if (req.Path == "/api/stop")
                {
                    _stop = true;
                    Log("收到停止指令…", "w");
                    body = "{\"ok\":true}";
                    return;
                }
            }

            code = 404;
            body = "{\"err\":\"not found\"}";
        }

        /// <summary>写系统剪贴板。剪贴板需要 STA 线程，这里单开一个再等它结束。</summary>
        public static bool CopyToClipboard(string text)
        {
            return CopyText(text);
        }

        static bool CopyText(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            if (text.Length > 4096) text = text.Substring(0, 4096);

            bool ok = false;
            var th = new Thread(delegate()
            {
                for (int i = 0; i < 4 && !ok; i++)
                {
                    try { System.Windows.Forms.Clipboard.SetText(text); ok = true; }
                    catch { Thread.Sleep(40); }   // 剪贴板被别的程序占用时重试
                }
            });
            th.SetApartmentState(ApartmentState.STA);
            th.IsBackground = true;
            th.Start();
            th.Join(2000);
            return ok;
        }

        static string PickFolder()
        {
            try
            {
                var dlg = new System.Windows.Forms.FolderBrowserDialog();
                dlg.Description = "选择目录";
                dlg.ShowNewFolderButton = true;
                var r = dlg.ShowDialog();
                return r == System.Windows.Forms.DialogResult.OK ? dlg.SelectedPath : "";
            }
            catch (Exception ex) { return "ERR:" + ex.Message; }
        }
    }
}
