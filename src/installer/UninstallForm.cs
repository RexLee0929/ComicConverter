using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace ComicConverterSetup
{
    /// <summary>
    /// 卸载向导：确认 → （条件）X 正在运行 → 卸载中 → 完成。外观与安装向导同一套。
    /// 占用处理与安装侧完全同款：不弹 MessageBox，切页给选项（规范 §三.1.6 / §五 1b）。
    /// </summary>
    public class UninstallForm : WizardShell
    {
        const int PageConfirm = 0, PageBlocked = 1;

        readonly string _dir;
        bool _busy, _removed, _failed, _blocked;
        bool _keepConfig;
        string _blockedPath;

        CheckBox _cbKeep;
        ProgressStrip _bar;
        Label _status;

        public UninstallForm(string dir)
            : base("卸载 " + Program.AppName, 640, 400, "从计算机中移除程序")
        {
            _dir = string.IsNullOrEmpty(dir) ? Program.DefaultDir() : dir;
            FormClosing += delegate { if (_removed) Program.ScheduleSelfDelete(); };
        }

        protected override void BuildPages()
        {
            NextBtn.Click += delegate { OnNext(); };
            Back.Click += delegate { OnBack(); };
            CancelBtn.Click += delegate { if (!_busy) Close(); };
        }

        protected override void ShowPage(int index)
        {
            if (index == PageBlocked) { ShowBlocked(_blockedPath); return; }
            BuildConfirm();
        }

        /// <summary>
        /// 确认页是第一页，打开向导永远先落在这里 —— 不在这里检测占用。
        /// 否则程序开着时一打开向导就直接被切到「正在运行」页，用户连卸载内容和
        /// 「保留配置文件」的选项都没看见（规范 §五 1b：点「卸载」时才检测）。
        /// </summary>
        void BuildConfirm()
        {
            _blocked = false;
            Body.Controls.Clear();
            TitleLabel.Text = "卸载 " + Program.AppName;

            Back.Visible = false;            // 确认页是第一页，没有「上一步」
            CancelBtn.Visible = true;
            CancelBtn.Enabled = true;
            CancelBtn.Text = "取消";
            NextBtn.Visible = true;
            NextBtn.Text = "卸载";
            NextBtn.Enabled = true;
            LayoutButtons(86);

            bool allUsers = Program.InstalledForAllUsers() || Program.IsUnderProgramFiles(_dir);

            L("安装程序将把 " + Program.AppName + " " + Program.Version
              + " 从您的计算机中移除（"
              + (allUsers ? "为所有用户安装" : "为当前用户安装") + "）。", 0, 0, 428, 24);

            Strong("安装位置：", 0, 34, 428, 22);
            L(_dir, 0, 56, 428, 20);

            Strong("将一并清理：", 0, 88, 428, 22);
            L("· 程序文件与 WebView2 缓存\n"
              + "· 桌面与开始菜单快捷方式\n"
              + "· 「设置 → 应用」里的卸载登记项", 0, 110, 428, 70);

            _cbKeep = Check("保留配置文件 config.json（重装后沿用当前设置）", 0, 190, 380, true);

            L("作者：" + Program.Author + "　　联系邮箱：" + Program.Email, 0, 224, 428, 20);
        }

        /// <summary>「X 正在运行」页：与安装侧同款，主按钮「结束进程并继续卸载」。</summary>
        void ShowBlocked(string path)
        {
            _blocked = true;
            _blockedPath = path;
            Body.Controls.Clear();

            TitleLabel.Text = Program.AppName + " 正在运行";

            Back.Visible = true;
            Back.Text = "上一步";
            Back.Enabled = true;
            CancelBtn.Visible = true;
            CancelBtn.Enabled = true;
            CancelBtn.Text = "取消";
            NextBtn.Visible = true;
            NextBtn.Enabled = true;
            NextBtn.Text = "结束进程并继续卸载";
            LayoutButtons(150);          // 文字长，默认 86px 会被折成两行

            Strong("需要删除的文件正被占用：", 0, 0, 428, 22);
            L(_dir, 0, 24, 428, 20);
            if (!string.IsNullOrEmpty(path))
            {
                var p = L(path, 0, 46, 428, 20);
                p.AutoEllipsis = true;      // 长路径单行裁切时给省略号，别硬截成半截
            }

            Strong("请选择一种处理方式：", 0, 80, 428, 22);
            L("· 点「结束进程并继续卸载」—— 自动关闭正在运行的 " + Program.AppName
              + "（未保存的操作会丢失），随后立即继续卸载；\n"
              + "· 点「上一步」—— 返回上一页确认卸载内容；\n"
              + "· 若上一步无效，请手动关闭 " + Program.AppName
              + "，再点「结束进程并继续卸载」重试。", 0, 104, 428, 90);
        }

        void OnBack()
        {
            if (_busy || _removed) return;

            // 从「正在运行」页返回确认页
            if (_blocked) { _blocked = false; _blockedPath = null; BuildConfirm(); return; }
            Close();
        }

        void OnNext()
        {
            if (_removed) { Close(); return; }
            if (_failed) { Close(); return; }
            if (_busy) return;

            // 占用页的主按钮：结束进程后接着卸
            if (_blocked) { EndRunningAndContinue(); return; }

            // 先把本页的选择记下来，占用页续卸时沿用（含「保留配置文件」的勾选）
            _keepConfig = _cbKeep != null && _cbKeep.Checked;

            // 程序还开着就切到占用页（不弹框），别等删到一半才失败
            if (Program.RunningInstances(_dir).Length > 0)
            {
                ShowBlocked(Program.ExePathIn(_dir));
                return;
            }

            StartUninstall();
        }

        /// <summary>结束占用进程后继续卸载；杀不掉就停在占用页，提示手动处理。</summary>
        void EndRunningAndContinue()
        {
            NextBtn.Enabled = false;
            int n = Program.KillRunning(_dir);

            if (Program.RunningInstances(_dir).Length > 0)
            {
                NextBtn.Enabled = true;
                MessageBox.Show(this,
                    "没能结束正在运行的 " + Program.AppName + "（权限不足或进程未响应）。\n\n"
                    + "请用任务管理器手动结束它，或重启计算机后再卸载。",
                    Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (n > 0) Thread.Sleep(300);   // 等文件句柄真正释放
            _blocked = false;
            _blockedPath = null;
            StartUninstall();
        }

        void StartUninstall()
        {
            _busy = true;
            Body.Controls.Clear();
            TitleLabel.Text = "正在卸载";

            Back.Visible = false;
            CancelBtn.Visible = false;
            NextBtn.Enabled = false;

            L("正在从 " + _dir + " 移除 " + Program.AppName + "…", 0, 0, 428, 40);

            // 自绘进度条：跟随派生主色（系统 ProgressBar 是绿的，不跟主题）
            _bar = new ProgressStrip(K, 0, 60, 428, 10);
            Body.Controls.Add(_bar);

            _status = L("准备中…", 0, 78, 428, 150);

            var dir = _dir;
            var keep = _keepConfig;
            var th = new Thread(delegate()
            {
                try
                {
                    int n = Program.RemoveAll(dir, keep, delegate(int pct, string msg)
                    {
                        Ui(delegate
                        {
                            if (_bar == null) return;
                            _bar.Value = Math.Min(100, pct);
                            _status.Text = msg + "…";
                        });
                    });
                    Ui(delegate { ShowFinish(n, keep); });
                }
                catch (Exception ex)
                {
                    Ui(delegate { ShowFailure(ex.Message); });
                }
            });
            th.IsBackground = true;
            th.Start();
        }

        void ShowFinish(int removed, bool keep)
        {
            _busy = false;
            _removed = true;
            Body.Controls.Clear();
            TitleLabel.Text = "正在完成 " + Program.AppName + " 卸载向导";

            Back.Visible = false;
            CancelBtn.Visible = false;
            NextBtn.Text = "关闭";
            NextBtn.Enabled = true;
            LayoutButtons(86);

            L(Program.AppName + " 已从您的计算机中移除。", 0, 0, 428, 24);
            L("共删除 " + removed + " 个文件。", 0, 32, 428, 20);

            if (keep)
            {
                // 文件名写进说明行：路径一长就会被省略号截掉尾巴，别让「保留了哪个文件」看不清
                L("配置文件已保留：config.json", 0, 62, 428, 20);
                var p = L(_dir, 0, 84, 428, 20);
                p.AutoEllipsis = true;
            }
            else
            {
                L("安装目录已清空。", 0, 62, 428, 20);
            }

            L("如果以后还要用，重新运行安装包即可。", 0, 118, 428, 20);
            L("作者：" + Program.Author + "　　联系邮箱：" + Program.Email, 0, 152, 428, 20);
        }

        void ShowFailure(string msg)
        {
            _busy = false;
            _failed = true;
            Body.Controls.Clear();
            TitleLabel.Text = "卸载未完全成功";

            Back.Visible = false;
            CancelBtn.Visible = false;
            NextBtn.Text = "关闭";
            NextBtn.Enabled = true;

            L("有些文件没能删除：\n\n" + msg
              + "\n\n可以稍后手动删除安装目录：\n" + _dir, 0, 0, 428, 140);
        }

        void Ui(Action a)
        {
            try { if (IsHandleCreated) BeginInvoke(a); } catch { }
        }
    }
}
