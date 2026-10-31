using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace ComicConverter
{
    public partial class App : Application
    {
        public static string LogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "error.log");

        static Mutex _mutex;

        /// <summary>「叫醒已有窗口」的信号：第二个实例只 Set 一下，不建任何窗口。</summary>
        static EventWaitHandle _activate;

        static string _instanceKey;

        protected override void OnStartup(StartupEventArgs e)
        {
            // 品牌色从图标取主色，先算好再开窗口
            Accent.Init();

            // 同一目录只允许跑一个实例：两个实例会同时读写 config.json，后写的覆盖前面的。
            // 已经在运行时**不弹「程序已在运行」**（规范 §七.6），改为把已有窗口叫到前面。
            if (!AcquireSingleInstance())
            {
                SignalExisting();
                Shutdown();
                return;
            }

            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs a)
            {
                Report(a.ExceptionObject as Exception, "AppDomain");
            };
            DispatcherUnhandledException += delegate(object s, DispatcherUnhandledExceptionEventArgs a)
            {
                Report(a.Exception, "Dispatcher");
                a.Handled = true;      // 吞掉异常不等于没事发生：状态栏红字 + error.log + 自绘错误框
            };
            try { base.OnStartup(e); }
            catch (Exception ex) { Dump("OnStartup: " + ex); throw; }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try { if (_activate != null) _activate.Dispose(); } catch { }
            try { if (_mutex != null) _mutex.ReleaseMutex(); } catch { }
            try { if (_mutex != null) _mutex.Dispose(); } catch { }
            base.OnExit(e);
        }

        /// <summary>互斥名按程序所在目录区分：免安装版和安装版可以各跑一个。</summary>
        static bool AcquireSingleInstance()
        {
            try
            {
                uint h = 2166136261;
                foreach (var c in AppDomain.CurrentDomain.BaseDirectory.ToLowerInvariant())
                {
                    h ^= c;
                    h *= 16777619;
                }
                _instanceKey = "Local\\ComicConverter_" + h.ToString("X8");

                bool owned;
                _mutex = new Mutex(true, _instanceKey, out owned);
                _activate = new EventWaitHandle(false, EventResetMode.AutoReset, _instanceKey + "_activate");

                if (owned) ListenForActivation();
                return owned;
            }
            catch { return true; }   // 拿不到互斥锁时不阻止启动
        }

        /// <summary>第二个实例：只发信号，让已有窗口自己到前面来。</summary>
        static void SignalExisting()
        {
            try
            {
                if (_activate != null) _activate.Set();
            }
            catch { }
        }

        static void ListenForActivation()
        {
            var ev = _activate;
            if (ev == null) return;

            var th = new Thread(delegate()
            {
                while (true)
                {
                    try
                    {
                        if (!ev.WaitOne()) continue;
                    }
                    catch { return; }

                    var app = Current;
                    if (app == null) return;
                    try
                    {
                        app.Dispatcher.BeginInvoke(new Action(BringToFront), DispatcherPriority.Normal);
                    }
                    catch { return; }
                }
            });
            th.IsBackground = true;
            th.Name = "activate-listener";
            th.Start();
        }

        /// <summary>
        /// 把主窗口提到前面并给一句状态栏提示。主窗口可能还没建好
        /// （双击得很快时信号先到），所以按 120ms 轮询最多等 5 秒。
        /// </summary>
        static void BringToFront()
        {
            int tries = 0;
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            t.Tick += delegate
            {
                var w = Current == null ? null : Current.MainWindow;
                if (w == null || !w.IsLoaded)
                {
                    if (++tries < 40) return;
                    t.Stop();
                    return;
                }
                t.Stop();

                if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
                w.Show();
                w.Activate();
                w.Topmost = true;      // 后台进程调 Activate 常被系统忽略，用一次置顶把窗口顶出来
                w.Topmost = false;
                w.Focus();

                var main = w as MainWindow;
                if (main != null) main.Toast("程序已经在一个窗口里打开了");
            };
            t.Start();
        }

        /// <summary>
        /// 未捕获异常的统一出口：写日志 + 交给主窗口做「状态栏红字 + 自绘错误框」。
        /// 主窗口还没建好（启动早期崩）时至少落一份 error.log，不静默退出。
        /// </summary>
        static void Report(Exception ex, string where)
        {
            var msg = ex == null ? "未知异常" : ex.ToString();
            Dump(where + ": " + msg);

            var app = Current;
            if (app == null) return;

            try
            {
                var w = app.MainWindow as MainWindow;
                if (w != null) w.ReportError(msg);
            }
            catch { }
        }

        public static void Dump(string msg)
        {
            try
            {
                File.AppendAllText(LogPath, DateTime.Now.ToString("HH:mm:ss") + "  " + msg + "\r\n\r\n");
            }
            catch { }
        }
    }
}
