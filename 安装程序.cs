using System;
using System.Drawing;
using System.Windows.Forms;
using System.IO;
using System.Reflection;
using System.Diagnostics;
using System.IO.Compression;
using Microsoft.Win32;

namespace ShutdownInstaller
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new InstallForm());
        }
    }

    class InstallForm : Form
    {
        // 配色（与主程序一致的深色风格）
        Color C_BG = Color.FromArgb(31, 35, 41);
        Color C_PANEL = Color.FromArgb(38, 43, 51);
        Color C_BORDER = Color.FromArgb(46, 53, 65);
        Color C_TEXT = Color.FromArgb(230, 233, 238);
        Color C_SUB = Color.FromArgb(124, 134, 152);
        Color C_ACCENT = Color.FromArgb(61, 126, 255);

        TextBox txtPath;
        CheckBox chkDesktop, chkStartMenu, chkAutorun;
        Button btnInstall, btnCancel, btnRun;
        ProgressBar prog;
        Label lblStep;
        Panel configArea, doneArea;
        string installDir;

        public InstallForm()
        {
            Text = "小工具箱 安装程序";
            ClientSize = new Size(540, 460);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = C_BG;
            Font = new Font("Microsoft YaHei UI", 9F);
            // 窗口图标：从 exe 自身提取
            try { this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            // 顶部标题区
            Panel head = new Panel();
            head.BackColor = C_PANEL;
            head.Dock = DockStyle.Top;
            head.Height = 86;
            head.Paint += (s, e) => { using (Pen p = new Pen(C_BORDER)) e.Graphics.DrawLine(p, 0, 85, 540, 85); };
            Controls.Add(head);

            Label title = new Label();
            title.Text = "小工具箱 · 安装程序";
            title.ForeColor = C_TEXT;
            title.Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold);
            title.Location = new Point(20, 18);
            title.AutoSize = true;
            head.Controls.Add(title);

            Label sub = new Label();
            sub.Text = "多功能小工具箱 · 安装向导";
            sub.ForeColor = C_SUB;
            sub.Font = new Font("Microsoft YaHei UI", 9F);
            sub.Location = new Point(22, 54);
            sub.AutoSize = true;
            head.Controls.Add(sub);

            // ---------- 配置区 ----------
            configArea = new Panel();
            configArea.BackColor = C_BG;
            configArea.Location = new Point(0, 86);
            configArea.Size = new Size(540, 280);
            Controls.Add(configArea);

            Label l1 = new Label();
            l1.Text = "安装位置";
            l1.ForeColor = C_TEXT;
            l1.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            l1.Location = new Point(24, 22);
            l1.AutoSize = true;
            configArea.Controls.Add(l1);

            txtPath = new TextBox();
            txtPath.BackColor = Color.FromArgb(30, 34, 41);
            txtPath.ForeColor = C_TEXT;
            txtPath.BorderStyle = BorderStyle.FixedSingle;
            txtPath.Font = new Font("Microsoft YaHei UI", 9F);
            txtPath.Location = new Point(24, 50);
            txtPath.Size = new Size(380, 28);
            txtPath.Text = DefaultDir();
            configArea.Controls.Add(txtPath);

            Button btnBrowse = MakeBtn("浏览...", C_PANEL, C_TEXT, 414, 48, 100, 32);
            btnBrowse.Click += (s, e) => Browse();
            configArea.Controls.Add(btnBrowse);

            Label l2 = new Label();
            l2.Text = "安装选项";
            l2.ForeColor = C_TEXT;
            l2.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            l2.Location = new Point(24, 100);
            l2.AutoSize = true;
            configArea.Controls.Add(l2);

            chkDesktop = new CheckBox();
            chkDesktop.Text = "创建桌面快捷方式";
            chkDesktop.ForeColor = C_TEXT;
            chkDesktop.Checked = true;
            chkDesktop.Location = new Point(26, 128);
            chkDesktop.AutoSize = true;
            configArea.Controls.Add(chkDesktop);

            chkStartMenu = new CheckBox();
            chkStartMenu.Text = "创建开始菜单快捷方式";
            chkStartMenu.ForeColor = C_TEXT;
            chkStartMenu.Checked = true;
            chkStartMenu.Location = new Point(26, 158);
            chkStartMenu.AutoSize = true;
            configArea.Controls.Add(chkStartMenu);

            chkAutorun = new CheckBox();
            chkAutorun.Text = "开机自动启动（可在软件内随时开关）";
            chkAutorun.ForeColor = C_TEXT;
            chkAutorun.Location = new Point(26, 188);
            chkAutorun.AutoSize = true;
            configArea.Controls.Add(chkAutorun);

            // 步骤/进度
            lblStep = new Label();
            lblStep.Text = "";
            lblStep.ForeColor = C_SUB;
            lblStep.Font = new Font("Microsoft YaHei UI", 9F);
            lblStep.Location = new Point(24, 222);
            lblStep.AutoSize = true;
            configArea.Controls.Add(lblStep);

            prog = new ProgressBar();
            prog.Location = new Point(24, 244);
            prog.Size = new Size(490, 14);
            prog.Style = ProgressBarStyle.Continuous;
            prog.Visible = false;
            configArea.Controls.Add(prog);

            // ---------- 完成区 ----------
            doneArea = new Panel();
            doneArea.BackColor = C_BG;
            doneArea.Location = new Point(0, 86);
            doneArea.Size = new Size(540, 280);
            doneArea.Visible = false;
            Controls.Add(doneArea);

            Label doneIcon = new Label();
            doneIcon.Text = "✓";
            doneIcon.ForeColor = Color.FromArgb(34, 197, 94);
            doneIcon.Font = new Font("Microsoft YaHei UI", 40F, FontStyle.Bold);
            doneIcon.Location = new Point(230, 36);
            doneIcon.AutoSize = true;
            doneArea.Controls.Add(doneIcon);

            Label doneTitle = new Label();
            doneTitle.Text = "安装完成";
            doneTitle.ForeColor = C_TEXT;
            doneTitle.Font = new Font("Microsoft YaHei UI", 15F, FontStyle.Bold);
            doneTitle.Location = new Point(207, 104);
            doneTitle.AutoSize = true;
            doneArea.Controls.Add(doneTitle);

            Label doneMsg = new Label();
            doneMsg.Text = "小工具箱已成功安装到你的电脑。";
            doneMsg.ForeColor = C_SUB;
            doneMsg.Font = new Font("Microsoft YaHei UI", 9F);
            doneMsg.Location = new Point(160, 140);
            doneMsg.AutoSize = true;
            doneArea.Controls.Add(doneMsg);

            // ---------- 底部按钮 ----------
            btnInstall = MakeBtn("安装", Color.FromArgb(43, 95, 208), Color.White, 330, 382, 90, 40);
            btnInstall.Click += (s, e) => DoInstall();
            Controls.Add(btnInstall);

            btnCancel = MakeBtn("取消", Color.FromArgb(51, 58, 70), Color.FromArgb(154, 164, 178), 430, 382, 90, 40);
            btnCancel.Click += (s, e) => Close();
            Controls.Add(btnCancel);

            btnRun = MakeBtn("立即运行", Color.FromArgb(43, 95, 208), Color.White, 330, 382, 90, 40);
            btnRun.Visible = false;
            btnRun.Click += (s, e) => { try { Process.Start(Path.Combine(installDir, "小工具箱.exe")); } catch { } Close(); };
            Controls.Add(btnRun);

            Label foot = new Label();
            foot.Text = "安装到当前用户目录，无需管理员权限 · 可在「控制面板」中卸载";
            foot.ForeColor = Color.FromArgb(109, 118, 134);
            foot.Font = new Font("Microsoft YaHei UI", 8F);
            foot.Location = new Point(24, 434);
            foot.AutoSize = true;
            Controls.Add(foot);
        }

        string DefaultDir()
        {
            string baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(baseDir, "Programs", "小工具箱");
        }

        void Browse()
        {
            using (FolderBrowserDialog d = new FolderBrowserDialog())
            {
                d.Description = "选择安装位置";
                d.SelectedPath = txtPath.Text;
                if (d.ShowDialog(this) == DialogResult.OK)
                    txtPath.Text = Path.Combine(d.SelectedPath, "小工具箱");
            }
        }

        Button MakeBtn(string text, Color bg, Color fg, int x, int y, int w, int h)
        {
            Button b = new Button();
            b.Text = text;
            b.BackColor = bg;
            b.ForeColor = fg;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            b.Cursor = Cursors.Hand;
            b.Location = new Point(x, y);
            b.Size = new Size(w, h);
            return b;
        }

        byte[] ExtractResource(string namePart)
        {
            Assembly asm = Assembly.GetExecutingAssembly();
            string[] names = asm.GetManifestResourceNames();
            foreach (string n in names)
            {
                if (n.IndexOf(namePart, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    using (Stream s = asm.GetManifestResourceStream(n))
                    {
                        byte[] buf = new byte[s.Length];
                        int read = 0;
                        while (read < buf.Length)
                        {
                            int r = s.Read(buf, read, buf.Length - read);
                            if (r <= 0) break;
                            read += r;
                        }
                        return buf;
                    }
                }
            }
            return null;
        }

        void DoInstall()
        {
            installDir = txtPath.Text.Trim();
            if (string.IsNullOrEmpty(installDir)) { MessageBox.Show("请选择安装位置。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

            // 若已安装版本正在运行，先提示关闭（兼容新旧进程名）
            Process[] procs = Process.GetProcessesByName("小工具箱");
            if (procs.Length == 0) procs = Process.GetProcessesByName("定时关机");
            if (procs.Length > 0)
            {
                if (MessageBox.Show("检测到「小工具箱」正在运行，需要先关闭才能继续安装。\n是否现在关闭？",
                    "提示", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) == DialogResult.OK)
                {
                    foreach (Process p in procs) p.Kill();
                }
                else return;
            }

            btnInstall.Enabled = false;
            btnCancel.Enabled = false;
            prog.Visible = true;
            lblStep.Text = "正在安装...";
            Application.DoEvents();

            try
            {
                byte[] exeData = ExtractResource("MainExe");
                if (exeData == null) throw new Exception("安装包资源缺失。");

                Directory.CreateDirectory(installDir);
                // 清理旧版文件名残留（曾用名 定时关机.exe）
                string legacy = Path.Combine(installDir, "定时关机.exe");
                if (File.Exists(legacy)) { try { File.Delete(legacy); } catch { } }
                string exePath = Path.Combine(installDir, "小工具箱.exe");
                File.WriteAllBytes(exePath, exeData);
                prog.Value = 40;
                Application.DoEvents();

                // 桌面快捷方式
                if (chkDesktop.Checked)
                {
                    string desk = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                    CreateShortcut(Path.Combine(desk, "小工具箱.lnk"), exePath, installDir, "小工具箱 v3.0");
                }
                prog.Value = 60;
                Application.DoEvents();

                // 开始菜单快捷方式
                if (chkStartMenu.Checked)
                {
                    string startMenu = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
                    CreateShortcut(Path.Combine(startMenu, "小工具箱.lnk"), exePath, installDir, "小工具箱 v3.0");
                }
                prog.Value = 80;
                Application.DoEvents();

                // 开机自启
                if (chkAutorun.Checked)
                {
                    SetAutorun(exePath, true);
                }

                // Steam 成就管理组件（sam_tool）
                byte[] samZip = ExtractResource("SamZip");
                if (samZip != null && samZip.Length > 0)
                {
                    try
                    {
                        string zipTmp = Path.Combine(Path.GetTempPath(), "sam_" + Guid.NewGuid().ToString("N") + ".zip");
                        File.WriteAllBytes(zipTmp, samZip);
                        string samDir = Path.Combine(installDir, "sam_tool");
                        Directory.CreateDirectory(samDir);
                        using (ZipArchive za = ZipFile.OpenRead(zipTmp))
                        {
                            foreach (ZipArchiveEntry en in za.Entries)
                            {
                                string outF = Path.Combine(samDir, en.FullName.Replace('/', Path.DirectorySeparatorChar));
                                Directory.CreateDirectory(Path.GetDirectoryName(outF));
                                en.ExtractToFile(outF, true);
                            }
                        }
                        File.Delete(zipTmp);
                    }
                    catch { }
                }

                // C盘清理组件（clean_tool）
                byte[] cleanZip = ExtractResource("CleanZip");
                if (cleanZip != null && cleanZip.Length > 0)
                {
                    try
                    {
                        string zipTmp = Path.Combine(Path.GetTempPath(), "clean_" + Guid.NewGuid().ToString("N") + ".zip");
                        File.WriteAllBytes(zipTmp, cleanZip);
                        string cleanDir = Path.Combine(installDir, "clean_tool");
                        Directory.CreateDirectory(cleanDir);
                        using (ZipArchive za = ZipFile.OpenRead(zipTmp))
                        {
                            foreach (ZipArchiveEntry en in za.Entries)
                            {
                                string outF = Path.Combine(cleanDir, en.FullName.Replace('/', Path.DirectorySeparatorChar));
                                Directory.CreateDirectory(Path.GetDirectoryName(outF));
                                en.ExtractToFile(outF, true);
                            }
                        }
                        File.Delete(zipTmp);
                    }
                    catch { }
                }

                // 游戏作弊组件（gcm_tool）由主程序首次使用时自动部署，安装器不再内置

                // FFmpeg 合并工具（视频下载高清格式用，压缩包内嵌，安装时解压）
                byte[] ffmpegZip = ExtractResource("FfmpegZip");
                if (ffmpegZip != null && ffmpegZip.Length > 0)
                {
                    try
                    {
                        string zipTmp = Path.Combine(Path.GetTempPath(), "ff_" + Guid.NewGuid().ToString("N") + ".zip");
                        File.WriteAllBytes(zipTmp, ffmpegZip);
                        string vdir = Path.Combine(installDir, "video_tool");
                        Directory.CreateDirectory(vdir);
                        using (ZipArchive za = ZipFile.OpenRead(zipTmp))
                        {
                            foreach (ZipArchiveEntry en in za.Entries)
                            {
                                string outF = Path.Combine(vdir, en.FullName.Replace('/', Path.DirectorySeparatorChar));
                                Directory.CreateDirectory(Path.GetDirectoryName(outF));
                                en.ExtractToFile(outF, true);
                            }
                        }
                        File.Delete(zipTmp);
                    }
                    catch { }
                }


                // 卸载信息（供控制面板卸载）
                WriteUninstallInfo(exePath);
                prog.Value = 100;
                Application.DoEvents();

                configArea.Visible = false;
                doneArea.Visible = true;
                btnInstall.Visible = false;
                btnRun.Visible = true;
                btnCancel.Text = "完成";
                btnCancel.Enabled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show("安装失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                btnInstall.Enabled = true;
                btnCancel.Enabled = true;
                prog.Visible = false;
                lblStep.Text = "";
            }
        }

        void CreateShortcut(string lnkPath, string target, string workDir, string desc)
        {
            try
            {
                Type t = Type.GetTypeFromProgID("WScript.Shell");
                if (t == null) return;
                object shell = Activator.CreateInstance(t);
                object sc = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnkPath });
                sc.GetType().InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc, new object[] { target });
                sc.GetType().InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc, new object[] { workDir });
                sc.GetType().InvokeMember("Description", BindingFlags.SetProperty, null, sc, new object[] { desc });
                sc.GetType().InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
            }
            catch { }
        }

        void SetAutorun(string exePath, bool on)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                {
                    if (on) k.SetValue("DoubaoScheduledShutdown", "\"" + exePath + "\"");
                    else k.DeleteValue("DoubaoScheduledShutdown", false);
                }
            }
            catch { }
        }

        void WriteUninstallInfo(string exePath)
        {
            try
            {
                string dir = Path.GetDirectoryName(exePath);
                string unBat = Path.Combine(dir, "卸载小工具箱.bat");
                string bat = "@echo off\r\n"
                    + "taskkill /f /im 小工具箱.exe >nul 2>&1\r\n"
                    + "taskkill /f /im 定时关机.exe >nul 2>&1\r\n"
                    + "del /q \"%USERPROFILE%\\Desktop\\小工具箱.lnk\" >nul 2>&1\r\n"
                    + "del /q \"%APPDATA%\\Microsoft\\Windows\\Start Menu\\Programs\\小工具箱.lnk\" >nul 2>&1\r\n"
                    + "reg delete \"HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run\" /v DoubaoScheduledShutdown /f >nul 2>&1\r\n"
                    + "reg delete \"HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\小工具箱\" /f >nul 2>&1\r\n"
                    + "cd /d \"%TEMP%\"\r\n"
                    + "rd /s /q \"" + dir + "\\clean_tool\" >nul 2>&1\r\n"
                    + "rd /s /q \"" + dir + "\\sam_tool\" >nul 2>&1\r\n"
                    + "rd /s /q \"" + dir + "\\gcm_tool\" >nul 2>&1\r\n"
                    + "rd /s /q \"" + dir + "\" >nul 2>&1\r\n"
                    + "exit\r\n";
                File.WriteAllText(unBat, bat, System.Text.Encoding.Default);

                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\小工具箱"))
                {
                    k.SetValue("DisplayName", "小工具箱 v3.0");
                    k.SetValue("DisplayVersion", "3.0.0");
                    k.SetValue("Publisher", "Doubao");
                    k.SetValue("InstallLocation", dir);
                    k.SetValue("DisplayIcon", exePath);
                    k.SetValue("UninstallString", "\"" + unBat + "\"");
                    k.SetValue("QuietUninstallString", "\"" + unBat + "\"");
                    k.SetValue("NoModify", 1);
                    k.SetValue("NoRepair", 1);
                    k.SetValue("EstimatedSize", 200);
                }
            }
            catch { }
        }
    }
}
