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

        protected override void OnStartup(StartupEventArgs e)
        {
            // 品牌色从图标取主色，先算好再开窗口
            Accent.Init();

            // 同一目录只允许跑一个实例：两个实例会同时读写 config.json，后写的覆盖前面的。
            if (!AcquireSingleInstance())
            {
                MessageBox.Show(
                    Brand.Name + " 已经在运行了。\n\n请切换到已经打开的窗口。",
                    Brand.Name, MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }

            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs a)
            {
                Dump("AppDomain: " + a.ExceptionObject);
            };
            DispatcherUnhandledException += delegate(object s, DispatcherUnhandledExceptionEventArgs a)
            {
                Dump("Dispatcher: " + a.Exception);
                a.Handled = true;
            };
            try { base.OnStartup(e); }
            catch (Exception ex) { Dump("OnStartup: " + ex); throw; }
        }

        protected override void OnExit(ExitEventArgs e)
        {
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
                bool owned;
                _mutex = new Mutex(true, "Local\\ComicConverter_" + h.ToString("X8"), out owned);
                return owned;
            }
            catch { return true; }   // 拿不到互斥锁时不阻止启动
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
