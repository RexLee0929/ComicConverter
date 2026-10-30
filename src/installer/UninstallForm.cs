using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace ComicConverterSetup
{
    /// <summary>卸载向导：确认 → 卸载中 → 完成。外观与安装向导同一套。</summary>
    public class UninstallForm : WizardShell
    {
        readonly string _dir;
        bool _busy, _removed, _failed;
        bool _keepConfig;

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
            Back.Click += delegate { if (!_busy && !_removed) Close(); };
            CancelBtn.Click += delegate { if (!_busy) Close(); };
        }

        protected override void ShowPage(int index)
        {
            if (index == 0) BuildConfirm();
        }

        void BuildConfirm()
        {
            Body.Controls.Clear();
            TitleLabel.Text = "卸载 " + Program.AppName;

            Back.Visible = false;
            CancelBtn.Visible = true;
            CancelBtn.Enabled = true;
            NextBtn.Visible = true;
            NextBtn.Text = "卸载";
            NextBtn.Enabled = true;

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

        void OnNext()
        {
            if (_removed) { Close(); return; }
            if (_failed) { Close(); return; }
            if (_busy) return;

            // 程序还开着就先处理，别等删到一半才失败
            if (Program.RunningInstances(_dir).Length > 0)
            {
                var r = MessageBox.Show(this,
                    Program.AppName + " 正在运行，需要先关闭它才能删除程序文件。\n\n"
                    + "点「确定」自动结束它并继续卸载（未保存的操作会丢失）；\n"
                    + "点「取消」先自行处理，稍后再卸载。",
                    Program.AppName, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
                if (r != DialogResult.OK) return;

                Program.KillRunning(_dir);
                if (Program.RunningInstances(_dir).Length > 0)
                {
                    MessageBox.Show(this,
                        "没能结束正在运行的 " + Program.AppName + "（权限不足或进程未响应）。\n\n"
                        + "请用任务管理器手动结束它后再卸载。",
                        Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                Thread.Sleep(300);
            }

            _keepConfig = _cbKeep != null && _cbKeep.Checked;
            StartUninstall();
        }

        void StartUninstall()
        {
            _busy = true;
            Body.Controls.Clear();
            TitleLabel.Text = "正在卸载";

            Back.Visible = false;
            NextBtn.Enabled = false;
            CancelBtn.Enabled = false;

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

            L(Program.AppName + " 已从您的计算机中移除。", 0, 0, 428, 24);
            L("共删除 " + removed + " 个文件。", 0, 32, 428, 20);

            if (keep)
            {
                L("配置文件已保留：" + Path.Combine(_dir, "config.json"), 0, 62, 428, 20);
            }
            else
            {
                L("安装目录已清空。", 0, 62, 428, 20);
            }

            L("如果以后还要用，重新运行安装包即可。", 0, 96, 428, 20);
            L("作者：" + Program.Author + "　　联系邮箱：" + Program.Email, 0, 130, 428, 20);
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
