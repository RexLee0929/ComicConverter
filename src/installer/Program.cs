using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ComicConverterSetup
{
    /// <summary>安装范围。对应 txtFormat 里 Inno 的「仅为我 / 为所有用户」选择。</summary>
    public enum InstallMode { PerUser, AllUsers }

    static class Program
    {
        /// <summary>程序名只用英文。</summary>
        public const string AppName = "ComicConverter";
        public const string AppId = "ComicConverter";
        public const string Tagline = "EPUB → CBZ 漫画库整理";
        public const string Author = "Rex Lee";
        public const string Email = "rexlee@rexleepro.com";

        public const string AppExe = "ComicConverter.exe";
        public const string ReadmeName = "README.md";

        const string UninstallSubKey =
            @"Software\Microsoft\Windows\CurrentVersion\Uninstall\ComicConverter";

        // ------------------------------------------------------------ 基本信息
        static string _version;

        public static string Version
        {
            get
            {
                if (_version == null)
                {
                    var asm = typeof(Program).Assembly;
                    var info = (AssemblyInformationalVersionAttribute)Attribute.GetCustomAttribute(
                        asm, typeof(AssemblyInformationalVersionAttribute));
                    if (info != null && !string.IsNullOrEmpty(info.InformationalVersion))
                    {
                        var v = info.InformationalVersion;
                        int plus = v.IndexOf('+');
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

        // ------------------------------------------------------------ 安装范围

        /// <summary>
        /// 目标目录里已装版本的对比结论。决定「选择安装位置」页的文案和「准备安装」页的行为判定。
        /// </summary>
        public enum UpgradeKind
        {
            /// <summary>目录是空的，没装过。</summary>
            Fresh,
            /// <summary>已装版本比安装包旧 —— 正常升级。</summary>
            Update,
            /// <summary>版本相同 —— 重装 / 修复。</summary>
            Same,
            /// <summary>已装版本比安装包新 —— 降级，要提醒用户。</summary>
            Downgrade,
            /// <summary>exe 在但版本读不出来（破解过 / 手工拷的 / 文件损坏）。</summary>
            Unknown
        }

        /// <summary>读目标目录里已安装程序的版本号，读不到返回 null。</summary>
        public static string InstalledVersion(string dir)
        {
            try
            {
                var exe = ExePathIn(dir);
                if (!File.Exists(exe)) return null;

                var vi = FileVersionInfo.GetVersionInfo(exe);
                if (!string.IsNullOrEmpty(vi.ProductVersion)) return vi.ProductVersion.Trim();
                if (!string.IsNullOrEmpty(vi.FileVersion)) return vi.FileVersion.Trim();
            }
            catch { }
            return null;
        }

        /// <summary>把 "1.2.3" / "1.2.3.0" 这类字符串解析成可比较的版本；解析失败返回 null。</summary>
        static Version ParseVersion(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;

            // 只取开头的数字和点，丢掉 "1.2.3-beta" 这类后缀
            var sb = new StringBuilder();
            foreach (var c in s.Trim())
            {
                if (char.IsDigit(c) || c == '.') sb.Append(c);
                else break;
            }

            var text = sb.ToString().Trim('.');
            if (text.Length == 0) return null;

            // 补齐到三段，"1.2" -> "1.2.0"，否则 Version 解析会报错
            var parts = text.Split('.');
            if (parts.Length == 1) text += ".0.0";
            else if (parts.Length == 2) text += ".0";

            try { return new Version(text); }
            catch { return null; }
        }

        /// <summary>判定本次安装属于全新安装 / 更新 / 重装 / 降级。</summary>
        public static UpgradeKind DetectUpgrade(string dir, out string installed)
        {
            installed = null;
            try
            {
                if (!File.Exists(ExePathIn(dir))) return UpgradeKind.Fresh;
            }
            catch { return UpgradeKind.Fresh; }

            installed = InstalledVersion(dir);
            var oldV = ParseVersion(installed);
            var newV = ParseVersion(Version);

            if (oldV == null || newV == null) return UpgradeKind.Unknown;

            int cmp = oldV.CompareTo(newV);
            if (cmp < 0) return UpgradeKind.Update;
            if (cmp == 0) return UpgradeKind.Same;
            return UpgradeKind.Downgrade;
        }

        public static InstallMode Mode = InstallMode.PerUser;

        public static bool IsAllUsers { get { return Mode == InstallMode.AllUsers; } }

        static bool? _elevated;

        /// <summary>当前进程是否已提权（管理员）。</summary>
        public static bool IsElevated
        {
            get
            {
                if (_elevated == null)
                {
                    try
                    {
                        using (var id = WindowsIdentity.GetCurrent())
                            _elevated = new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
                    }
                    catch { _elevated = false; }
                }
                return _elevated.Value;
            }
        }

        /// <summary>64 位 Program Files，避免 32 位进程拿到 (x86) 那个。</summary>
        public static string ProgramFilesDir
        {
            get
            {
                var pf = Environment.GetEnvironmentVariable("ProgramW6432");
                if (string.IsNullOrEmpty(pf))
                    pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                return pf;
            }
        }

        /// <summary>与 Inno 的 {autopf} 一致：按安装范围给不同的默认目录。</summary>
        public static string DefaultDir(InstallMode mode)
        {
            if (mode == InstallMode.AllUsers)
                return Path.Combine(ProgramFilesDir, AppId);

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", AppId);
        }

        public static string DefaultDir() { return DefaultDir(Mode); }

        public static string ExePathIn(string dir) { return Path.Combine(dir, AppExe); }
        public static string ReadmePathIn(string dir) { return Path.Combine(dir, ReadmeName); }

        /// <summary>用户配置文件。安装时不覆盖（payload 里没有它），卸载时默认保留。</summary>
        public const string ConfigName = "config.json";

        public static string ConfigPathIn(string dir) { return Path.Combine(dir, ConfigName); }

        /// <summary>目标目录里是否已有用户配置 —— 决定「选择安装位置」页要不要显示覆盖选项。</summary>
        public static bool HasConfig(string dir)
        {
            try
            {
                if (string.IsNullOrEmpty(dir) || !Path.IsPathRooted(dir)) return false;
                return File.Exists(ConfigPathIn(dir));
            }
            catch { return false; }
        }

        public static bool IsUnderProgramFiles(string dir)
        {
            try
            {
                var pf = ProgramFilesDir;
                return !string.IsNullOrEmpty(pf)
                       && dir.StartsWith(pf, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        // ------------------------------------------------------------ 目录位置
        /// <summary>桌面可能被 OneDrive 重定向，必须读注册表拿真实路径。</summary>
        public static string DesktopDir(bool allUsers)
        {
            if (allUsers)
            {
                var common = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
                if (!string.IsNullOrEmpty(common)) return common;
            }

            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders"))
                {
                    var v = k == null ? null : k.GetValue("Desktop") as string;
                    if (!string.IsNullOrEmpty(v))
                    {
                        var p = Environment.ExpandEnvironmentVariables(v);
                        if (Directory.Exists(p)) return p;
                    }
                }
            }
            catch { }

            var d = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (!Directory.Exists(d))
            {
                try { Directory.CreateDirectory(d); } catch { }
            }
            return d;
        }

        public static string StartMenuDir(bool allUsers)
        {
            var f = allUsers
                ? Environment.SpecialFolder.CommonPrograms
                : Environment.SpecialFolder.Programs;
            return Environment.GetFolderPath(f);
        }

        // ------------------------------------------------------------ 注册表
        static RegistryKey OpenUninstallKey(bool allUsers, bool writable)
        {
            var hive = allUsers ? RegistryHive.LocalMachine : RegistryHive.CurrentUser;
            var view = (allUsers && Environment.Is64BitOperatingSystem)
                ? RegistryView.Registry64
                : RegistryView.Default;

            var root = RegistryKey.OpenBaseKey(hive, view);
            return writable ? root.CreateSubKey(UninstallSubKey) : root.OpenSubKey(UninstallSubKey);
        }

        /// <summary>已安装的目录：先找「所有用户」，再找「当前用户」。</summary>
        public static string InstalledDir()
        {
            foreach (var all in new[] { true, false })
            {
                try
                {
                    using (var k = OpenUninstallKey(all, false))
                    {
                        var v = k == null ? null : k.GetValue("InstallLocation") as string;
                        if (!string.IsNullOrEmpty(v)) return v;
                    }
                }
                catch { }
            }
            return null;
        }

        public static bool InstalledForAllUsers()
        {
            try
            {
                using (var k = OpenUninstallKey(true, false)) return k != null;
            }
            catch { return false; }
        }

        static void WriteUninstallEntry(string dir, InstallMode mode)
        {
            try
            {
                using (var k = OpenUninstallKey(mode == InstallMode.AllUsers, true))
                {
                    if (k == null) return;
                    k.SetValue("DisplayName", AppName);
                    k.SetValue("DisplayVersion", Version);
                    k.SetValue("Publisher", Author);
                    k.SetValue("InstallLocation", dir);
                    k.SetValue("DisplayIcon", ExePathIn(dir));
                    k.SetValue("UninstallString", "\"" + Path.Combine(dir, "uninstall.exe") + "\"");
                    k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
                    k.SetValue("EstimatedSize", DirSizeKb(dir), RegistryValueKind.DWord);
                    k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                    k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                }
            }
            catch { }
        }

        static void RemoveUninstallEntries()
        {
            foreach (var all in new[] { true, false })
            {
                try
                {
                    var hive = all ? RegistryHive.LocalMachine : RegistryHive.CurrentUser;
                    var view = (all && Environment.Is64BitOperatingSystem)
                        ? RegistryView.Registry64
                        : RegistryView.Default;

                    using (var root = RegistryKey.OpenBaseKey(hive, view))
                    {
                        if (root.OpenSubKey(UninstallSubKey) != null)
                            root.DeleteSubKeyTree(UninstallSubKey, false);
                    }
                }
                catch { }
            }
        }

        static int DirSizeKb(string dir)
        {
            try
            {
                long sum = 0;
                foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                {
                    try { sum += new FileInfo(f).Length; } catch { }
                }
                return (int)Math.Min(int.MaxValue, sum / 1024);
            }
            catch { return 4096; }
        }

        // ------------------------------------------------------------ 入口
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main(string[] args)
        {
            // 必须在建任何窗口前声明 DPI 感知，否则 150% 缩放下窗口会被系统按比例缩小
            try { SetProcessDPIAware(); } catch { }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // 卸载：先复制到临时目录，再从临时目录执行，才能真正删掉安装目录
            int runIdx = Array.IndexOf(args, "--uninstall-run");
            if (runIdx >= 0 && args.Length > runIdx + 1)
            {
                Application.Run(new UninstallForm(args[runIdx + 1]));
                return;
            }

            var self = Path.GetFileNameWithoutExtension(Application.ExecutablePath);
            bool isUninstall = self.Equals("uninstall", StringComparison.OrdinalIgnoreCase)
                               || (args.Length > 0 && args[0] == "--uninstall");

            if (isUninstall)
            {
                RelaunchForUninstall();
                return;
            }

            // 提权重启后的实例：安装范围已经确定
            if (Array.IndexOf(args, "--all-users") >= 0) Mode = InstallMode.AllUsers;

            if (args.Length > 0 && args[0] == "--silent")
            {
                try
                {
                    DoInstall(DefaultDir(), Mode, true, true, false, null);
                    Environment.Exit(0);
                }
                catch { Environment.Exit(1); }
                return;
            }

            Application.Run(new InstallForm());
        }

        // ------------------------------------------------------------ 提权
        /// <summary>以管理员身份重新启动自己。用户取消 UAC 时返回 false。</summary>
        public static bool RelaunchAsAdmin(string arguments)
        {
            try
            {
                Process.Start(new ProcessStartInfo(Application.ExecutablePath, arguments)
                {
                    UseShellExecute = true,
                    Verb = "runas"
                });
                return true;
            }
            catch { return false; }
        }

        static void RelaunchForUninstall()
        {
            var dir = InstalledDir();
            if (string.IsNullOrEmpty(dir))
                dir = Path.GetDirectoryName(Application.ExecutablePath);

            bool needAdmin = (InstalledForAllUsers() || IsUnderProgramFiles(dir)) && !IsElevated;

            try
            {
                var tmp = Path.Combine(Path.GetTempPath(),
                    "ComicConverter-uninstall-" + Process.GetCurrentProcess().Id + ".exe");
                File.Copy(Application.ExecutablePath, tmp, true);

                var psi = new ProcessStartInfo(tmp, "--uninstall-run \"" + dir + "\"")
                {
                    UseShellExecute = true
                };
                if (needAdmin) psi.Verb = "runas";   // 删 Program Files / HKLM 需要提权

                Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageBox.Show("无法启动卸载程序：\n" + ex.Message, AppName,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ------------------------------------------------------------ 安装
        /// <param name="resetConfig">用户勾了「覆盖现有配置」：装完删掉 config.json，程序下次启动重建默认值。</param>
        public static void DoInstall(string dir, InstallMode mode, bool desktopShortcut,
                                     bool startMenuShortcut, bool resetConfig,
                                     Action<int, string> progress)
        {
            Directory.CreateDirectory(dir);

            var asm = Assembly.GetExecutingAssembly();
            using (var s = asm.GetManifestResourceStream("payload.zip"))
            {
                if (s == null) throw new Exception("安装包数据缺失（payload.zip 未嵌入）");

                using (var zip = new ZipArchive(s, ZipArchiveMode.Read))
                {
                    int total = 0;
                    foreach (var e in zip.Entries)
                        if (!e.FullName.EndsWith("/")) total++;

                    int i = 0;
                    foreach (var e in zip.Entries)
                    {
                        if (e.FullName.EndsWith("/")) continue;

                        var target = Path.Combine(dir, e.FullName.Replace('/', '\\'));
                        var d = Path.GetDirectoryName(target);
                        if (!string.IsNullOrEmpty(d)) Directory.CreateDirectory(d);

                        e.ExtractToFile(target, true);

                        i++;
                        if (progress != null)
                            progress(total == 0 ? 100 : i * 100 / total, e.Name);
                    }
                }
            }

            // 用户勾了「覆盖现有配置」才删；不勾时 payload 里也没有 config.json，天然不碰它
            if (resetConfig)
            {
                try
                {
                    var cfg = ConfigPathIn(dir);
                    if (File.Exists(cfg)) File.Delete(cfg);
                }
                catch { }
            }

            // 使用说明随程序一起装进去，完成页可以勾选查看
            try
            {
                File.WriteAllText(ReadmePathIn(dir), Readme, new UTF8Encoding(false));
            }
            catch { }

            // 卸载入口：把安装器自身复制成 uninstall.exe
            try
            {
                var self = Application.ExecutablePath;
                var dst = Path.Combine(dir, "uninstall.exe");
                if (!string.Equals(self, dst, StringComparison.OrdinalIgnoreCase))
                    File.Copy(self, dst, true);
            }
            catch { }

            bool allUsers = mode == InstallMode.AllUsers;
            if (desktopShortcut)
                CreateShortcut(Path.Combine(DesktopDir(allUsers), AppName + ".lnk"), dir);
            if (startMenuShortcut)
                CreateShortcut(Path.Combine(StartMenuDir(allUsers), AppName + ".lnk"), dir);

            WriteUninstallEntry(dir, mode);
        }

        static void CreateShortcut(string lnkPath, string targetDir)
        {
            try
            {
                var d = Path.GetDirectoryName(lnkPath);
                if (!string.IsNullOrEmpty(d) && !Directory.Exists(d)) Directory.CreateDirectory(d);

                var t = Type.GetTypeFromProgID("WScript.Shell");
                if (t == null) return;
                var shell = Activator.CreateInstance(t);
                var lnk = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell,
                    new object[] { lnkPath });
                var lt = lnk.GetType();
                var exe = ExePathIn(targetDir);
                lt.InvokeMember("TargetPath", BindingFlags.SetProperty, null, lnk, new object[] { exe });
                lt.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, lnk, new object[] { targetDir });
                lt.InvokeMember("IconLocation", BindingFlags.SetProperty, null, lnk, new object[] { exe + ",0" });
                lt.InvokeMember("Description", BindingFlags.SetProperty, null, lnk, new object[] { AppName });
                lt.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
            }
            catch { }
        }

        // ------------------------------------------------------------ 卸载
        /// <summary>清理安装痕迹，返回删除的文件数。keepConfig 为 true 时保留 config.json。</summary>
        public static int RemoveAll(string dir, bool keepConfig, Action<int, string> progress)
        {
            KillRunning(dir);

            const int Steps = 4;
            int step = 0;
            Action<string> tick = delegate(string msg)
            {
                step++;
                if (progress != null) progress(step * 100 / Steps, msg);
            };

            // 1. 注册表（两边都试，能删的删）
            RemoveUninstallEntries();
            tick("清理注册表");

            // 2. 快捷方式：当前用户与所有用户的位置都清一遍
            foreach (var all in new[] { false, true })
            {
                TryDelete(Path.Combine(DesktopDir(all), AppName + ".lnk"));
                TryDelete(Path.Combine(StartMenuDir(all), AppName + ".lnk"));
            }
            tick("删除快捷方式");

            // 3. 安装目录
            int removed = 0;
            if (Directory.Exists(dir))
            {
                foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                {
                    if (keepConfig &&
                        Path.GetFileName(f).Equals("config.json", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (TryDelete(f)) removed++;
                }

                var dirs = Directory.GetDirectories(dir, "*", SearchOption.AllDirectories);
                Array.Sort(dirs, delegate(string a, string b) { return b.Length.CompareTo(a.Length); });
                foreach (var d in dirs)
                {
                    try { if (Directory.GetFileSystemEntries(d).Length == 0) Directory.Delete(d); }
                    catch { }
                }

                try
                {
                    if (Directory.GetFileSystemEntries(dir).Length == 0) Directory.Delete(dir);
                }
                catch { }
            }
            tick("删除程序文件");

            // 4. WebView2 缓存（不放配置，删掉没关系）
            try
            {
                var cache = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    AppId, "WebView2");
                if (Directory.Exists(cache)) Directory.Delete(cache, true);
            }
            catch { }
            tick("清理缓存");

            return removed;
        }

        static bool TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) { File.Delete(path); return true; }
            }
            catch { }
            return false;
        }

        /// <summary>在指定目录下运行的实例。拿不到模块路径的也一并算上，宁可多提示，也不能等写文件时才炸。</summary>
        public static Process[] RunningInstances(string dir)
        {
            var list = new List<Process>();
            try
            {
                var root = string.IsNullOrEmpty(dir) ? null : dir.TrimEnd('\\') + "\\";
                foreach (var p in Process.GetProcessesByName(AppId))
                {
                    string exe = null;
                    try { exe = p.MainModule == null ? null : p.MainModule.FileName; } catch { }

                    // 权限不足或跨位数时 MainModule 会抛，此时按「在运行」处理
                    if (root == null || string.IsNullOrEmpty(exe)
                        || exe.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                        list.Add(p);
                }
            }
            catch { }
            return list.ToArray();
        }

        /// <summary>结束安装目录下运行的实例，返回结束掉的个数。安装/卸载共用。</summary>
        public static int KillRunning(string dir)
        {
            int n = 0;
            foreach (var p in RunningInstances(dir))
            {
                try
                {
                    p.Kill();
                    p.WaitForExit(3000);
                    n++;
                }
                catch { }
            }
            return n;
        }

        /// <summary>异常消息是不是「文件被其它进程占用」。中英文 Windows 的措辞都认。</summary>
        public static bool LooksLikeFileInUse(string msg)
        {
            if (string.IsNullOrEmpty(msg)) return false;
            return msg.IndexOf("正由另一进程使用", StringComparison.Ordinal) >= 0
                || msg.IndexOf("另一个程序正在使用", StringComparison.Ordinal) >= 0
                || msg.IndexOf("being used by another process", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>从异常消息里抠出被占用的文件路径，取不到就返回 null。</summary>
        public static string QuotedPath(string msg)
        {
            if (string.IsNullOrEmpty(msg)) return null;
            int a = msg.IndexOf('"');
            if (a < 0) return null;
            int b = msg.IndexOf('"', a + 1);
            if (b < 0) return null;
            return msg.Substring(a + 1, b - a - 1);
        }

        /// <summary>临时副本用完把自己删掉。</summary>
        public static void ScheduleSelfDelete()
        {
            try
            {
                var self = Application.ExecutablePath;
                var tmp = Path.GetTempPath();
                if (!self.StartsWith(tmp, StringComparison.OrdinalIgnoreCase)) return;

                Process.Start(new ProcessStartInfo("cmd.exe",
                    "/c timeout /t 2 >nul & del /f /q \"" + self + "\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                });
            }
            catch { }
        }

        public static void Launch(string dir)
        {
            try { Process.Start(ExePathIn(dir)); } catch { }
        }

        public static void OpenReadme(string dir)
        {
            try
            {
                var p = ReadmePathIn(dir);
                if (File.Exists(p)) Process.Start(new ProcessStartInfo(p) { UseShellExecute = true });
            }
            catch { }
        }

        // ------------------------------------------------------------ 内嵌文本
        static string _license;

        /// <summary>MIT 许可原文，安装时展示。填进 TextBox 前统一成 CRLF 行尾（规范 §三）。</summary>
        public static string License
        {
            get
            {
                if (_license == null) _license = ToCrlf(Resource("LICENSE", "MIT License"));
                return _license;
            }
        }

        /// <summary>行尾统一成 \r\n。WinForms 的 TextBox 只认 CRLF，
        /// 内嵌的 LICENSE 常是 LF 行尾，直接塞会整篇挤成一段。</summary>
        public static string ToCrlf(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n");
        }

        static string _readme;

        /// <summary>使用说明，安装时写进安装目录。</summary>
        public static string Readme
        {
            get
            {
                if (_readme == null) _readme = Resource("README", "# " + AppName);
                return _readme;
            }
        }

        static string Resource(string name, string fallback)
        {
            try
            {
                using (var s = typeof(Program).Assembly.GetManifestResourceStream(name))
                {
                    if (s == null) return fallback + "\r\n";
                    using (var r = new StreamReader(s, Encoding.UTF8))
                        return r.ReadToEnd();
                }
            }
            catch { return fallback + "\r\n"; }
        }
    }
}
