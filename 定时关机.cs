using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Text;
using System.Media;
using Microsoft.Win32;
using System.Reflection;
using System.Collections.Generic;
using System.IO.Compression;
using System.Net;
using System.Web.Script.Serialization;
using System.Runtime.InteropServices;

namespace ShutdownTool
{
    static class Program
    {
        public static Mutex mutex;
        [STAThread]
        static void Main()
        {
            bool createdNew;
            mutex = new Mutex(true, "DoubaoShutdownTool_SingleInstance", out createdNew);
            if (!createdNew) { MessageBox.Show("小工具箱已在运行。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            // 全局异常捕获：写日志，不弹崩溃框
            Application.ThreadException += (s, e) => CrashLog(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => CrashLog(e.ExceptionObject as Exception);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
            GC.KeepAlive(mutex);
        }
        static void CrashLog(Exception ex)
        {
            if (ex == null) return;
            try
            {
                string dir = Path.GetDirectoryName(Application.ExecutablePath);
                File.AppendAllText(Path.Combine(dir, "error.log"),
                    "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "]\r\n" + ex + "\r\n\r\n");
            }
            catch { }
        }
    }

    // ================= 任务配置 =================
    class TaskCfg
    {
        public string Action = "关机";
        public string Mode = "countdown";   // countdown / datetime / daily / weekly
        public int Hours = 0, Mins = 30, Secs = 0;
        public DateTime AtTime = DateTime.Now.AddHours(1);
        public bool[] Week = new bool[] { false, false, false, false, false, false, false }; // 周一~周日
        public bool NeedConfirm = true;
        public int ConfirmSecs = 60;
        public bool Sound = true;
        public bool Tray = true;
        public bool Autorun = false;
        // 运行态
        public bool Active = false;
        public bool Periodic = false;
        public DateTime EndTime;
        public int TotalSec = 0;
        public string Desc = "";
    }

    static class Config
    {
        public static void Save(string dir, TaskCfg c)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("action=" + c.Action);
                sb.AppendLine("mode=" + c.Mode);
                sb.AppendLine("hours=" + c.Hours);
                sb.AppendLine("mins=" + c.Mins);
                sb.AppendLine("secs=" + c.Secs);
                sb.AppendLine("at=" + c.AtTime.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine("week=" + string.Join(",", c.Week));
                sb.AppendLine("needconfirm=" + c.NeedConfirm);
                sb.AppendLine("confirms=" + c.ConfirmSecs);
                sb.AppendLine("sound=" + c.Sound);
                sb.AppendLine("tray=" + c.Tray);
                sb.AppendLine("autorun=" + c.Autorun);
                sb.AppendLine("active=" + c.Active);
                sb.AppendLine("periodic=" + c.Periodic);
                sb.AppendLine("endtime=" + c.EndTime.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine("totalsec=" + c.TotalSec);
                sb.AppendLine("desc=" + c.Desc);
                File.WriteAllText(Path.Combine(dir, "config.txt"), sb.ToString(), Encoding.UTF8);
            }
            catch { }
        }
        public static TaskCfg Load(string dir)
        {
            var c = new TaskCfg();
            try
            {
                string p = Path.Combine(dir, "config.txt");
                if (!File.Exists(p)) return c;
                foreach (string line in File.ReadAllLines(p, Encoding.UTF8))
                {
                    int i = line.IndexOf('=');
                    if (i < 0) continue;
                    string k = line.Substring(0, i).Trim();
                    string v = line.Substring(i + 1).Trim();
                    switch (k)
                    {
                        case "action": c.Action = v; break;
                        case "mode": c.Mode = v; break;
                        case "hours": int.TryParse(v, out c.Hours); break;
                        case "mins": int.TryParse(v, out c.Mins); break;
                        case "secs": int.TryParse(v, out c.Secs); break;
                        case "at": DateTime.TryParse(v, out c.AtTime); break;
                        case "week":
                            {
                                string[] ws = v.Split(',');
                                for (int j = 0; j < 7 && j < ws.Length; j++) { bool b; if (bool.TryParse(ws[j], out b)) c.Week[j] = b; }
                            }
                            break;
                        case "needconfirm": bool.TryParse(v, out c.NeedConfirm); break;
                        case "confirms": int.TryParse(v, out c.ConfirmSecs); break;
                        case "sound": bool.TryParse(v, out c.Sound); break;
                        case "tray": bool.TryParse(v, out c.Tray); break;
                        case "autorun": bool.TryParse(v, out c.Autorun); break;
                        case "active": bool.TryParse(v, out c.Active); break;
                        case "periodic": bool.TryParse(v, out c.Periodic); break;
                        case "endtime": DateTime.TryParse(v, out c.EndTime); break;
                        case "totalsec": int.TryParse(v, out c.TotalSec); break;
                        case "desc": c.Desc = v; break;
                    }
                }
            }
            catch { }
            return c;
        }
    }

    // ================= 主窗体 =================
    class MainForm : Form
    {
        // 状态
        TaskCfg cfg = new TaskCfg();
        bool taskActive = false;
        bool confirmShown = false;
        DateTime endTime;
        int totalSec = 0;
        string curAction = "关机";
        string curDesc = "";
        System.Windows.Forms.Timer uiTimer;
        ConfirmForm confirmWin;
        NotifyIcon trayIcon;
        ContextMenuStrip trayMenu;

        string BASE_DIR = Path.GetDirectoryName(Application.ExecutablePath);
        string logFile;

        // ===== 语言 =====
        bool langEn = false;        // false=中文 true=English
        string T(string zh, string en) { return langEn ? en : zh; }

        // 配色（仅黑色天蓝主题）
        Color C_BG { get { return Color.FromArgb(31, 35, 41); } }
        Color C_SIDE { get { return Color.FromArgb(26, 30, 37); } }
        Color C_MAIN { get { return Color.FromArgb(38, 43, 51); } }
        Color C_BUBBLE { get { return Color.FromArgb(44, 50, 60); } }
        Color C_BORDER { get { return Color.FromArgb(51, 59, 72); } }
        Color C_TEXT { get { return Color.FromArgb(230, 233, 238); } }
        Color C_SUB { get { return Color.FromArgb(126, 136, 154); } }
        Color C_ACCENT { get { return Color.FromArgb(61, 126, 255); } }
        Color C_ON { get { return Color.FromArgb(34, 197, 94); } }
        Color C_SEL { get { return Color.FromArgb(40, 50, 74); } }
        // 输入框/列表底色
        Color C_INPUT { get { return Color.FromArgb(27, 31, 38); } }
        Color C_INPUT_TEXT { get { return Color.White; } }

        // 控件
        Panel sidebar, mainArea;
        Label[] convName = new Label[16];
        Panel[] convItem = new Panel[16];
        int sidebarSel = 0;
        Label lblPeer, lblStatus, lblDetail, lblTime, lblTimeSub, lblTyping, lblService, lblAbout;
        Panel ringPanel;
        CheckBox chkConfirm, chkSound, chkAutorun, chkTray;
        NumericUpDown numH, numM, numS, numConfirm;
        RadioButton rbCountdown, rbDatetime, rbDaily, rbWeekly;
        DateTimePicker dtpTime, dtpDaily, dtpWeekly;
        CheckBox[] weekCb = new CheckBox[7];
        Panel pCountdown, pDatetime, pDaily, pWeekly;
        Button btnStart, btnCancel, btnImm, btnLog;

        // ===== 拼图页 =====
        Panel pageTimer, pagePuzzle, pageReset, pageClean, pageGcm, pageVideo, pageMusic;

        // ===== 视频下载页 =====
        TextBox txtVUrl;
        Button btnVParse, btnVDl;
        ComboBox cmbVFormat;
        Label lblVInfo, lblVDir, lblVTip;
        ListView lvVideos;
        string vOutDir = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "下载");
        Process vDlProc = null;
        bool vBusy = false;
        List<string> vFmtIds = new List<string>();
        List<bool> vFmtMerge = new List<bool>();

        // ===== 音乐下载页 =====
        TextBox txtMSearch;
        Button btnMSearch, btnMDl, btnMUrl, btnMOpen;
        ListView lvSongs;
        ProgressBar pbMusic;
        Label lblMStatus;
        List<object[]> mSongs = new List<object[]>();   // id, name, artist, album, duration
        bool mBusy = false;
        List<Bitmap> pzImages = new List<Bitmap>();
        int pzImgIdx = 0, pzGrid = 3;
        bool pzStarted = false;   // 是否已点「下一张」启动下载（进页面不预载任何图）
        PictureBox[] pzPieces;
        int[] pzGridContent;
        int pzCellPx = 150, pzPiecePx = 96;
        int pzGridAreaX = 30, pzGridAreaY = 95, pzGridAreaPx = 460;
        int pzPieceAreaX = 530, pzPieceAreaY = 95;
        bool pzDragging = false;
        int pzDragIdx = -1;
        System.Windows.Forms.Timer pzTimer;
        int pzElapsed = 0;
        bool pzSolved = false;
        Label lblPzTime, lblPzDone;
        Button btnPzPrev, btnPzNext, btnPz3, btnPz4, btnPzRestart;
        Random pzRnd = new Random();
        // 精选保底池（前 24 张保证可用且为人物/女性图）；第 25 张起动态生成随机图源，无上限
        static readonly string[] PZ_URLS = {
            "https://images.pexels.com/photos/1036623/pexels-photo-1036623.jpeg?auto=compress&cs=tinysrgb&w=600",
            "https://images.pexels.com/photos/774909/pexels-photo-774909.jpeg?auto=compress&cs=tinysrgb&w=600",
            "https://images.pexels.com/photos/415829/pexels-photo-415829.jpeg?auto=compress&cs=tinysrgb&w=600",
            "https://images.pexels.com/photos/733872/pexels-photo-733872.jpeg?auto=compress&cs=tinysrgb&w=600",
            "https://images.pexels.com/photos/1130626/pexels-photo-1130626.jpeg?auto=compress&cs=tinysrgb&w=600",
            "https://images.pexels.com/photos/1239291/pexels-photo-1239291.jpeg?auto=compress&cs=tinysrgb&w=600",
            "https://images.pexels.com/photos/2380794/pexels-photo-2380794.jpeg?auto=compress&cs=tinysrgb&w=600",
            "https://images.pexels.com/photos/1181686/pexels-photo-1181686.jpeg?auto=compress&cs=tinysrgb&w=600",
            "https://images.pexels.com/photos/3778603/pexels-photo-3778603.jpeg?auto=compress&cs=tinysrgb&w=600",
            "https://images.pexels.com/photos/3710588/pexels-photo-3710588.jpeg?auto=compress&cs=tinysrgb&w=600",
            "https://images.pexels.com/photos/3710646/pexels-photo-3710646.jpeg?auto=compress&cs=tinysrgb&w=600",
            "https://images.pexels.com/photos/2210606/pexels-photo-2210606.jpeg?auto=compress&cs=tinysrgb&w=600",
            "https://images.pexels.com/photos/4352248/pexels-photo-4352248.jpeg?auto=compress&cs=tinysrgb&w=600",
            "https://images.pexels.com/photos/3454953/pexels-photo-3454953.jpeg?auto=compress&cs=tinysrgb&w=600",
            "https://images.pexels.com/photos/1908195/pexels-photo-1908195.jpeg?auto=compress&cs=tinysrgb&w=600",
            "https://images.pexels.com/photos/3514422/pexels-photo-3514422.jpeg?auto=compress&cs=tinysrgb&w=600",
            "https://images.pexels.com/photos/3852807/pexels-photo-3852807.jpeg?auto=compress&cs=tinysrgb&w=600",
            "https://images.pexels.com/photos/3900857/pexels-photo-3900857.jpeg?auto=compress&cs=tinysrgb&w=600",
            "https://images.pexels.com/photos/864939/pexels-photo-864939.jpeg?auto=compress&cs=tinysrgb&w=600",
            "https://images.pexels.com/photos/1587009/pexels-photo-1587009.jpeg?auto=compress&cs=tinysrgb&w=600",
            "https://images.pexels.com/photos/762020/pexels-photo-762020.jpeg?auto=compress&cs=tinysrgb&w=600",
            "https://images.pexels.com/photos/1065084/pexels-photo-1065084.jpeg?auto=compress&cs=tinysrgb&w=600",
            "https://images.pexels.com/photos/977933/pexels-photo-977933.jpeg?auto=compress&cs=tinysrgb&w=600",
            "https://images.pexels.com/photos/4156880/pexels-photo-4156880.jpeg?auto=compress&cs=tinysrgb&w=600",
        };

        string pzCacheDir = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "puzzle_cache");


        string[] ACTIONS_ZH = { "关机", "锁定", "一键重装", "C盘清理", "游戏作弊", "视频下载", "音乐下载", "拼图", "Steam成就", "电脑开荒" };
        string[] ACTIONS_EN = { "Shutdown", "Lock", "Reinstall", "C: Cleanup", "Game Cheats", "Video Download", "Music Download", "Puzzle", "Steam Achievements", "PC Setup" };
        string[] ACTION_SUB_ZH = { "关闭计算机", "锁定当前屏幕", "重装 / 恢复系统", "清理系统缓存垃圾", "单机游戏修改器管理", "全网视频一键下载", "网易云曲库在线下载", "在线图片拼图游戏", "Steam 成就管理器", "开荒·体检·换盘·优化" };
        string[] ACTION_SUB_EN = { "Shut down computer", "Lock current screen", "Reinstall / restore system", "Clean system cache & junk", "Offline game trainer manager", "Download video from 1000+ sites", "Netease music download", "Online image jigsaw game", "Steam achievement manager", "Setup, checkup, drive, optimize" };
        string[] ACTIONS { get { return langEn ? ACTIONS_EN : ACTIONS_ZH; } }
        string[] ACTION_SUB { get { return langEn ? ACTION_SUB_EN : ACTION_SUB_ZH; } }
        Color[] ACTION_COLOR = {
            Color.FromArgb(232, 82, 61),
            Color.FromArgb(139, 149, 165),
            Color.FromArgb(34, 197, 94),
            Color.FromArgb(56, 189, 248),
            Color.FromArgb(244, 114, 182),
            Color.FromArgb(250, 204, 21),
            Color.FromArgb(16, 185, 129),
            Color.FromArgb(168, 85, 247),
            Color.FromArgb(249, 115, 22),
            Color.FromArgb(45, 212, 191)
        };
        Color curActionColor;

        public MainForm()
        {
            logFile = Path.Combine(BASE_DIR, "shutdown_history.log");
            LoadUiPrefs();
            curActionColor = ACTION_COLOR[0];
            try { this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            InitUI();
            InitTray();
            InitState();
            cfg = Config.Load(BASE_DIR);
            ApplyConfigToUI();
            TryRestoreTask();
            uiTimer = new System.Windows.Forms.Timer();
            uiTimer.Interval = 300;
            uiTimer.Tick += (s, e) => Tick();
            uiTimer.Start();
        }

        // ================= 界面偏好（语言） =================
        void LoadUiPrefs()
        {
            try
            {
                string p = Path.Combine(BASE_DIR, "ui.txt");
                if (!File.Exists(p)) return;
                foreach (string line in File.ReadAllLines(p, Encoding.UTF8))
                {
                    int i = line.IndexOf('=');
                    if (i < 0) continue;
                    string k = line.Substring(0, i).Trim(), v = line.Substring(i + 1).Trim();
                    if (k == "lang" && v == "1") langEn = true;
                }
            }
            catch { }
        }
        void SaveUiPrefs()
        {
            try
            {
                File.WriteAllText(Path.Combine(BASE_DIR, "ui.txt"),
                    "lang=" + (langEn ? 1 : 0), Encoding.UTF8);
            }
            catch { }
        }
        void RestartApp()
        {
            try { Program.mutex.ReleaseMutex(); Program.mutex.Close(); }
            catch (Exception ex) { try { File.AppendAllText(Path.Combine(BASE_DIR, "error.log"), "[restart-mutex] " + ex + "\r\n"); } catch { } }
            try { Process.Start(Application.ExecutablePath); }
            catch (Exception ex) { try { File.AppendAllText(Path.Combine(BASE_DIR, "error.log"), "[restart-start] " + ex + "\r\n"); } catch { } }
            Application.Exit();
        }
        void ToggleLang()
        {
            langEn = !langEn;
            SaveUiPrefs();
            RestartApp();
        }

        // 液态玻璃（DWM 亚克力背景）
        [StructLayout(LayoutKind.Sequential)]
        struct AccentPolicy { public int AccentState; public int AccentFlags; public int GradientColor; public int AnimationId; }
        [StructLayout(LayoutKind.Sequential)]
        struct WindowCompositionAttribData { public int Attrib; public IntPtr Data; public int SizeOfData; }
        [DllImport("user32.dll")]
        static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttribData data);
        void ApplyAcrylic(IntPtr hwnd)
        {
            try
            {
                AccentPolicy ap = new AccentPolicy();
                ap.AccentState = 4;   // ACCENT_ENABLE_ACRYLICBLURBEHIND
                ap.GradientColor = Color.FromArgb(170, 26, 30, 37).ToArgb();
                WindowCompositionAttribData data = new WindowCompositionAttribData();
                data.Attrib = 19;     // WCA_ACCENT_POLICY
                data.Data = Marshal.AllocHGlobal(Marshal.SizeOf(ap));
                Marshal.StructureToPtr(ap, data.Data, false);
                data.SizeOfData = Marshal.SizeOf(ap);
                SetWindowCompositionAttribute(hwnd, ref data);
                Marshal.FreeHGlobal(data.Data);
            }
            catch { }
        }

        // ================= 界面 =================
        void InitUI()
        {
            Text = "小工具箱";
            ClientSize = new Size(960, 700);
            MinimumSize = new Size(960, 700);
            AutoScaleMode = AutoScaleMode.None;   // 禁用自动缩放，防止控件位置漂移
            BackColor = C_BG;
            Font = new Font("Microsoft YaHei UI", 9F);
            StartPosition = FormStartPosition.CenterScreen;

            // ---------- 左侧栏 ----------
            sidebar = new Panel();
            sidebar.BackColor = C_SIDE;
            sidebar.Dock = DockStyle.Left;
            sidebar.Width = 200;

            Panel me = new Panel();
            me.BackColor = C_SIDE;
            me.Location = new Point(0, 0);
            me.Size = new Size(200, 70);
            sidebar.Controls.Add(me);
            Panel meAvatar = new Panel();
            meAvatar.BackColor = Color.FromArgb(43, 95, 208);
            meAvatar.Location = new Point(14, 15);
            meAvatar.Size = new Size(40, 40);
            meAvatar.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath path = UiKit.RoundedPath(new Rectangle(0, 0, 39, 39), 10))
                {
                    using (LinearGradientBrush br = new LinearGradientBrush(new Rectangle(0, 0, 39, 39), Color.FromArgb(91, 150, 255), Color.FromArgb(43, 95, 208), 135f))
                        e.Graphics.FillPath(br, path);
                    // 玻璃高光
                    e.Graphics.SetClip(path);
                    using (LinearGradientBrush hl = new LinearGradientBrush(new Rectangle(0, 0, 39, 20), Color.FromArgb(90, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
                        e.Graphics.FillRectangle(hl, 0, 0, 39, 20);
                    e.Graphics.ResetClip();
                }
                DrawCentered(e.Graphics, meAvatar, "⚡", 19F, Color.White);
            };
            me.Controls.Add(meAvatar);
            Label meName = new Label();
            meName.Text = "小工具箱";
            meName.ForeColor = C_TEXT;
            meName.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            meName.Location = new Point(62, 16);
            meName.AutoSize = true;
            me.Controls.Add(meName);
            Label meSub = new Label();
            meSub.Text = "小工具箱 v3.0";
            meSub.ForeColor = C_SUB;
            meSub.Font = new Font("Microsoft YaHei UI", 8F);
            meSub.Location = new Point(62, 40);
            meSub.AutoSize = true;
            me.Controls.Add(meSub);

            int y = 70;
            for (int i = 0; i < ACTIONS.Length; i++)
            {
                Panel item = new Panel();
                item.BackColor = (i == 0) ? C_SEL : C_SIDE;
                item.Location = new Point(0, y);
                item.Size = new Size(200, 46);
                item.Cursor = Cursors.Hand;
                int idx = i;
                item.Click += (s, e) => SelectAction(idx);
                item.Paint += (s, e) =>
                {
                    if (i == sidebarSel)
                    {
                        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                        Rectangle irc = new Rectangle(0, 0, item.Width, item.Height - 1);
                        using (var br = new System.Drawing.Drawing2D.LinearGradientBrush(irc,
                            Color.FromArgb(49, 61, 89),
                            Color.FromArgb(36, 45, 66), 90f))
                            e.Graphics.FillRectangle(br, irc);
                    }
                    if (i < ACTIONS.Length - 1) using (Pen p = new Pen(Color.FromArgb(38, 43, 51))) e.Graphics.DrawLine(p, 0, 45, 200, 45);
                };
                sidebar.Controls.Add(item);

                Panel bar = new Panel();
                bar.BackColor = C_ACCENT;
                bar.Location = new Point(0, 6);
                bar.Size = new Size(3, 32);
                bar.Visible = (i == 0);
                item.Controls.Add(bar);

                Panel av = new Panel();
                av.BackColor = Color.FromArgb(30, 30, 34);
                av.Location = new Point(14, 5);
                av.Size = new Size(38, 36);
                int ai = i;
                av.Paint += (s, e) =>
                {
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    Rectangle avr = new Rectangle(0, 0, av.Width - 1, av.Height - 1);
                    using (GraphicsPath path = UiKit.RoundedPath(avr, 9))
                    {
                        using (LinearGradientBrush br = new LinearGradientBrush(avr,
                            Color.FromArgb(36, 40, 48),
                            Color.FromArgb(24, 27, 33), 90f))
                            e.Graphics.FillPath(br, path);
                        using (Pen pp = new Pen(Color.FromArgb(56, 66, 82)))
                            e.Graphics.DrawPath(pp, path);
                    }
                    using (StringFormat sf = new StringFormat())
                    {
                        sf.Alignment = StringAlignment.Center;
                        sf.LineAlignment = StringAlignment.Center;
                        using (Font f = new Font("Microsoft YaHei UI", 15F, FontStyle.Bold))
                        using (SolidBrush b = new SolidBrush(ACTION_COLOR[ai]))
                            e.Graphics.DrawString(ACTIONS[ai].Substring(0, 1), f, b, new RectangleF(0, 0, av.Width, av.Height), sf);
                    }
                };
                item.Controls.Add(av);

                Label nm = new Label();
                nm.Text = ACTIONS[i];
                nm.ForeColor = (i == 0) ? Color.White : C_TEXT;
                nm.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold);
                nm.Location = new Point(58, 6);
                nm.AutoSize = true;
                item.Controls.Add(nm);

                Label sub = new Label();
                sub.Text = ACTION_SUB[i];
                sub.ForeColor = C_SUB;
                sub.Font = new Font("Microsoft YaHei UI", 7.5F);
                sub.Location = new Point(58, 24);
                sub.AutoSize = true;
                item.Controls.Add(sub);

                // 子控件点击统一转发到侧栏项（Label/Panel 会拦截鼠标事件）
                nm.Click += (s, e) => SelectAction(idx);
                sub.Click += (s, e) => SelectAction(idx);
                av.Click += (s, e) => SelectAction(idx);
                item.Click += (s, e) => SelectAction(idx);

                convItem[i] = item;
                convName[i] = nm;
                y += 46;
            }

            chkAutorun = new CheckBox();
            chkAutorun.Text = T("开机自动启动", "Auto-start at boot");
            chkAutorun.ForeColor = C_SUB;
            chkAutorun.Font = new Font("Microsoft YaHei UI", 8F);
            chkAutorun.Location = new Point(12, 540);
            chkAutorun.AutoSize = true;
            chkAutorun.CheckedChanged += (s, e) => ToggleAutorun();
            sidebar.Controls.Add(chkAutorun);

            chkTray = new CheckBox();
            chkTray.Text = T("关闭时最小化到托盘", "Minimize to tray on close");
            chkTray.ForeColor = C_SUB;
            chkTray.Font = new Font("Microsoft YaHei UI", 8F);
            chkTray.Location = new Point(12, 566);
            chkTray.AutoSize = true;
            chkTray.CheckedChanged += (s, e) => cfg.Tray = chkTray.Checked;
            sidebar.Controls.Add(chkTray);

            lblAbout = new Label();
            lblAbout.Text = langEn ? "Mini Toolbox v3.0 · click About" : "小工具箱 v3.0 · 点击关于";
            lblAbout.ForeColor = Color.FromArgb(90, 99, 115);
            lblAbout.Font = new Font("Microsoft YaHei UI", 7.5F);
            lblAbout.Location = new Point(12, 620);
            lblAbout.AutoSize = true;
            lblAbout.Cursor = Cursors.Hand;
            lblAbout.Click += (s, e) => ShowAbout();
            sidebar.Controls.Add(lblAbout);

            // ---------- 右侧主区 ----------
            mainArea = new Panel();
            mainArea.BackColor = C_MAIN;
            mainArea.Dock = DockStyle.Fill;
            Controls.Add(mainArea);   // mainArea 先添加：Dock=Fill 后处理，占 200..800
            Controls.Add(sidebar);    // sidebar 后添加：Dock=Left 先处理，占 0..200

            // 双页面：定时关机页 / 拼图页
            pageTimer = new Panel();
            pageTimer.Dock = DockStyle.Fill;
            pageTimer.BackColor = C_MAIN;
            mainArea.Controls.Add(pageTimer);

            pagePuzzle = new Panel();
            pagePuzzle.Dock = DockStyle.Fill;
            pagePuzzle.BackColor = C_MAIN;
            pagePuzzle.Visible = false;
            mainArea.Controls.Add(pagePuzzle);

            pageReset = new Panel();
            pageReset.Dock = DockStyle.Fill;
            pageReset.BackColor = C_MAIN;
            pageReset.Visible = false;
            mainArea.Controls.Add(pageReset);

            Label rTitle = new Label();
            rTitle.Text = T("一键重装系统", "Reinstall System");
            rTitle.ForeColor = C_TEXT;
            rTitle.Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold);
            rTitle.Location = new Point(30, 26);
            rTitle.AutoSize = true;
            pageReset.Controls.Add(rTitle);

            Label rSub = new Label();
            rSub.Text = T("使用 Windows 官方恢复机制，安全重装或修复系统", "Use official Windows recovery to reinstall or repair safely");
            rSub.ForeColor = C_SUB;
            rSub.Font = new Font("Microsoft YaHei UI", 9F);
            rSub.Location = new Point(30, 58);
            rSub.AutoSize = true;
            pageReset.Controls.Add(rSub);

            Button rBack = MakeBtn(T("返回定时", "Back"), C_ON, Color.Black, 620, 26, 90, 28);
            rBack.Click += (s, e) => ShowResetPage(false);
            pageReset.Controls.Add(rBack);

            string[,] resets = langEn
                ? new string[,] {
                    { "Open Reset Wizard", "Keep files or remove everything, follow the wizard", "wizard" },
                    { "Restart to Recovery (WinRE)", "Reset this PC, fix startup, open Command Prompt", "winre" },
                    { "Open Recovery Settings", "Settings → System → Recovery: reset & advanced startup", "settings" },
                }
                : new string[,] {
                    { "打开重装向导", "保留文件或删除所有内容，跟随向导完成系统重装", "wizard" },
                    { "重启进入恢复环境", "进入 WinRE：可重置此电脑、修复启动、进入命令提示符", "winre" },
                    { "打开系统恢复设置", "打开「设置 → 系统 → 恢复」，查看重置与高级启动选项", "settings" },
                };
            int ry = 100;
            for (int i = 0; i < 3; i++)
            {
                Panel card = new Panel();
                card.BackColor = C_BUBBLE;
                card.Location = new Point(30, ry);
                card.Size = new Size(700, 84);
                card.Paint += (s, e) => { using (Pen p = new Pen(C_BORDER)) e.Graphics.DrawRectangle(p, 0, 0, 699, 83); };
                pageReset.Controls.Add(card);

                Label ct = new Label();
                ct.Text = resets[i, 0];
                ct.ForeColor = C_TEXT;
                ct.Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold);
                ct.Location = new Point(20, 12);
                ct.AutoSize = true;
                card.Controls.Add(ct);

                Label cd = new Label();
                cd.Text = resets[i, 1];
                cd.ForeColor = C_SUB;
                cd.Font = new Font("Microsoft YaHei UI", 8.5F);
                cd.Location = new Point(20, 44);
                cd.AutoSize = true;
                card.Controls.Add(cd);

                string mode = resets[i, 2];
                Button go = MakeBtn("开始", C_ON, Color.Black, 580, 26, 90, 32);
                go.Click += (s, e) => ConfirmReset(mode);
                card.Controls.Add(go);

                ry += 96;
            }

            Label warn = new Label();
            warn.Text = "⚠ 重装系统会删除或重置大量数据，操作前请务必备份重要文件，并确保设备已连接电源。";
            warn.ForeColor = Color.FromArgb(240, 160, 90);
            warn.Font = new Font("Microsoft YaHei UI", 9F);
            warn.Location = new Point(30, ry + 6);
            warn.AutoSize = true;
            pageReset.Controls.Add(warn);

            Label tip = new Label();
            tip.Text = "提示：系统重装依赖 Windows 恢复环境（WinRE）。若恢复环境缺失，可在「命令提示符」中执行：reagentc /enable";
            tip.ForeColor = C_SUB;
            tip.Font = new Font("Microsoft YaHei UI", 8F);
            tip.Location = new Point(30, ry + 32);
            tip.AutoSize = true;
            pageReset.Controls.Add(tip);

            // ===== C盘清理页 =====
            pageClean = new Panel();
            pageClean.Dock = DockStyle.Fill;
            pageClean.BackColor = C_MAIN;
            pageClean.Visible = false;
            mainArea.Controls.Add(pageClean);

            Label cTitle = new Label();
            cTitle.Text = "C盘清理";
            cTitle.ForeColor = C_TEXT;
            cTitle.Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold);
            cTitle.Location = new Point(30, 26);
            cTitle.AutoSize = true;
            pageClean.Controls.Add(cTitle);

            Label cSub = new Label();
            cSub.Text = "深度清理 Windows 系统与用户缓存垃圾，释放磁盘空间";
            cSub.ForeColor = C_SUB;
            cSub.Font = new Font("Microsoft YaHei UI", 9F);
            cSub.Location = new Point(30, 58);
            cSub.AutoSize = true;
            pageClean.Controls.Add(cSub);

            Button cBack = MakeBtn("返回定时", C_ON, Color.Black, 620, 26, 90, 28);
            cBack.Click += (s, e) => ShowCleanPage(false);
            pageClean.Controls.Add(cBack);

            string[,] cleans = {
                { "平衡保护清理", "白名单保护浏览器、云笔记与办公数据，温和安全（推荐）", "v8", "低" },
                { "极致深度清理", "全量扫描用户目录 Temp / Cache / Log 与系统缓存并清空", "full", "中" },
            };
            int cy = 100;
            for (int i = 0; i < 2; i++)
            {
                Panel card = new Panel();
                card.BackColor = C_BUBBLE;
                card.Location = new Point(30, cy);
                card.Size = new Size(700, 96);
                card.Paint += (s, e) => { using (Pen p = new Pen(C_BORDER)) e.Graphics.DrawRectangle(p, 0, 0, 699, 95); };
                pageClean.Controls.Add(card);

                Label ctn = new Label();
                ctn.Text = cleans[i, 0];
                ctn.ForeColor = C_TEXT;
                ctn.Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold);
                ctn.Location = new Point(20, 12);
                ctn.AutoSize = true;
                card.Controls.Add(ctn);

                Label ctd = new Label();
                ctd.Text = cleans[i, 1];
                ctd.ForeColor = C_SUB;
                ctd.Font = new Font("Microsoft YaHei UI", 8.5F);
                ctd.Location = new Point(20, 44);
                ctd.AutoSize = true;
                card.Controls.Add(ctd);

                Label risk = new Label();
                risk.Text = "风险：" + cleans[i, 3];
                risk.ForeColor = (cleans[i, 3] == "低") ? C_ON : Color.FromArgb(249, 115, 22);
                risk.Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold);
                risk.Location = new Point(20, 68);
                risk.AutoSize = true;
                card.Controls.Add(risk);

                string cmode = cleans[i, 2];
                Button cgo = MakeBtn("一键清理", C_ON, Color.Black, 560, 30, 110, 36);
                cgo.Click += (s, e) => RunClean(cmode);
                card.Controls.Add(cgo);

                cy += 108;
            }

            Label cwarn = new Label();
            cwarn.Text = "⚠ 清理内容为系统与软件的可再生缓存/临时文件，不会触碰个人文件与文档；首次执行会弹出系统 UAC 授权窗口，请点击「是」。";
            cwarn.ForeColor = Color.FromArgb(240, 160, 90);
            cwarn.Font = new Font("Microsoft YaHei UI", 8.5F);
            cwarn.Location = new Point(30, cy + 4);
            cwarn.AutoSize = true;
            pageClean.Controls.Add(cwarn);

            Label cnote = new Label();
            cnote.Text = "清理引擎基于开源项目 Light-Help（Cotton059 · GPL-3.0）本地化集成：删除前统计大小，完成后报告释放空间。";
            cnote.ForeColor = C_SUB;
            cnote.Font = new Font("Microsoft YaHei UI", 8F);
            cnote.Location = new Point(30, cy + 30);
            cnote.AutoSize = true;
            pageClean.Controls.Add(cnote);

            // ===== 游戏作弊页 =====
            pageGcm = new Panel();
            pageGcm.Dock = DockStyle.Fill;
            pageGcm.BackColor = C_MAIN;
            pageGcm.Visible = false;
            mainArea.Controls.Add(pageGcm);

            Label gTitle = new Label();
            gTitle.Text = "游戏作弊";
            gTitle.ForeColor = C_TEXT;
            gTitle.Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold);
            gTitle.Location = new Point(30, 26);
            gTitle.AutoSize = true;
            pageGcm.Controls.Add(gTitle);

            Label gSub = new Label();
            gSub.Text = "单机游戏修改器管理：多来源聚合 · 中英搜索 · 一键下载归档（Game Cheats Manager）";
            gSub.ForeColor = C_SUB;
            gSub.Font = new Font("Microsoft YaHei UI", 9F);
            gSub.Location = new Point(30, 58);
            gSub.AutoSize = true;
            pageGcm.Controls.Add(gSub);

            Button gBack = MakeBtn("返回定时", C_ON, Color.Black, 620, 26, 90, 28);
            gBack.Click += (s, e) => ShowGcmPage(false);
            pageGcm.Controls.Add(gBack);

            Panel gCard = new Panel();
            gCard.BackColor = C_BUBBLE;
            gCard.Location = new Point(30, 100);
            gCard.Size = new Size(700, 110);
            gCard.Paint += (s, e) => { using (Pen p = new Pen(C_BORDER)) e.Graphics.DrawRectangle(p, 0, 0, 699, 109); };
            pageGcm.Controls.Add(gCard);

            Label gN = new Label();
            gN.Text = "启动 Game Cheats Manager v2.5.4";
            gN.ForeColor = C_TEXT;
            gN.Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold);
            gN.Location = new Point(20, 12);
            gN.AutoSize = true;
            gCard.Controls.Add(gN);

            Label gD = new Label();
            gD.Text = "汇集风灵月影 / 小幸姐 / CE 脚本 / GCM 专属 / 社区上传五大来源；\n输入中英文游戏名即时搜索，双击即可下载、解压并归档管理，启动时自动检查更新。";
            gD.ForeColor = C_SUB;
            gD.Font = new Font("Microsoft YaHei UI", 8.5F);
            gD.Location = new Point(20, 40);
            gD.AutoSize = true;
            gCard.Controls.Add(gD);

            Label gRisk = new Label();
            gRisk.Text = T("适用：单机游戏（严格仅限单人模式）", "For single-player games only");
            gRisk.ForeColor = C_ON;
            gRisk.Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold);
            gRisk.Location = new Point(20, 84);
            gRisk.AutoSize = true;
            gCard.Controls.Add(gRisk);

            Button gGo = MakeBtn(T("启动", "Launch"), C_ON, Color.Black, 560, 36, 110, 40);
            gGo.Click += (s, e) => RunGcm();
            gCard.Controls.Add(gGo);

            Label gWarn = new Label();
            gWarn.Text = T("⚠ 修改器会改动游戏内存，部分杀毒软件可能误报，可在 GCM 内一键加入 Windows Defender 白名单；首次启动会自动联网同步修改器索引。", "⚠ Trainers modify game memory; some antivirus may flag them. GCM can add a Defender whitelist in one click; the index syncs on first launch.");
            gWarn.ForeColor = Color.FromArgb(240, 160, 90);
            gWarn.Font = new Font("Microsoft YaHei UI", 8.5F);
            gWarn.Location = new Point(30, 222);
            gWarn.AutoSize = true;
            pageGcm.Controls.Add(gWarn);

            Label gNote = new Label();
            gNote.Text = T("游戏作弊功能基于开源项目 Game-Cheats-Manager（dyang886 · GPL-3.0）集成，官方站点 gamezonelabs.com。", "Powered by open-source Game-Cheats-Manager (dyang886 · GPL-3.0), official site gamezonelabs.com.");
            gNote.ForeColor = C_SUB;
            gNote.Font = new Font("Microsoft YaHei UI", 8F);
            gNote.Location = new Point(30, 248);
            gNote.AutoSize = true;
            pageGcm.Controls.Add(gNote);

            // ---------- 视频下载页 ----------
            pageVideo = new Panel();
            pageVideo.Dock = DockStyle.Fill;
            pageVideo.BackColor = C_MAIN;
            pageVideo.Visible = false;
            mainArea.Controls.Add(pageVideo);

            Label vTitle = new Label();
            vTitle.Text = T("视频下载", "Video Download");
            vTitle.ForeColor = C_TEXT;
            vTitle.Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold);
            vTitle.Location = new Point(30, 26);
            vTitle.AutoSize = true;
            pageVideo.Controls.Add(vTitle);

            Label vSub = new Label();
            vSub.Text = T("支持 B站 / 抖音 / 快手 / YouTube / 微博 / 腾讯 / 爱奇艺 / 优酷等 1000+ 平台 · 粘贴链接即可解析下载", "Supports 1000+ sites: Bilibili, Douyin, Kuaishou, YouTube, Weibo, Tencent, iQiyi, Youku... Paste a link to parse");
            vSub.ForeColor = C_SUB;
            vSub.Font = new Font("Microsoft YaHei UI", 9F);
            vSub.Location = new Point(30, 58);
            vSub.AutoSize = true;
            pageVideo.Controls.Add(vSub);

            Button vBack = MakeBtn(T("返回定时", "Back"), C_ON, Color.Black, 620, 26, 90, 28);
            vBack.Click += (s, e) => ShowVideoPage(false);
            pageVideo.Controls.Add(vBack);

            Label vUrlL = new Label();
            vUrlL.Text = T("视频链接", "Video URL");
            vUrlL.ForeColor = C_TEXT;
            vUrlL.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            vUrlL.Location = new Point(30, 100);
            vUrlL.AutoSize = true;
            pageVideo.Controls.Add(vUrlL);

            txtVUrl = MakeInput(30, 124, 560, 34, T("粘贴视频 / 播放列表链接，如 https://www.bilibili.com/video/BVxxxx", "Paste video / playlist URL, e.g. https://www.youtube.com/watch?v=xxxx"));
            pageVideo.Controls.Add(txtVUrl);

            btnVParse = MakeBtn(T("解析", "Parse"), Color.FromArgb(59, 130, 246), Color.White, 602, 124, 80, 34);
            btnVParse.Click += (s, e) => VideoParse();
            pageVideo.Controls.Add(btnVParse);

            lblVInfo = new Label();
            lblVInfo.Text = T("尚未解析 · 输入链接后点击「解析」获取清晰度列表", "Not parsed yet · paste a link and press Parse");
            lblVInfo.ForeColor = C_SUB;
            lblVInfo.Font = new Font("Microsoft YaHei UI", 8.5F);
            lblVInfo.Location = new Point(30, 168);
            lblVInfo.AutoSize = true;
            pageVideo.Controls.Add(lblVInfo);

            Label vFmtL = new Label();
            vFmtL.Text = T("清晰度 / 格式", "Quality / Format");
            vFmtL.ForeColor = C_TEXT;
            vFmtL.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            vFmtL.Location = new Point(30, 196);
            vFmtL.AutoSize = true;
            pageVideo.Controls.Add(vFmtL);

            cmbVFormat = new ComboBox();
            cmbVFormat.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbVFormat.BackColor = C_INPUT;
            cmbVFormat.ForeColor = C_INPUT_TEXT;
            cmbVFormat.FlatStyle = FlatStyle.Flat;
            cmbVFormat.Location = new Point(30, 220);
            cmbVFormat.Size = new Size(430, 30);
            pageVideo.Controls.Add(cmbVFormat);

            btnVDl = MakeBtn(T("开始下载", "Download"), C_ON, Color.Black, 472, 220, 100, 30);
            btnVDl.Click += (s, e) => VideoDownload();
            pageVideo.Controls.Add(btnVDl);

            Label vDirL = new Label();
            vDirL.Text = T("保存到", "Save to");
            vDirL.ForeColor = C_TEXT;
            vDirL.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            vDirL.Location = new Point(30, 266);
            vDirL.AutoSize = true;
            pageVideo.Controls.Add(vDirL);

            lblVDir = new Label();
            lblVDir.Text = vOutDir;
            lblVDir.ForeColor = C_SUB;
            lblVDir.Font = new Font("Microsoft YaHei UI", 8.5F);
            lblVDir.Location = new Point(30, 290);
            lblVDir.AutoSize = true;
            pageVideo.Controls.Add(lblVDir);

            Button vBrowse = MakeBtn(T("更改目录", "Change"), Color.FromArgb(59, 130, 246), Color.White, 470, 286, 90, 26);
            vBrowse.Click += (s, e) => PickVideoDir();
            pageVideo.Controls.Add(vBrowse);

            Button vOpen = MakeBtn(T("打开文件夹", "Open Folder"), C_BUBBLE_ALT(), Color.White, 572, 286, 98, 26);
            vOpen.Click += (s, e) => OpenVideoDir();
            pageVideo.Controls.Add(vOpen);

            lvVideos = MakeDarkList(langEn ? new string[] { "File", "Size", "Progress", "Speed", "Status" } : new string[] { "文件名", "大小", "进度", "速度", "状态" }, new int[] { 330, 80, 90, 90, 100 }, 30, 330, 700, 250);
            pageVideo.Controls.Add(lvVideos);

            lblVTip = new Label();
            lblVTip.Text = T("提示：默认选择单文件格式无需额外组件；带「需FFmpeg」的高清格式会在首次使用时自动下载 FFmpeg 合并工具。", "Tip: single-file formats need no extra tools; HD formats marked FFmpeg auto-download the merger on first use.");
            lblVTip.ForeColor = Color.FromArgb(240, 160, 90);
            lblVTip.Font = new Font("Microsoft YaHei UI", 8F);
            lblVTip.Location = new Point(30, 598);
            lblVTip.AutoSize = true;
            pageVideo.Controls.Add(lblVTip);

            // ---------- 音乐下载页 ----------
            pageMusic = new Panel();
            pageMusic.Dock = DockStyle.Fill;
            pageMusic.BackColor = C_MAIN;
            pageMusic.Visible = false;
            mainArea.Controls.Add(pageMusic);

            Label mTitle = new Label();
            mTitle.Text = T("音乐下载", "Music Download");
            mTitle.ForeColor = C_TEXT;
            mTitle.Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold);
            mTitle.Location = new Point(30, 26);
            mTitle.AutoSize = true;
            pageMusic.Controls.Add(mTitle);

            Label mSub = new Label();
            mSub.Text = T("网易云曲库在线搜索 · 一键下载 MP3 · 也支持粘贴网易云/QQ音乐等歌曲链接", "Search NetEase library & download MP3 · paste a song link to download");
            mSub.ForeColor = C_SUB;
            mSub.Font = new Font("Microsoft YaHei UI", 9F);
            mSub.Location = new Point(30, 58);
            mSub.AutoSize = true;
            pageMusic.Controls.Add(mSub);

            Button mBack = MakeBtn(T("返回定时", "Back"), C_ON, Color.Black, 620, 26, 90, 28);
            mBack.Click += (s, e) => ShowMusicPage(false);
            pageMusic.Controls.Add(mBack);

            txtMSearch = MakeInput(30, 100, 430, 34, T("输入歌名 / 歌手搜索，如：夜曲", "Search by song / artist, e.g. Nocturne"));
            pageMusic.Controls.Add(txtMSearch);

            btnMSearch = MakeBtn(T("搜索", "Search"), Color.FromArgb(59, 130, 246), Color.White, 472, 100, 100, 34);
            btnMSearch.Click += (s, e) => MusicSearch();
            pageMusic.Controls.Add(btnMSearch);

            btnMUrl = MakeBtn(T("粘贴链接下载", "Link Download"), C_BUBBLE_ALT(), Color.White, 584, 100, 116, 34);
            btnMUrl.Click += (s, e) => MusicByUrl();
            pageMusic.Controls.Add(btnMUrl);

            lvSongs = MakeDarkList(langEn ? new string[] { "Song", "Artist", "Album", "Length" } : new string[] { "歌曲", "歌手", "专辑", "时长" }, new int[] { 250, 150, 210, 70 }, 30, 146, 700, 320);
            lvSongs.MouseDoubleClick += (s, e) => MusicDownloadSelected();
            pageMusic.Controls.Add(lvSongs);

            btnMDl = MakeBtn(T("下载选中歌曲", "Download Selected"), C_ON, Color.Black, 30, 480, 160, 34);
            btnMDl.Click += (s, e) => MusicDownloadSelected();
            pageMusic.Controls.Add(btnMDl);

            btnMOpen = MakeBtn(T("打开下载目录", "Open Folder"), C_BUBBLE_ALT(), Color.White, 200, 480, 130, 34);
            btnMOpen.Click += (s, e) => OpenMusicDir();
            pageMusic.Controls.Add(btnMOpen);

            pbMusic = new ProgressBar();
            pbMusic.Minimum = 0;
            pbMusic.Maximum = 100;
            pbMusic.Style = ProgressBarStyle.Continuous;
            pbMusic.Location = new Point(30, 534);
            pbMusic.Size = new Size(700, 16);
            pbMusic.Visible = false;
            pageMusic.Controls.Add(pbMusic);

            lblMStatus = new Label();
            lblMStatus.Text = T("搜索后双击歌曲即可下载，或选中后点击「下载选中歌曲」", "Double-click a song to download, or select & press Download Selected");
            lblMStatus.ForeColor = C_SUB;
            lblMStatus.Font = new Font("Microsoft YaHei UI", 8.5F);
            lblMStatus.Location = new Point(30, 562);
            lblMStatus.AutoSize = true;
            pageMusic.Controls.Add(lblMStatus);

            Panel topbar = new Panel();
            topbar.BackColor = C_MAIN;
            topbar.Location = new Point(0, 0);
            topbar.Size = new Size(760, 48);
            topbar.Paint += (s, e) =>
            {
                using (var br = new System.Drawing.Drawing2D.LinearGradientBrush(new Rectangle(0, 46, 760, 2), Color.FromArgb(61, 126, 255), Color.FromArgb(38, 43, 51), 0f))
                    e.Graphics.FillRectangle(br, 0, 46, 760, 2);
            };
            pageTimer.Controls.Add(topbar);

            // 页面标题左侧天蓝强调条
            Panel peerBar = new Panel();
            peerBar.BackColor = C_ACCENT;
            peerBar.Location = new Point(8, 14);
            peerBar.Size = new Size(4, 20);
            peerBar.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var path = UiKit.RoundedPath(new Rectangle(0, 0, 3, 19), 2))
                using (var br = new System.Drawing.Drawing2D.LinearGradientBrush(new Rectangle(0, 0, 3, 19), Color.FromArgb(91, 150, 255), Color.FromArgb(49, 102, 225), 90f))
                    e.Graphics.FillPath(br, path);
            };
            topbar.Controls.Add(peerBar);

            lblPeer = new Label();
            lblPeer.Text = T("关机", "Shutdown");
            lblPeer.ForeColor = C_TEXT;
            lblPeer.Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold);
            lblPeer.Location = new Point(22, 13);
            lblPeer.AutoSize = true;
            topbar.Controls.Add(lblPeer);

            lblTyping = new Label();
            lblTyping.Text = T("任务进行中 ···", "Task running ···");
            lblTyping.ForeColor = C_ON;
            lblTyping.Font = new Font("Microsoft YaHei UI", 8F);
            lblTyping.Location = new Point(84, 17);
            lblTyping.AutoSize = true;
            lblTyping.Visible = false;
            topbar.Controls.Add(lblTyping);

            // 服务运行中状态胶囊（深绿圆角底 + 绿字 + 电源图标）
            Panel pillRun = new Panel();
            pillRun.Location = new Point(452, 9);
            pillRun.Size = new Size(172, 30);
            pillRun.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle pr = new Rectangle(0, 0, 171, 29);
                using (GraphicsPath path = UiKit.RoundedPath(pr, 15))
                {
                    using (LinearGradientBrush br = new LinearGradientBrush(pr, Color.FromArgb(28, 70, 46), Color.FromArgb(18, 48, 32), 90f))
                        e.Graphics.FillPath(br, path);
                    // 液态玻璃高光
                    e.Graphics.SetClip(path);
                    using (LinearGradientBrush hl = new LinearGradientBrush(new Rectangle(0, 0, 171, 15), Color.FromArgb(70, 180, 255, 200), Color.FromArgb(0, 180, 255, 200), 90f))
                        e.Graphics.FillRectangle(hl, 0, 0, 171, 15);
                    e.Graphics.ResetClip();
                    using (Pen pp = new Pen(Color.FromArgb(52, 180, 110), 1.2f))
                        e.Graphics.DrawPath(pp, path);
                }
            };
            topbar.Controls.Add(pillRun);

            lblService = new Label();
            lblService.Text = T("服务运行中", "Service Running");
            lblService.ForeColor = C_ON;
            lblService.Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold);
            lblService.Location = new Point(462, 16);
            lblService.AutoSize = true;
            topbar.Controls.Add(lblService);

            // 运行状态图标（绿色电脑 + 电源符号，表示服务运行中）
            PictureBox picRun = new PictureBox();
            picRun.Location = new Point(lblService.Right + 3, 12);
            picRun.Size = new Size(30, 26);
            picRun.BackColor = Color.Transparent;
            picRun.Paint += (s, e) =>
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (Pen p = new Pen(C_ON, 1.7f))
                {
                    // 显示器屏幕
                    g.DrawRectangle(p, 2, 1, 20, 13);
                    // 底座
                    g.DrawLine(p, 7, 17, 17, 17);
                    g.DrawLine(p, 9, 20, 15, 20);
                    g.DrawLine(p, 7, 17, 9, 20);
                    g.DrawLine(p, 17, 17, 15, 20);
                    // 屏幕内电源符号（电脑关机/电源图标）
                    g.DrawEllipse(p, 8, 3, 8, 8);
                    g.DrawLine(p, 12, 1, 12, 4);
                }
            };
            topbar.Controls.Add(picRun);

            // 语言切换按钮（中 / EN）
            Button btnLang = new RoundBtn(Color.FromArgb(43, 58, 80), C_TEXT);
            btnLang.Text = langEn ? "中" : "EN";
            btnLang.Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold);
            btnLang.FlatStyle = FlatStyle.Flat;
            btnLang.FlatAppearance.BorderSize = 0;
            btnLang.Cursor = Cursors.Hand;
            btnLang.Size = new Size(38, 26);
            btnLang.Location = new Point(624, 11);
            btnLang.Click += (s, e) => ToggleLang();
            topbar.Controls.Add(btnLang);
            ToolTip tipTop = new ToolTip();
            tipTop.SetToolTip(btnLang, langEn ? "Switch to Chinese" : "切换到英文");

            // 任务卡（液态玻璃）
            Panel bubble1 = new Panel();
            bubble1.BackColor = C_BUBBLE;
            bubble1.Location = new Point(20, 58);
            bubble1.Size = new Size(560, 195);
            bubble1.Paint += (s, e) =>
                DrawGlassCard(e.Graphics, new Rectangle(0, 0, 559, 194), 14,
                    Color.FromArgb(50, 56, 68),
                    Color.FromArgb(34, 39, 47));
            pageTimer.Controls.Add(bubble1);

            Label t1 = new Label();
            t1.Text = T("当前任务", "Current Task");
            t1.ForeColor = C_SUB;
            t1.Font = new Font("Microsoft YaHei UI", 8F);
            t1.Location = new Point(16, 10);
            t1.AutoSize = true;
            bubble1.Controls.Add(t1);
            Panel line1 = new Panel();
            line1.BackColor = C_BORDER;
            line1.Location = new Point(70, 17);
            line1.Size = new Size(474, 1);
            bubble1.Controls.Add(line1);

            ringPanel = new Panel();
            ringPanel.BackColor = Color.Transparent;
            ringPanel.Location = new Point(24, 36);
            ringPanel.Size = new Size(150, 150);
            ringPanel.Paint += RingPaint;
            bubble1.Controls.Add(ringPanel);

            lblTime = new Label();
            lblTime.Text = "--:--:--";
            lblTime.ForeColor = C_TEXT;
            lblTime.Font = new Font("Consolas", 22F, FontStyle.Bold);
            lblTime.Location = new Point(204, 52);
            lblTime.AutoSize = true;
            bubble1.Controls.Add(lblTime);

            lblTimeSub = new Label();
            lblTimeSub.Text = T("空闲 · 未设置", "Idle · Not set");
            lblTimeSub.ForeColor = C_SUB;
            lblTimeSub.Font = new Font("Microsoft YaHei UI", 8.5F);
            lblTimeSub.Location = new Point(208, 94);
            lblTimeSub.AutoSize = true;
            bubble1.Controls.Add(lblTimeSub);

            lblStatus = new Label();
            lblStatus.Text = T("状态：空闲", "Status: Idle");
            lblStatus.ForeColor = C_SUB;
            lblStatus.Font = new Font("Microsoft YaHei UI", 10F);
            lblStatus.Location = new Point(204, 120);
            lblStatus.AutoSize = true;
            bubble1.Controls.Add(lblStatus);

            lblDetail = new Label();
            lblDetail.Text = T("选择操作与计时方式，点击「开始任务」", "Choose an action & timer mode, then press Start");
            lblDetail.ForeColor = Color.FromArgb(109, 118, 134);
            lblDetail.Font = new Font("Microsoft YaHei UI", 9F);
            lblDetail.Location = new Point(204, 148);
            lblDetail.Size = new Size(340, 40);
            lblDetail.TextAlign = ContentAlignment.TopLeft;
            bubble1.Controls.Add(lblDetail);

            // 计时方式卡（液态玻璃）
            Panel bubble2 = new Panel();
            bubble2.BackColor = C_BUBBLE;
            bubble2.Location = new Point(20, 263);
            bubble2.Size = new Size(560, 185);
            bubble2.Paint += (s, e) =>
                DrawGlassCard(e.Graphics, new Rectangle(0, 0, 559, 184), 12,
                    Color.FromArgb(50, 56, 68),
                    Color.FromArgb(34, 39, 47));
            pageTimer.Controls.Add(bubble2);

            Label t2 = new Label();
            t2.Text = T("计时方式", "Timer Mode");
            t2.ForeColor = C_SUB;
            t2.Font = new Font("Microsoft YaHei UI", 8F);
            t2.Location = new Point(16, 10);
            t2.AutoSize = true;
            bubble2.Controls.Add(t2);
            Panel line2 = new Panel();
            line2.BackColor = C_BORDER;
            line2.Location = new Point(70, 17);
            line2.Size = new Size(474, 1);
            bubble2.Controls.Add(line2);

            rbCountdown = new RadioButton();
            rbCountdown.Text = T("倒计时", "Countdown");
            rbCountdown.ForeColor = C_TEXT;
            rbCountdown.AutoSize = false;
            rbCountdown.Size = new Size(92, 24);
            rbCountdown.Location = new Point(18, 30);
            rbCountdown.Checked = true;
            rbCountdown.CheckedChanged += (s, e) => UpdateModeUI();
            bubble2.Controls.Add(rbCountdown);

            rbDatetime = new RadioButton();
            rbDatetime.Text = T("指定时间", "At Time");
            rbDatetime.ForeColor = C_TEXT;
            rbDatetime.AutoSize = false;
            rbDatetime.Size = new Size(92, 24);
            rbDatetime.Location = new Point(110, 30);
            rbDatetime.CheckedChanged += (s, e) => UpdateModeUI();
            bubble2.Controls.Add(rbDatetime);

            rbDaily = new RadioButton();
            rbDaily.Text = T("每天重复", "Daily");
            rbDaily.ForeColor = C_TEXT;
            rbDaily.AutoSize = false;
            rbDaily.Size = new Size(92, 24);
            rbDaily.Location = new Point(210, 30);
            rbDaily.CheckedChanged += (s, e) => UpdateModeUI();
            bubble2.Controls.Add(rbDaily);

            rbWeekly = new RadioButton();
            rbWeekly.Text = T("每周指定", "Weekly");
            rbWeekly.ForeColor = C_TEXT;
            rbWeekly.AutoSize = false;
            rbWeekly.Size = new Size(92, 24);
            rbWeekly.Location = new Point(310, 30);
            rbWeekly.CheckedChanged += (s, e) => UpdateModeUI();
            bubble2.Controls.Add(rbWeekly);

            // 倒计时输入组（不透明背景，避免覆盖上层控件；先添加面板，radio在Z序顶层）
            pCountdown = new Panel();
            pCountdown.BackColor = C_BUBBLE;
            pCountdown.Location = new Point(12, 58);
            pCountdown.Size = new Size(536, 118);
            bubble2.Controls.Add(pCountdown);
            pCountdown.SendToBack();
            numH = MakeNum(0, 999, 0);
            numM = MakeNum(0, 59, 30);
            numS = MakeNum(0, 59, 0);
            AddField(pCountdown, T("小时", "Hours"), numH, 30, 70);
            AddField(pCountdown, T("分钟", "Minutes"), numM, 180, 70);
            AddField(pCountdown, T("秒", "Seconds"), numS, 330, 70);
            Label cdHint = new Label();
            cdHint.Text = T("从点击「开始任务」起倒计时", "Counts down from pressing Start");
            cdHint.ForeColor = Color.FromArgb(90, 99, 115);
            cdHint.Font = new Font("Microsoft YaHei UI", 8F);
            cdHint.Location = new Point(30, 24);
            cdHint.AutoSize = true;
            pCountdown.Controls.Add(cdHint);

            // 指定时间输入组
            pDatetime = new Panel();
            pDatetime.BackColor = C_BUBBLE;
            pDatetime.Location = new Point(12, 58);
            pDatetime.Size = new Size(536, 118);
            pDatetime.Visible = false;
            bubble2.Controls.Add(pDatetime);
            dtpTime = MakeTimePicker(false);
            dtpTime.Location = new Point(30, 40);
            dtpTime.Size = new Size(260, 28);
            pDatetime.Controls.Add(dtpTime);
            Label dtHint = new Label();
            dtHint.Text = T("到指定日期时间执行（仅一次）", "Runs once at the chosen date & time");
            dtHint.ForeColor = Color.FromArgb(90, 99, 115);
            dtHint.Font = new Font("Microsoft YaHei UI", 8F);
            dtHint.Location = new Point(30, 78);
            dtHint.AutoSize = true;
            pDatetime.Controls.Add(dtHint);

            // 每天重复输入组
            pDaily = new Panel();
            pDaily.BackColor = C_BUBBLE;
            pDaily.Location = new Point(12, 58);
            pDaily.Size = new Size(536, 118);
            pDaily.Visible = false;
            bubble2.Controls.Add(pDaily);
            dtpDaily = MakeTimePicker(true);
            dtpDaily.Location = new Point(30, 40);
            dtpDaily.Size = new Size(160, 28);
            pDaily.Controls.Add(dtpDaily);
            Label dyHint = new Label();
            dyHint.Text = T("每天这个时间自动执行，可持续运行", "Runs every day at this time");
            dyHint.ForeColor = Color.FromArgb(90, 99, 115);
            dyHint.Font = new Font("Microsoft YaHei UI", 8F);
            dyHint.Location = new Point(30, 78);
            dyHint.AutoSize = true;
            pDaily.Controls.Add(dyHint);

            // 每周指定输入组
            pWeekly = new Panel();
            pWeekly.BackColor = C_BUBBLE;
            pWeekly.Location = new Point(12, 58);
            pWeekly.Size = new Size(536, 118);
            pWeekly.Visible = false;
            bubble2.Controls.Add(pWeekly);
            string[] wd = langEn ? new[] { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" } : new[] { "一", "二", "三", "四", "五", "六", "日" };
            for (int i = 0; i < 7; i++)
            {
                CheckBox cb = new CheckBox();
                cb.Text = langEn ? wd[i] : "周" + wd[i];
                cb.ForeColor = C_SUB;
                cb.Font = new Font("Microsoft YaHei UI", 8.5F);
                cb.Location = new Point(30 + i * 70, 30);
                cb.AutoSize = true;
                weekCb[i] = cb;
                pWeekly.Controls.Add(cb);
            }
            dtpWeekly = MakeTimePicker(true);
            dtpWeekly.Location = new Point(30, 68);
            dtpWeekly.Size = new Size(160, 28);
            pWeekly.Controls.Add(dtpWeekly);
            Label wkHint = new Label();
            wkHint.Text = T("选中的星期到点自动执行", "Runs on the selected weekdays");
            wkHint.ForeColor = Color.FromArgb(90, 99, 115);
            wkHint.Font = new Font("Microsoft YaHei UI", 8F);
            wkHint.Location = new Point(200, 72);
            wkHint.AutoSize = true;
            pWeekly.Controls.Add(wkHint);

            // 设置卡（液态玻璃）
            Panel bubble3 = new Panel();
            bubble3.BackColor = C_BUBBLE;
            bubble3.Location = new Point(20, 458);
            bubble3.Size = new Size(560, 72);
            bubble3.Paint += (s, e) =>
                DrawGlassCard(e.Graphics, new Rectangle(0, 0, 559, 71), 12,
                    Color.FromArgb(50, 56, 68),
                    Color.FromArgb(34, 39, 47));
            pageTimer.Controls.Add(bubble3);

            chkConfirm = new CheckBox();
            chkConfirm.Text = T("到点弹窗确认", "Confirm popup at time");
            chkConfirm.ForeColor = C_SUB;
            chkConfirm.Font = new Font("Microsoft YaHei UI", 8.5F);
            chkConfirm.Checked = true;
            chkConfirm.Location = new Point(18, 14);
            chkConfirm.AutoSize = true;
            bubble3.Controls.Add(chkConfirm);

            chkSound = new CheckBox();
            chkSound.Text = T("执行提示音", "Play sound");
            chkSound.ForeColor = C_SUB;
            chkSound.Font = new Font("Microsoft YaHei UI", 8.5F);
            chkSound.Checked = true;
            chkSound.Location = new Point(150, 14);
            chkSound.AutoSize = true;
            bubble3.Controls.Add(chkSound);

            Label ls = new Label();
            ls.Text = T("确认超时", "Confirm timeout");
            ls.ForeColor = C_SUB;
            ls.Font = new Font("Microsoft YaHei UI", 8.5F);
            ls.Location = new Point(286, 16);
            ls.AutoSize = true;
            bubble3.Controls.Add(ls);

            numConfirm = new NumericUpDown();
            numConfirm.Minimum = 10; numConfirm.Maximum = 600; numConfirm.Value = 60;
            numConfirm.BackColor = C_INPUT;
            numConfirm.ForeColor = C_TEXT;
            numConfirm.BorderStyle = BorderStyle.FixedSingle;
            numConfirm.Font = new Font("Consolas", 10F);
            numConfirm.Location = new Point(356, 10);
            numConfirm.Size = new Size(58, 26);
            bubble3.Controls.Add(numConfirm);

            Label ls2 = new Label();
            ls2.Text = T("秒自动执行", "s auto-execute");
            ls2.ForeColor = C_SUB;
            ls2.Font = new Font("Microsoft YaHei UI", 8.5F);
            ls2.Location = new Point(420, 16);
            ls2.AutoSize = true;
            bubble3.Controls.Add(ls2);

            btnLog = MakeBtn(T("任务日志", "Task Log"), Color.FromArgb(43, 58, 80), C_TEXT, 16, 40, 130, 24);
            btnLog.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
            btnLog.Click += (s, e) => ShowLog();
            bubble3.Controls.Add(btnLog);

            // 底部操作栏
            Panel inputbar = new Panel();
            inputbar.BackColor = Color.FromArgb(30, 34, 41);
            inputbar.Location = new Point(0, 540);
            inputbar.Size = new Size(600, 100);
            inputbar.Paint += (s, e) => { using (Pen p = new Pen(C_BORDER)) e.Graphics.DrawLine(p, 0, 0, 600, 0); };
            pageTimer.Controls.Add(inputbar);

            Label hint = new Label();
            hint.Text = T("提示：建议开启「到点弹窗确认」，防止误操作", "Tip: enable Confirm popup to avoid accidental shutdown");
            hint.ForeColor = Color.FromArgb(109, 118, 134);
            hint.Font = new Font("Microsoft YaHei UI", 8F);
            hint.Location = new Point(20, 10);
            hint.AutoSize = true;
            inputbar.Controls.Add(hint);

            btnStart = MakeBtn(T("▶ 开始任务", "▶ Start Task"), Color.FromArgb(43, 95, 208), Color.White, 20, 38, 170, 48);
            btnStart.Click += (s, e) => StartTask();
            inputbar.Controls.Add(btnStart);

            btnCancel = MakeBtn(T("✖ 取消任务", "✖ Cancel"), C_BUBBLE_ALT(), Color.FromArgb(154, 164, 178), 200, 38, 170, 48);
            btnCancel.Click += (s, e) => CancelTask();
            inputbar.Controls.Add(btnCancel);

            btnImm = MakeBtn(T("⚡ 立即执行", "⚡ Run Now"), Color.FromArgb(196, 61, 43), Color.White, 380, 38, 170, 48);
            btnImm.Click += (s, e) => Immediate();
            inputbar.Controls.Add(btnImm);

            UpdateModeUI();
        }

        NumericUpDown MakeNum(int min, int max, int val)
        {
            NumericUpDown n = new NumericUpDown();
            n.Minimum = min; n.Maximum = max; n.Value = val;
            n.BackColor = C_INPUT;
            n.ForeColor = C_TEXT;
            n.BorderStyle = BorderStyle.FixedSingle;
            n.Font = new Font("Consolas", 11F);
            return n;
        }

        DateTimePicker MakeTimePicker(bool timeOnly)
        {
            DateTimePicker d = new DateTimePicker();
            d.Format = DateTimePickerFormat.Custom;
            d.CustomFormat = timeOnly ? "HH:mm" : "yyyy-MM-dd HH:mm:ss";
            d.ShowUpDown = timeOnly;
            d.BackColor = C_INPUT;
            d.ForeColor = C_TEXT;
            return d;
        }

        void AddField(Control parent, string label, Control ctrl, int x, int y)
        {
            Label l = new Label();
            l.Text = label;
            l.ForeColor = C_SUB;
            l.Font = new Font("Microsoft YaHei UI", 8F);
            l.Location = new Point(x, y - 18);
            l.AutoSize = true;
            parent.Controls.Add(l);
            ctrl.Location = new Point(x, y);
            ctrl.Size = new Size(120, 30);
            parent.Controls.Add(ctrl);
        }

        Button MakeBtn(string text, Color bg, Color fg, int x, int y, int w, int h)
        {
            RoundBtn b = new RoundBtn(bg, fg);
            b.Text = text;
            b.ForeColor = fg;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            b.Cursor = Cursors.Hand;
            b.Location = new Point(x, y);
            b.Size = new Size(w, h);
            return b;
        }

        Color C_BUBBLE_ALT() { return Color.FromArgb(51, 58, 70); }

        TextBox MakeInput(int x, int y, int w, int h, string hint)
        {
            TextBox t = new TextBox();
            t.Location = new Point(x, y);
            t.Size = new Size(w, h);
            t.BackColor = C_INPUT;
            t.ForeColor = C_INPUT_TEXT;
            t.BorderStyle = BorderStyle.FixedSingle;
            t.Font = new Font("Microsoft YaHei UI", 9.5F);
            return t;
        }

        ListView MakeDarkList(string[] cols, int[] widths, int x, int y, int w, int h)
        {
            ListView lv = new ListView();
            lv.View = View.Details;
            lv.FullRowSelect = true;
            lv.GridLines = true;
            lv.BorderStyle = BorderStyle.FixedSingle;
            lv.BackColor = C_INPUT;
            lv.ForeColor = C_INPUT_TEXT;
            lv.Font = new Font("Microsoft YaHei UI", 8.5F);
            for (int i = 0; i < cols.Length; i++)
                lv.Columns.Add(cols[i], widths[i]);
            lv.Location = new Point(x, y);
            lv.Size = new Size(w, h);
            return lv;
        }

        void DrawCentered(Graphics g, Control c, string text, float size, Color color)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;
                using (Font f = new Font("Microsoft YaHei UI", size, FontStyle.Bold))
                using (SolidBrush b = new SolidBrush(color))
                    g.DrawString(text, f, b, new RectangleF(0, 0, c.Width, c.Height), sf);
            }
        }

        // 液态玻璃卡片：半透明渐变底 + 顶部高光反射 + 冷色描边
        void DrawGlassCard(Graphics g, Rectangle rc, int radius, Color baseTop, Color baseBottom)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = UiKit.RoundedPath(rc, radius))
            {
                g.SetClip(path);
                using (LinearGradientBrush br = new LinearGradientBrush(rc, baseTop, baseBottom, 90f))
                    g.FillRectangle(br, rc);
                // 顶部玻璃高光（液态玻璃反射）
                using (LinearGradientBrush hl = new LinearGradientBrush(
                    new Rectangle(rc.X, rc.Y, rc.Width, Math.Max(rc.Height / 2, 30)),
                    Color.FromArgb(42, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
                    g.FillRectangle(hl, new Rectangle(rc.X, rc.Y, rc.Width, Math.Max(rc.Height / 2, 30)));
                g.ResetClip();
                using (Pen pp = new Pen(Color.FromArgb(85, 185, 215, 255), 1.1f))
                    g.DrawPath(pp, path);
            }
        }

        void RingPaint(object s, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rect = new Rectangle(6, 6, ringPanel.Width - 12, ringPanel.Height - 12);
            float w = 11F;
            // 背景双环：外深内浅，增强层次
            using (Pen p1 = new Pen(Color.FromArgb(48, 55, 68), w)) { g.DrawArc(p1, rect, 0, 360); }
            using (Pen p2 = new Pen(Color.FromArgb(70, 79, 96), 2.2F)) { g.DrawArc(p2, rect, 0, 360); }
            float frac = 0F;
            if (taskActive && totalSec > 0)
            {
                double rem = (endTime - DateTime.Now).TotalSeconds;
                frac = (float)Math.Max(0, Math.Min(1, rem / totalSec));
            }
            if (frac > 0)
            {
                float sweep = (float)(frac * 360);
                using (Pen fgPen = new Pen(curActionColor, w) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawArc(fgPen, rect, -90, sweep);
                // 弧线头部亮点，增加精致感
                double head = (-90 + sweep) * Math.PI / 180.0;
                float hx = rect.X + rect.Width / 2F + (float)(Math.Cos(head) * (rect.Width / 2F));
                float hy = rect.Y + rect.Height / 2F + (float)(Math.Sin(head) * (rect.Height / 2F));
                using (SolidBrush hb = new SolidBrush(UiKit.Lighten(curActionColor, 70)))
                    g.FillEllipse(hb, hx - 3.5F, hy - 3.5F, 7F, 7F);
            }
            // 中间百分比
            string pct = taskActive ? ((int)(frac * 100)).ToString() + "%" : "--";
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;
                using (Font f = new Font("Consolas", 20F, FontStyle.Bold))
                using (SolidBrush b = new SolidBrush(taskActive ? curActionColor : C_SUB))
                    g.DrawString(pct, f, b, new RectangleF(0, 0, ringPanel.Width, ringPanel.Height), sf);
            }
        }

        void UpdateModeUI()
        {
            pCountdown.Visible = rbCountdown.Checked;
            pDatetime.Visible = rbDatetime.Checked;
            pDaily.Visible = rbDaily.Checked;
            pWeekly.Visible = rbWeekly.Checked;
        }

        // ================= 拼图 =================
        void ShowPuzzlePage(bool show)
        {
            pageTimer.Visible = !show;
            pagePuzzle.Visible = show;
            if (pageReset != null) pageReset.Visible = false;
            if (pageClean != null) pageClean.Visible = false;
            if (pageGcm != null) pageGcm.Visible = false;
            if (show)
            {
                if (pzImages == null || pzImages.Count == 0) LoadPuzzleImages();
                if (pzPieces == null) { BuildPuzzlePage(); NewPuzzle(); }
            }
            else
            {
                CleanupPzCache();
            }
        }

        void BuildPuzzlePage()
        {
            Label t = new Label();
            t.Text = T("拼图", "Puzzle");
            t.ForeColor = C_TEXT;
            t.Font = new Font("Microsoft YaHei UI", 15F, FontStyle.Bold);
            t.Location = new Point(24, 12);
            t.AutoSize = true;
            pagePuzzle.Controls.Add(t);

            Label h = new Label();
            h.Text = T("拖动左侧拼图块到右侧拼装区，全部放对即完成", "Drag pieces to the right board; place them all to win");
            h.ForeColor = C_SUB;
            h.Font = new Font("Microsoft YaHei UI", 9F);
            h.Location = new Point(92, 18);
            h.AutoSize = true;
            pagePuzzle.Controls.Add(h);

            btnPzPrev = MakeBtn(T("上一张", "Prev"), C_ON, Color.Black, 24, 48, 74, 28);
            btnPzPrev.Click += (s, e) => {
                if (!pzStarted) return;
                if (pzImgIdx > 0) pzImgIdx--;
                NewPuzzle();
            };
            pagePuzzle.Controls.Add(btnPzPrev);
            btnPzNext = MakeBtn(T("下一张", "Next"), C_ON, Color.Black, 104, 48, 74, 28);
            btnPzNext.Click += (s, e) => {
                if (!pzStarted) { pzStarted = true; pzImgIdx = 0; }   // 首次点击才联网下载
                else { pzImgIdx++; if (pzImgIdx > 2000000000) pzImgIdx = 0; }   // 无上限递增
                NewPuzzle();
            };
            pagePuzzle.Controls.Add(btnPzNext);
            btnPz3 = MakeBtn("3x3", C_ON, Color.Black, 192, 48, 56, 28);
            btnPz3.Click += (s, e) => { pzGrid = 3; NewPuzzle(); };
            pagePuzzle.Controls.Add(btnPz3);
            btnPz4 = MakeBtn("4x4", C_ON, Color.Black, 252, 48, 56, 28);
            btnPz4.Click += (s, e) => { pzGrid = 4; NewPuzzle(); };
            pagePuzzle.Controls.Add(btnPz4);
            btnPzRestart = MakeBtn(T("重新开始", "Restart"), C_ON, Color.Black, 320, 48, 90, 28);
            btnPzRestart.Click += (s, e) => NewPuzzle();
            pagePuzzle.Controls.Add(btnPzRestart);

            lblPzTime = new Label();
            lblPzTime.Text = T("用时 00:00", "Time 00:00");
            lblPzTime.ForeColor = C_ON;
            lblPzTime.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            lblPzTime.Location = new Point(430, 54);
            lblPzTime.AutoSize = true;
            pagePuzzle.Controls.Add(lblPzTime);

            Button btnBack = MakeBtn(T("返回定时", "Back"), C_ON, Color.Black, 620, 48, 90, 28);
            btnBack.Click += (s, e) => ShowPuzzlePage(false);
            pagePuzzle.Controls.Add(btnBack);

            Panel ga = new Panel();
            ga.BackColor = C_BG;
            ga.Location = new Point(pzGridAreaX - 4, pzGridAreaY - 4);
            ga.Size = new Size(pzGridAreaPx + 8, pzGridAreaPx + 8);
            ga.Paint += (s, e) =>
            {
                using (Pen p = new Pen(C_BORDER)) e.Graphics.DrawRectangle(p, 0, 0, ga.Width - 1, ga.Height - 1);
                using (Pen p = new Pen(Color.FromArgb(70, 130, 140, 150)))
                {
                    int sz = pzGridAreaPx / pzGrid;
                    for (int i = 1; i < pzGrid; i++)
                    {
                        e.Graphics.DrawLine(p, 4 + i * sz, 4, 4 + i * sz, 4 + pzGridAreaPx);
                        e.Graphics.DrawLine(p, 4, 4 + i * sz, 4 + pzGridAreaPx, 4 + i * sz);
                    }
                }
            };
            pagePuzzle.Controls.Add(ga);

            Label pt = new Label();
            pt.Text = "拼图块";
            pt.ForeColor = C_SUB;
            pt.Font = new Font("Microsoft YaHei UI", 9F);
            pt.Location = new Point(pzPieceAreaX, 78);
            pt.AutoSize = true;
            pagePuzzle.Controls.Add(pt);

            lblPzDone = new Label();
            lblPzDone.Text = "";
            lblPzDone.ForeColor = Color.FromArgb(80, 200, 120);
            lblPzDone.Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold);
            lblPzDone.Location = new Point(pzGridAreaX, pzGridAreaY + pzGridAreaPx + 14);
            lblPzDone.AutoSize = true;
            pagePuzzle.Controls.Add(lblPzDone);

            pzTimer = new System.Windows.Forms.Timer();
            pzTimer.Interval = 1000;
            pzTimer.Tick += (s, e) => { pzElapsed++; lblPzTime.Text = (langEn ? "Time " : "用时 ") + FmtTime(pzElapsed); };
        }

        static string FmtTime(int sec) { return (sec / 60).ToString("00") + ":" + (sec % 60).ToString("00"); }

        void LoadPuzzleImages()
        {
            try { Directory.CreateDirectory(pzCacheDir); } catch { }
            if (pzImages == null) pzImages = new List<Bitmap>();
            pzImages.Clear();
        }

        void PzEnsureList(int i)
        {
            if (pzImages == null) pzImages = new List<Bitmap>();
            while (pzImages.Count <= i) pzImages.Add(null);
        }

        // 无上限图源：前 24 张用已验证保底池，之后动态生成随机 Pexels 图源（失败自动跳转下一条）
        string PzUrlFor(int i)
        {
            if (i < PZ_URLS.Length) return PZ_URLS[i];
            int id = pzRnd.Next(100000, 5000000);
            return "https://images.pexels.com/photos/" + id + "/pexels-photo-" + id + ".jpeg?auto=compress&cs=tinysrgb&w=600";
        }

        Bitmap MakePzPlaceholder(int i)
        {
            var bmp = new Bitmap(540, 540);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.FromArgb(38 + (i % 3) * 26, 52, 82));
                using (Font f1 = new Font("Microsoft YaHei UI", 22F, FontStyle.Bold))
                    g.DrawString("第 " + (i + 1) + " 张", f1, new SolidBrush(Color.White), 205, 215);
                using (Font f2 = new Font("Microsoft YaHei UI", 12F))
                    g.DrawString("联网加载中…", f2, new SolidBrush(Color.FromArgb(150, 165, 195)), 210, 268);
            }
            return bmp;
        }

        // 当前索引图片：内存有则直接返回；缓存文件有则加载；否则占位图并触发按需下载
        Bitmap PzCurrentImage()
        {
            if (pzImgIdx < 0) return null;
            PzEnsureList(pzImgIdx);
            if (pzImages[pzImgIdx] != null) return pzImages[pzImgIdx];
            string fp = Path.Combine(pzCacheDir, "pz" + pzImgIdx + ".jpg");
            if (File.Exists(fp))
            {
                try
                {
                    var bmp = new Bitmap(fp);
                    pzImages[pzImgIdx] = bmp;
                    return bmp;
                }
                catch { }
            }
            EnsurePzDownload(pzImgIdx);
            return MakePzPlaceholder(pzImgIdx);
        }

        HashSet<int> pzDlSet = new HashSet<int>();
        void EnsurePzDownload(int i)
        {
            if (i < 0) return;
            PzEnsureList(i);
            if (pzImages[i] != null) return;
            if (pzDlSet.Contains(i)) return;
            pzDlSet.Add(i);
            Thread t = new Thread(() => PzDlOne(i));
            t.IsBackground = true;
            t.Start();
        }

        void PzDlOne(int i)
        {
            bool ok = false;
            try
            {
                System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;
                try { Directory.CreateDirectory(pzCacheDir); } catch { }
                string fp = Path.Combine(pzCacheDir, "pz" + i + ".jpg");
                string url = PzUrlFor(i);
                for (int attempt = 0; attempt < 4 && !ok; attempt++)
                {
                    try
                    {
                        string tmp = fp + ".tmp";
                        using (var wc = new System.Net.WebClient())
                        {
                            wc.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
                            wc.DownloadFile(url, tmp);
                        }
                        using (var src = new Bitmap(tmp))
                        {
                            Bitmap sq = Square540(src);
                            sq.Save(fp, System.Drawing.Imaging.ImageFormat.Jpeg);
                            sq.Dispose();
                        }
                        File.Delete(tmp);
                        ok = true;
                    }
                    catch (Exception ex)
                    {
                        // 该图源不可用：换一条随机图源重试
                        if (attempt == 3)
                            try { File.AppendAllText(Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "error.log"), "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] 拼图图片下载失败 " + i + ": " + ex.Message + "\r\n"); } catch { }
                        url = PzUrlFor(i);
                    }
                }
            }
            catch (Exception ex2)
            {
                try { File.AppendAllText(Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "error.log"), "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] 拼图下载异常 " + i + ": " + ex2.Message + "\r\n"); } catch { }
            }
            finally
            {
                try
                {
                    this.Invoke((Action)(() =>
                    {
                        pzDlSet.Remove(i);
                        if (ok)
                        {
                            try
                            {
                                PzEnsureList(i);
                                string fp = Path.Combine(pzCacheDir, "pz" + i + ".jpg");
                                var bmp = new Bitmap(fp);
                                var old = pzImages[i];
                                pzImages[i] = bmp;
                                if (old != null) old.Dispose();
                            }
                            catch { }
                            if (pzImgIdx == i && pzPieces != null && pagePuzzle.Visible) NewPuzzle();
                        }
                    }));
                }
                catch { }
            }
        }

        Bitmap Square540(Bitmap src)
        {
            var bmp = new Bitmap(540, 540);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                int s = Math.Min(src.Width, src.Height);
                int x = (src.Width - s) / 2, y = (src.Height - s) / 2;
                g.DrawImage(src, new Rectangle(0, 0, 540, 540), new Rectangle(x, y, s, s), GraphicsUnit.Pixel);
            }
            return bmp;
        }

        // 退出拼图页/程序：释放内存图片并删除本地缓存目录
        void CleanupPzCache()
        {
            try
            {
                pzStarted = false;
                if (pzImages != null)
                {
                    for (int i = 0; i < pzImages.Count; i++)
                    {
                        if (pzImages[i] != null)
                        {
                            try { pzImages[i].Dispose(); } catch { }
                            pzImages[i] = null;
                        }
                    }
                    pzImages.Clear();
                }
                if (Directory.Exists(pzCacheDir))
                {
                    try { Directory.Delete(pzCacheDir, true); } catch { }
                }
            }
            catch { }
        }

        void NewPuzzle()
        {
            if (pzPieces != null)
                foreach (var pb in pzPieces) pagePuzzle.Controls.Remove(pb);
            pzPieces = null;
            if (!pzStarted)
            {
                pzDragging = false; pzDragIdx = -1; pzSolved = false;
                pzTimer.Stop(); pzElapsed = 0;
                lblPzTime.Text = (langEn ? "Time 00:00" : "用时 00:00");
                lblPzDone.ForeColor = C_SUB;
                lblPzDone.Text = T("点击「下一张」开始加载图片", "Press Next to load images");
                return;
            }
            lblPzDone.Text = "";
            lblPzDone.ForeColor = Color.FromArgb(80, 200, 120);
            pzCellPx = pzGridAreaPx / pzGrid;
            int n = pzGrid * pzGrid;
            pzGridContent = new int[n];
            for (int i = 0; i < n; i++) pzGridContent[i] = -1;

            int[] order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;
            for (int i = n - 1; i > 0; i--) { int j = pzRnd.Next(i + 1); int t = order[i]; order[i] = order[j]; order[j] = t; }

            pzPieces = new PictureBox[n];
            Bitmap src = PzCurrentImage();
            int pw = src.Width / pzGrid, ph = src.Height / pzGrid;
            for (int i = 0; i < n; i++)
            {
                int r = i / pzGrid, c = i % pzGrid;
                var pb = new PictureBox();
                pb.SizeMode = PictureBoxSizeMode.Zoom;
                pb.BackColor = C_BUBBLE;
                pb.BorderStyle = BorderStyle.FixedSingle;
                pb.Tag = i;
                var bmp = new Bitmap(pw, ph);
                using (Graphics g = Graphics.FromImage(bmp))
                    g.DrawImage(src, new Rectangle(0, 0, pw, ph), new Rectangle(c * pw, r * ph, pw, ph), GraphicsUnit.Pixel);
                pb.Image = bmp;
                pb.MouseDown += PzPieceDown;
                pb.MouseMove += PzPieceMove;
                pb.MouseUp += PzPieceUp;
                pagePuzzle.Controls.Add(pb);
                pzPieces[i] = pb;
            }
            pzDragging = false; pzDragIdx = -1; pzSolved = false;
            lblPzDone.Text = "";
            pzElapsed = 0; lblPzTime.Text = "用时 00:00";
            pzTimer.Stop(); pzTimer.Start();
            RelayoutPieces();
        }

        void RelayoutPieces()
        {
            int n = pzGrid * pzGrid;
            for (int g = 0; g < n; g++)
            {
                int idx = pzGridContent[g];
                if (idx >= 0 && idx < n)
                {
                    int r = g / pzGrid, c = g % pzGrid;
                    pzPieces[idx].Location = new Point(pzGridAreaX + c * pzCellPx, pzGridAreaY + r * pzCellPx);
                    pzPieces[idx].Size = new Size(pzCellPx, pzCellPx);
                    pzPieces[idx].BringToFront();
                }
            }
            int cols = 2, gap = 8, k = 0;
            for (int idx = 0; idx < n; idx++)
            {
                if (InGrid(idx)) continue;
                int col = k % cols, row = k / cols;
                pzPieces[idx].Location = new Point(pzPieceAreaX + col * (pzPiecePx + gap), pzPieceAreaY + row * (pzPiecePx + gap));
                pzPieces[idx].Size = new Size(pzPiecePx, pzPiecePx);
                pzPieces[idx].BringToFront();
                k++;
            }
        }

        bool InGrid(int idx)
        {
            if (pzGridContent == null) return false;
            foreach (int g in pzGridContent) if (g == idx) return true;
            return false;
        }

        void PzPieceDown(object sender, MouseEventArgs e)
        {
            if (pzSolved) return;
            var pb = (PictureBox)sender;
            int idx = (int)pb.Tag;
            for (int g = 0; g < pzGridContent.Length; g++)
                if (pzGridContent[g] == idx) pzGridContent[g] = -1;
            pzDragging = true; pzDragIdx = idx;
            pb.BringToFront();
        }

        void PzPieceMove(object sender, MouseEventArgs e)
        {
            if (!pzDragging || pzDragIdx < 0) return;
            var pb = (PictureBox)sender;
            Point pt = pagePuzzle.PointToClient(Cursor.Position);
            pb.Location = new Point(pt.X - pb.Width / 2, pt.Y - pb.Height / 2);
        }

        void PzPieceUp(object sender, MouseEventArgs e)
        {
            if (!pzDragging) return;
            pzDragging = false;
            int idx = pzDragIdx;
            pzDragIdx = -1;
            Point pt = pagePuzzle.PointToClient(Cursor.Position);
            int g = HitGridCell(pt.X, pt.Y);
            if (g >= 0)
            {
                int old = pzGridContent[g];
                if (old >= 0) pzGridContent[g] = -1;
                pzGridContent[g] = idx;
            }
            RelayoutPieces();
            CheckSolved();
        }

        int HitGridCell(int px, int py)
        {
            if (px < pzGridAreaX || px >= pzGridAreaX + pzGridAreaPx) return -1;
            if (py < pzGridAreaY || py >= pzGridAreaY + pzGridAreaPx) return -1;
            int c = (px - pzGridAreaX) / pzCellPx;
            int r = (py - pzGridAreaY) / pzCellPx;
            if (c >= pzGrid || r >= pzGrid) return -1;
            return r * pzGrid + c;
        }

        void CheckSolved()
        {
            int n = pzGrid * pzGrid;
            for (int i = 0; i < n; i++)
                if (pzGridContent[i] != i) return;
            pzSolved = true;
            pzTimer.Stop();
            lblPzDone.Text = T("恭喜完成！用时 ", "Completed! Time ") + FmtTime(pzElapsed);
        }
        // ================= 托盘 =================
        void InitTray()
        {
            trayIcon = new NotifyIcon();
            try { trayIcon.Icon = this.Icon; } catch { }
            trayIcon.Text = "小工具箱";
            trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add(T("显示主窗口", "Show Main Window"), null, (s, e) => ShowMain());
            trayMenu.Items.Add((langEn ? "Run " : "立即") + curAction, null, (s, e) => Immediate());
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add(T("退出程序", "Exit"), null, (s, e) => ExitApp());
            trayIcon.ContextMenuStrip = trayMenu;
            trayIcon.DoubleClick += (s, e) => ShowMain();
            trayIcon.Visible = true;
        }

        void ShowMain()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        void ExitApp()
        {
            uiTimer.Stop();
            cfg.Active = taskActive && !cfg.Periodic;
            cfg.Periodic = taskActive && cfg.Periodic;
            cfg.EndTime = endTime;
            cfg.TotalSec = totalSec;
            cfg.Desc = curDesc;
            Config.Save(BASE_DIR, cfg);
            trayIcon.Visible = false;
            Application.Exit();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyAcrylic(this.Handle);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // 任务进行中：关闭=最小化到托盘；空闲：正常退出
            if (taskActive && cfg.Tray && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                trayIcon.ShowBalloonTip(2000, "小工具箱", "任务进行中，已最小化到托盘，到点仍会执行。", ToolTipIcon.Info);
                return;
            }
            trayIcon.Visible = false;
            cfg.Active = taskActive && !cfg.Periodic;
            cfg.Periodic = taskActive && cfg.Periodic;
            cfg.EndTime = endTime;
            cfg.TotalSec = totalSec;
            cfg.Desc = curDesc;
            Config.Save(BASE_DIR, cfg);
            CleanupPzCache();
            base.OnFormClosing(e);
        }

        // ================= 状态 =================
        void InitState()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                    chkAutorun.Checked = k != null && k.GetValue("DoubaoScheduledShutdown") != null;
            }
            catch { chkAutorun.Checked = false; }
        }

        void ApplyConfigToUI()
        {
            chkTray.Checked = cfg.Tray;
            chkConfirm.Checked = cfg.NeedConfirm;
            chkSound.Checked = cfg.Sound;
            chkAutorun.Checked = cfg.Autorun;
            numConfirm.Value = Math.Max(10, Math.Min(600, cfg.ConfirmSecs));
            numH.Value = Math.Max(0, Math.Min(999, cfg.Hours));
            numM.Value = Math.Max(0, Math.Min(59, cfg.Mins));
            numS.Value = Math.Max(0, Math.Min(59, cfg.Secs));
            dtpTime.Value = cfg.AtTime > DateTime.Now ? cfg.AtTime : DateTime.Now.AddHours(1);
            dtpDaily.Value = DateTime.Now.Date.Add(cfg.AtTime.TimeOfDay);
            dtpWeekly.Value = DateTime.Now.Date.Add(cfg.AtTime.TimeOfDay);
            for (int i = 0; i < 7; i++) weekCb[i].Checked = cfg.Week[i];
            switch (cfg.Mode)
            {
                case "datetime": rbDatetime.Checked = true; break;
                case "daily": rbDaily.Checked = true; break;
                case "weekly": rbWeekly.Checked = true; break;
                default: rbCountdown.Checked = true; break;
            }
            SelectAction(Array.IndexOf(ACTIONS, cfg.Action) >= 0 ? Array.IndexOf(ACTIONS, cfg.Action) : 0);
            UpdateModeUI();
        }

        void TryRestoreTask()
        {
            if (!cfg.Active && !cfg.Periodic) return;
            if (cfg.EndTime <= DateTime.Now) { cfg.Active = false; cfg.Periodic = false; return; }
            taskActive = true;
            endTime = cfg.EndTime;
            totalSec = Math.Max(cfg.TotalSec, 1);
            curDesc = cfg.Desc;
            confirmShown = false;
            lblTyping.Visible = true;
            lblStatus.Text = T("状态：任务进行中", "Status: Running");
            lblStatus.ForeColor = C_ON;
            lblDetail.Text = curDesc;
            lblDetail.ForeColor = C_TEXT;
            lblTimeSub.Text = cfg.Periodic ? T("周期任务 · 已恢复", "Periodic · Restored") : T("任务已恢复", "Task restored");
            Log("[恢复] " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " 恢复任务: " + curDesc);
        }

        void Log(string entry)
        {
            try { File.AppendAllText(logFile, entry + "\r\n"); } catch { }
        }

        void ToggleAutorun()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                {
                    if (chkAutorun.Checked)
                        k.SetValue("DoubaoScheduledShutdown", "\"" + Application.ExecutablePath + "\"");
                    else
                        k.DeleteValue("DoubaoScheduledShutdown", false);
                }
                cfg.Autorun = chkAutorun.Checked;
            }
            catch { MessageBox.Show("注册表操作失败。", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        // ================= C盘清理 =================
        void ShowCleanPage(bool show)
        {
            if (pageClean == null) return;
            pageTimer.Visible = !show;
            pagePuzzle.Visible = false;
            if (pageReset != null) pageReset.Visible = false;
            if (pageGcm != null) pageGcm.Visible = false;
            pageClean.Visible = show;
            if (show) { lblPeer.Text = T("C盘清理", "C: Cleanup"); lblPeer.ForeColor = Color.FromArgb(56, 189, 248); }
        }

        void RunClean(string mode)
        {
            string dir = Path.Combine(BASE_DIR, "clean_tool");
            string script = (mode == "v8") ? "DeepClean_v8.ps1" : "DeepClean.ps1";
            string path = Path.Combine(dir, script);
            if (!File.Exists(path))
            {
                Cursor.Current = Cursors.WaitCursor;
                bool ok = UnpackResource("CleanZip", dir, "C盘清理");
                Cursor.Current = Cursors.Default;
                path = Path.Combine(dir, script);
                if (!ok || !File.Exists(path))
                {
                    MessageBox.Show(this, "清理组件缺失且自动部署失败，请重新安装本工具后再试。", "C盘清理", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }
            string desc = (mode == "v8") ? "平衡保护清理（白名单保护，推荐）" : "极致深度清理（全量扫描缓存）";
            if (MessageBox.Show(this, desc + "将删除系统与用户目录下的临时文件、缓存与日志。\n这些均为可再生数据，不会涉及个人文件。\n\n是否继续？", "C盘清理", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("powershell.exe");
                psi.Arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + path + "\" -Silent";
                psi.Verb = "runas";
                psi.UseShellExecute = true;
                Process.Start(psi);
                Log("[清理] " + desc + " 已启动 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            }
            catch
            {
                MessageBox.Show(this, "启动失败，可能未授予管理员权限。", "C盘清理", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ================= 游戏作弊 =================
        void ShowGcmPage(bool show)
        {
            if (pageGcm == null) return;
            pageTimer.Visible = !show;
            pagePuzzle.Visible = false;
            if (pageReset != null) pageReset.Visible = false;
            if (pageClean != null) pageClean.Visible = false;
            pageGcm.Visible = show;
            if (show) { lblPeer.Text = T("游戏作弊", "Game Cheats"); lblPeer.ForeColor = Color.FromArgb(244, 114, 182); }
        }

        void RunGcm()
        {
            string dir = Path.Combine(BASE_DIR, "gcm_tool");
            string exe = Path.Combine(dir, "Game Cheats Manager.exe");
            if (!File.Exists(exe))
            {
                Cursor.Current = Cursors.WaitCursor;
                bool ok = UnpackResource("GcmZip", dir, "游戏作弊管理");
                Cursor.Current = Cursors.Default;
                exe = Path.Combine(dir, "Game Cheats Manager.exe");
                if (!ok || !File.Exists(exe))
                {
                    MessageBox.Show(this, "未找到游戏作弊管理组件（gcm_tool\\Game Cheats Manager.exe）且自动部署失败。\n请重新安装小工具箱后再试。", "游戏作弊", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                MessageBox.Show(this, "游戏作弊管理组件已自动部署完成，现在可以正常使用了。\n（后续启动无需再次部署）", "游戏作弊", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            try
            {
                // 首次运行预置中文深色配置与索引
                string cfgDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GCM Settings");
                string cfgFile = Path.Combine(cfgDir, "settings.json");
                string preset = Path.Combine(dir, "preset");
                if (!File.Exists(cfgFile) && Directory.Exists(preset))
                {
                    if (!Directory.Exists(cfgDir)) Directory.CreateDirectory(cfgDir);
                    foreach (string f in Directory.GetFiles(preset, "*.json"))
                        File.Copy(f, Path.Combine(cfgDir, Path.GetFileName(f)), true);
                    string pdb = Path.Combine(preset, "db");
                    if (Directory.Exists(pdb) && !Directory.Exists(Path.Combine(cfgDir, "db")))
                        CopyDir(pdb, Path.Combine(cfgDir, "db"));
                }
                Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = dir });
                Log("[游戏作弊] 已启动 GCM " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "启动失败：" + ex.Message, "游戏作弊", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        void CopyDir(string src, string dst)
        {
            if (!Directory.Exists(dst)) Directory.CreateDirectory(dst);
            foreach (string f in Directory.GetFiles(src))
                File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
            foreach (string d in Directory.GetDirectories(src))
                CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
        }

        // ================= 视频下载 / 音乐下载 =================
        void ShowVideoPage(bool show)
        {
            if (pageVideo == null) return;
            pageTimer.Visible = !show;
            pagePuzzle.Visible = false;
            if (pageReset != null) pageReset.Visible = false;
            if (pageClean != null) pageClean.Visible = false;
            if (pageGcm != null) pageGcm.Visible = false;
            if (pageMusic != null) pageMusic.Visible = false;
            pageVideo.Visible = show;
            if (show) { lblPeer.Text = T("视频下载", "Video Download"); lblPeer.ForeColor = ACTION_COLOR[5]; }
        }

        void ShowMusicPage(bool show)
        {
            if (pageMusic == null) return;
            pageTimer.Visible = !show;
            pagePuzzle.Visible = false;
            if (pageReset != null) pageReset.Visible = false;
            if (pageClean != null) pageClean.Visible = false;
            if (pageGcm != null) pageGcm.Visible = false;
            if (pageVideo != null) pageVideo.Visible = false;
            pageMusic.Visible = show;
            if (show) { lblPeer.Text = T("音乐下载", "Music Download"); lblPeer.ForeColor = ACTION_COLOR[6]; }
        }

        string VideoToolDir() { return Path.Combine(BASE_DIR, "video_tool"); }
        string VideoToolExe() { return Path.Combine(VideoToolDir(), "yt-dlp.exe"); }

        bool EnsureVideoTool()
        {
            string exe = VideoToolExe();
            if (File.Exists(exe)) return true;
            Cursor.Current = Cursors.WaitCursor;
            bool ok = UnpackResource("VideoZip", VideoToolDir(), "视频下载引擎");
            Cursor.Current = Cursors.Default;
            if (ok && File.Exists(exe))
            {
                MessageBox.Show(this, "视频下载引擎已自动部署完成，现在可以正常使用了。", "视频下载", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return true;
            }
            MessageBox.Show(this, "视频下载引擎（yt-dlp）缺失且自动部署失败。\n请重新安装小工具箱后再试。", "视频下载", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        void PickVideoDir()
        {
            FolderBrowserDialog d = new FolderBrowserDialog();
            d.Description = "选择视频保存目录";
            d.SelectedPath = vOutDir;
            if (d.ShowDialog(this) == DialogResult.OK)
            {
                vOutDir = d.SelectedPath;
                lblVDir.Text = vOutDir;
            }
        }

        void OpenVideoDir()
        {
            if (!Directory.Exists(vOutDir)) Directory.CreateDirectory(vOutDir);
            try { Process.Start("explorer.exe", "\"" + vOutDir + "\""); } catch { }
        }

        void OpenMusicDir()
        {
            string dir = Path.Combine(vOutDir, "音乐");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            try { Process.Start("explorer.exe", "\"" + dir + "\""); } catch { }
        }

        // ---------- 视频解析 ----------
        void VideoParse()
        {
            if (vBusy) return;
            string url = txtVUrl.Text.Trim();
            if (url.Length == 0) { MessageBox.Show(this, "请先粘贴视频链接。", "视频下载", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            if (!EnsureVideoTool()) return;
            vBusy = true;
            btnVParse.Enabled = false;
            btnVParse.Text = "解析中…";
            lblVInfo.Text = "正在解析视频信息…";
            lblVInfo.ForeColor = C_SUB;
            cmbVFormat.Items.Clear();
            Thread t = new Thread(() =>
            {
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo(VideoToolExe(), "-J --no-playlist --no-warnings --no-check-certificate \"" + url + "\"");
                    psi.CreateNoWindow = true;
                    psi.UseShellExecute = false;
                    psi.RedirectStandardOutput = true;
                    psi.RedirectStandardError = true;
                    psi.StandardOutputEncoding = Encoding.UTF8;
                    Process p = Process.Start(psi);
                    string json = p.StandardOutput.ReadToEnd();
                    string err = p.StandardError.ReadToEnd();
                    p.WaitForExit();
                    object[] res = ParseYtJson(json);
                    this.Invoke((Action)(() =>
                    {
                        if (res == null)
                        {
                            lblVInfo.Text = "解析失败：\n" + (err.Trim().Length > 0 ? FirstLine(err) : "无法获取视频信息（链接无效 / 平台限制 / 需要登录）");
                            lblVInfo.ForeColor = Color.FromArgb(240, 120, 100);
                        }
                        else
                        {
                            lblVInfo.Text = (string)res[0];
                            lblVInfo.ForeColor = C_SUB;
                            List<object[]> fmts = (List<object[]>)res[1];
                            cmbVFormat.Items.Clear();
                            vFmtIds.Clear();
                            vFmtMerge.Clear();
                            foreach (object[] f in fmts)
                            {
                                cmbVFormat.Items.Add((string)f[1]);
                                vFmtIds.Add((string)f[0]);
                                vFmtMerge.Add((bool)f[3]);
                            }
                            if (cmbVFormat.Items.Count > 0) cmbVFormat.SelectedIndex = 0;
                            lblVTip.Text = "已解析 " + fmts.Count + " 种格式，选择后点击「开始下载」。";
                            lblVTip.ForeColor = Color.FromArgb(240, 160, 90);
                        }
                    }));
                }
                catch (Exception ex)
                {
                    this.Invoke((Action)(() => { lblVInfo.Text = "解析异常：" + ex.Message; lblVInfo.ForeColor = Color.FromArgb(240, 120, 100); }));
                }
                finally { this.Invoke((Action)(() => { vBusy = false; btnVParse.Enabled = true; btnVParse.Text = "解析"; })); }
            });
            t.IsBackground = true;
            t.Start();
        }

        object[] ParseYtJson(string json)
        {
            try
            {
                if (json == null || json.Trim().Length == 0) return null;
                JavaScriptSerializer ser = new JavaScriptSerializer();
                ser.MaxJsonLength = int.MaxValue;
                Dictionary<string, object> root = ser.Deserialize<Dictionary<string, object>>(json);
                if (root == null || !root.ContainsKey("title")) return null;
                string title = Convert.ToString(root["title"]);
                List<object[]> fmts = new List<object[]>();
                if (root.ContainsKey("formats"))
                {
                    object[] arr = ToArr(root["formats"]);
                    if (arr != null)
                    {
                        List<Dictionary<string, object>> list = new List<Dictionary<string, object>>();
                        foreach (object o in arr)
                        {
                            Dictionary<string, object> d = o as Dictionary<string, object>;
                            if (d != null) list.Add(d);
                        }
                        list.Sort((a, b) => HOf(b).CompareTo(HOf(a)));
                        HashSet<string> seen = new HashSet<string>();
                        foreach (Dictionary<string, object> d in list)
                        {
                            string id = SOf(d, "format_id");
                            string ext = SOf(d, "ext");
                            string vc = SOf(d, "vcodec");
                            string ac = SOf(d, "acodec");
                            if (id.Length == 0) continue;
                            bool hasV = vc.Length > 0 && vc != "none";
                            if (!hasV) continue;
                            int h = HOf(d);
                            string note = h > 0 ? h + "p" : SOf(d, "format_note");
                            if (note.Length == 0) note = ext;
                            string size = "";
                            object fsz;
                            if (d.TryGetValue("filesize", out fsz) && fsz != null)
                            {
                                long fs = 0; long.TryParse(Convert.ToString(fsz), out fs);
                                if (fs > 0) size = " · 约 " + FmtSize(fs);
                            }
                            bool merge = ac.Length == 0 || ac == "none";
                            string key = (merge ? "M" : "F") + h + "_" + ext;
                            if (!seen.Add(key)) continue;
                            string disp = note + " " + ext + (merge ? "（需FFmpeg）" : "") + size;
                            fmts.Add(new object[] { id, disp, h, merge });
                        }
                        // 无需合并的排前面
                        fmts.Sort((a, b) => { int ma = (bool)a[3] ? 1 : 0; int mb = (bool)b[3] ? 1 : 0; return ma.CompareTo(mb); });
                        if (fmts.Count == 0) return null;
                    }
                }
                else if (root.ContainsKey("requested_formats")) { } // 单格式情况忽略，fmts 空则下面回退
                return new object[] { title, fmts };
            }
            catch { return null; }
        }

        string SOf(Dictionary<string, object> d, string key)
        {
            object v;
            if (d.TryGetValue(key, out v) && v != null) return Convert.ToString(v);
            return "";
        }

        // JavaScriptSerializer 的 JSON 数组是 ArrayList，统一转 object[]
        object[] ToArr(object v)
        {
            if (v == null) return null;
            if (v is object[]) return (object[])v;
            System.Collections.ArrayList al = v as System.Collections.ArrayList;
            if (al == null) return null;
            object[] r = new object[al.Count];
            for (int i = 0; i < al.Count; i++) r[i] = al[i];
            return r;
        }

        int HOf(Dictionary<string, object> d)
        {
            object v;
            int h = 0;
            if (d.TryGetValue("height", out v) && v != null) int.TryParse(Convert.ToString(v), out h);
            return h;
        }

        string FmtSize(long b)
        {
            if (b >= 1024L * 1024 * 1024) return (b / 1048576.0 / 1024.0).ToString("0.0") + "GB";
            if (b >= 1048576) return (b / 1048576.0).ToString("0.0") + "MB";
            if (b >= 1024) return (b / 1024.0).ToString("0.0") + "KB";
            return b + "B";
        }

        string FirstLine(string s)
        {
            string[] ls = s.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            return ls.Length > 0 ? ls[0] : s;
        }

        // ---------- 视频下载 ----------
        void VideoDownload()
        {
            if (vBusy) { MessageBox.Show(this, "正在解析中，请稍候…", "视频下载", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            if (vDlProc != null) { MessageBox.Show(this, "已有下载任务进行中，请等待完成。", "视频下载", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            if (cmbVFormat.SelectedIndex < 0) { MessageBox.Show(this, "请先解析视频并选择清晰度。", "视频下载", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            string url = txtVUrl.Text.Trim();
            if (url.Length == 0) { MessageBox.Show(this, "请先粘贴视频链接。", "视频下载", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            if (!EnsureVideoTool()) return;
            int selIdx = cmbVFormat.SelectedIndex;
            string fmtId = vFmtIds[selIdx];
            bool needMerge = vFmtMerge[selIdx];
            if (needMerge && !File.Exists(Path.Combine(VideoToolDir(), "ffmpeg.exe")))
            {
                if (MessageBox.Show(this, "该格式需要 FFmpeg 合并音视频（高清视频通常需要）。\n是否现在自动获取 FFmpeg（约90MB）？", "视频下载", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    return;
                while (!EnsureFfmpeg())
                {
                    DialogResult dr = ShowFfmpegFailDialog();
                    if (dr != DialogResult.Retry) return;
                }
            }
            if (!Directory.Exists(vOutDir)) Directory.CreateDirectory(vOutDir);
            string title = lblVInfo.Text.Length > 0 ? lblVInfo.Text : "视频";
            ListViewItem li = lvVideos.Items.Add(title);
            li.SubItems.Add("");
            li.SubItems.Add("0%");
            li.SubItems.Add("");
            li.SubItems.Add("排队中");
            lblVTip.Text = "开始下载：" + title;
            lblVTip.ForeColor = Color.FromArgb(240, 160, 90);
            string args = "--no-playlist --no-check-certificate --newline -f \"" + fmtId + "\" -o \"" + Path.Combine(vOutDir, "%(title)s.%(ext)s") + "\" --progress-template \"download:DP|%(progress._percent_str)s|%(progress._downloaded_bytes_str)s|%(progress._total_bytes_str)s|%(progress._speed_str)s|%(progress._eta_str)s\" \"" + url + "\"";
            ProcessStartInfo psi = new ProcessStartInfo(VideoToolExe(), args);
            psi.CreateNoWindow = true;
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
            try
            {
                Process p = Process.Start(psi);
                p.BeginOutputReadLine();
                p.OutputDataReceived += (s, e) =>
                {
                    if (e.Data == null) return;
                    this.Invoke((Action)(() => HandleVLine(li, e.Data)));
                };
                p.EnableRaisingEvents = true;
                p.Exited += (s, e) =>
                {
                    this.Invoke((Action)(() =>
                    {
                        vDlProc = null;
                        if (li.ListView != null && !li.ListView.IsDisposed)
                        {
                            if (li.SubItems[4].Text != "失败") { li.SubItems[4].Text = (p.ExitCode == 0) ? "完成" : "失败"; if (p.ExitCode == 0) li.SubItems[2].Text = "100%"; }
                        }
                        if (p.ExitCode != 0) lblVTip.Text = "下载失败，详见列表状态。";
                        else lblVTip.Text = "下载完成，文件已保存到 " + vOutDir;
                    }));
                };
                vDlProc = p;
            }
            catch (Exception ex)
            {
                li.SubItems[4].Text = "失败";
                MessageBox.Show(this, "启动下载失败：" + ex.Message, "视频下载", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        void HandleVLine(ListViewItem li, string line)
        {
            if (li.ListView == null || li.ListView.IsDisposed) return;
            if (line.StartsWith("DP|"))
            {
                string[] parts = line.Split('|');
                string pct = parts.Length > 1 ? parts[1].Trim() : "";
                string dl = parts.Length > 2 ? parts[2].Trim() : "";
                string total = parts.Length > 3 ? parts[3].Trim() : "";
                string spd = parts.Length > 4 ? parts[4].Trim() : "";
                string eta = parts.Length > 5 ? parts[5].Trim() : "";
                li.SubItems[2].Text = pct;
                li.SubItems[3].Text = spd;
                li.SubItems[4].Text = eta.Length > 0 ? "下载中 · 剩余 " + eta : "下载中";
                if (total.Length > 0 && li.SubItems[1].Text.Length == 0) li.SubItems[1].Text = total;
                else if (dl.Length > 0 && total.Length > 0) li.SubItems[1].Text = dl + " / " + total;
            }
            else if (line.StartsWith("ERROR"))
            {
                li.SubItems[4].Text = "失败";
                lblVTip.Text = "下载失败：" + line;
                lblVTip.ForeColor = Color.FromArgb(240, 120, 100);
            }
            else if (line.Contains("[download] 100%"))
            {
                li.SubItems[2].Text = "100%";
                li.SubItems[4].Text = "完成";
            }
        }

        bool EnsureFfmpeg()
        {
            string ff = Path.Combine(VideoToolDir(), "ffmpeg.exe");
            if (File.Exists(ff)) return true;
            // 本机已装有 FFmpeg → 直接复制使用
            string local = FindLocalFfmpeg();
            if (local != null)
            {
                try
                {
                    File.Copy(local, ff, true);
                    lblVTip.Text = "已使用本机检测到的 FFmpeg，高清视频可正常合并下载。";
                    lblVTip.ForeColor = C_ON;
                    return true;
                }
                catch { }
            }
            string[] mirrors = new string[] {
                "https://ghfast.top/",
                "https://gh-proxy.com/",
                "",                                  // 直连 GitHub（有代理时通常最快）
                "https://ghproxy.cn/",
                "https://ghproxy.net/",
                "https://mirror.ghproxy.com/",
                "https://ghps.cc/",
                "https://github.moeyy.xyz/"
            };
            string rel = "https://github.com/GyanD/codexffmpeg/releases/download/7.1/ffmpeg-7.1-essentials_build.zip";
            string zipPath = Path.Combine(VideoToolDir(), "ffmpeg_tmp.zip");
            string exDir = Path.Combine(VideoToolDir(), "ffx");
            bool anyErr = false;
            for (int i = 0; i < mirrors.Length; i++)
            {
                string url = mirrors[i] + rel;
                lblVTip.Text = "正在下载 FFmpeg 合并工具（约90MB）… 镜像 " + (i + 1) + "/" + mirrors.Length;
                lblVTip.ForeColor = Color.FromArgb(240, 160, 90);
                Application.DoEvents();
                try
                {
                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                    req.Timeout = 30000;
                    req.ReadWriteTimeout = 40000;
                    req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)";
                    using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                    using (Stream src = resp.GetResponseStream())
                    using (FileStream dst = new FileStream(zipPath, FileMode.Create, FileAccess.Write))
                    {
                        byte[] buf = new byte[65536];
                        int n;
                        while ((n = src.Read(buf, 0, buf.Length)) > 0) dst.Write(buf, 0, n);
                    }
                    if (Directory.Exists(exDir)) Directory.Delete(exDir, true);
                    Directory.CreateDirectory(exDir);
                    ZipFile.ExtractToDirectory(zipPath, exDir);
                    string found = FindFile(exDir, "ffmpeg.exe");
                    if (found != null) File.Copy(found, ff, true);
                    try { File.Delete(zipPath); } catch { }
                    try { Directory.Delete(exDir, true); } catch { }
                    if (File.Exists(ff))
                    {
                        lblVTip.Text = "FFmpeg 已就绪，高清视频可正常合并下载。";
                        lblVTip.ForeColor = C_ON;
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    anyErr = true;
                    try { File.AppendAllText(Path.Combine(BASE_DIR, "error.log"), "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] FFmpeg 镜像" + (i + 1) + " 失败：" + url + " => " + ex.Message + "\r\n"); } catch { }
                }
            }
            if (anyErr)
            {
                try { File.AppendAllText(Path.Combine(BASE_DIR, "error.log"), "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] FFmpeg 全部镜像下载失败\r\n"); } catch { }
            }
            lblVTip.Text = "FFmpeg 下载失败，可点击「开始下载」重试；或手动放入 ffmpeg.exe。";
            lblVTip.ForeColor = Color.FromArgb(230, 120, 110);
            return false;
        }

        string FindLocalFfmpeg()
        {
            string[] candidates = {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ffmpeg\\bin\\ffmpeg.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "scoop\\apps\\ffmpeg\\current\\bin\\ffmpeg.exe"),
                @"C:\ffmpeg\bin\ffmpeg.exe",
                @"C:\Program Files\ffmpeg\bin\ffmpeg.exe",
                @"C:\Program Files (x86)\ffmpeg\bin\ffmpeg.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft\\WinGet\\Links\\ffmpeg.exe")
            };
            foreach (string c in candidates)
            {
                try { if (File.Exists(c)) return c; } catch { }
            }
            string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string p in pathEnv.Split(';'))
            {
                try
                {
                    string f = Path.Combine(p.Trim().Trim('"'), "ffmpeg.exe");
                    if (File.Exists(f)) return f;
                }
                catch { }
            }
            return null;
        }

        DialogResult ShowFfmpegFailDialog()
        {
            using (Form dlg = new Form())
            {
                dlg.Text = "视频下载";
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.MaximizeBox = false;
                dlg.MinimizeBox = false;
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.ClientSize = new Size(480, 262);
                dlg.BackColor = C_BG;
                dlg.Font = new Font("Microsoft YaHei UI", 9F);
                Label icon = new Label();
                icon.Text = "⚠";
                icon.ForeColor = Color.FromArgb(240, 160, 60);
                icon.Font = new Font("Microsoft YaHei UI", 30F);
                icon.Location = new Point(18, 22);
                icon.AutoSize = true;
                Label msg = new Label();
                msg.Text = "FFmpeg 自动下载失败（已尝试多个镜像源，可能是网络原因）。\n\n三种解决办法：\n① 将随软件提供的 ffmpeg.exe 复制到程序目录的\n   video_tool 文件夹；\n② 点「打开下载页面」用浏览器搜索下载后放入；\n③ 稍后点「重试」再次自动下载。";
                msg.ForeColor = C_TEXT;
                msg.Location = new Point(82, 16);
                msg.Size = new Size(384, 158);
                Button b1 = new Button();
                b1.Text = "打开下载页面";
                b1.Width = 130; b1.Height = 36;
                b1.Location = new Point(82, 200);
                b1.Click += (s, e) => { try { Process.Start("https://www.baidu.com/s?wd=ffmpeg%207.1%20essentials_build%20zip%20%E4%B8%8B%E8%BD%BD"); } catch { } };
                Button b2 = new Button();
                b2.Text = "重试";
                b2.Width = 86; b2.Height = 36;
                b2.Location = new Point(230, 200);
                b2.DialogResult = DialogResult.Retry;
                Button b3 = new Button();
                b3.Text = "取消";
                b3.Width = 86; b3.Height = 36;
                b3.Location = new Point(326, 200);
                b3.DialogResult = DialogResult.Cancel;
                foreach (Button b in new Button[] { b1, b2, b3 })
                {
                    b.FlatStyle = FlatStyle.Flat;
                    b.FlatAppearance.BorderColor = C_BORDER;
                    b.BackColor = C_MAIN;
                    b.ForeColor = C_TEXT;
                }
                dlg.Controls.Add(icon);
                dlg.Controls.Add(msg);
                dlg.Controls.Add(b1);
                dlg.Controls.Add(b2);
                dlg.Controls.Add(b3);
                return dlg.ShowDialog(this);
            }
        }

        string FindFile(string dir, string name)
        {
            foreach (string f in Directory.GetFiles(dir, name, SearchOption.AllDirectories)) return f;
            return null;
        }

        // ---------- 音乐搜索与下载 ----------
        void MusicSearch()
        {
            if (mBusy) return;
            string kw = txtMSearch.Text.Trim();
            if (kw.Length == 0) { MessageBox.Show(this, "请输入歌名或歌手。", "音乐下载", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            mBusy = true;
            btnMSearch.Text = "搜索中…";
            btnMSearch.Enabled = false;
            lvSongs.Items.Clear();
            mSongs.Clear();
            Thread t = new Thread(() =>
            {
                try
                {
                    string api = "https://music.163.com/api/search/get/web?s=" + Uri.EscapeDataString(kw) + "&type=1&limit=30&offset=0";
                    WebClient wc = new WebClient();
                    wc.Encoding = Encoding.UTF8;
                    wc.Headers["Referer"] = "https://music.163.com/";
                    wc.Headers["User-Agent"] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)";
                    string json = wc.DownloadString(api);
                    List<object[]> songs = ParseNeteaseSongs(json);
                    this.Invoke((Action)(() =>
                    {
                        lvSongs.BeginUpdate();
                        lvSongs.Items.Clear();
                        mSongs.Clear();
                        foreach (object[] s in songs)
                        {
                            mSongs.Add(s);
                            ListViewItem li = lvSongs.Items.Add((string)s[1]);
                            li.SubItems.Add((string)s[2]);
                            li.SubItems.Add((string)s[3]);
                            li.SubItems.Add((string)s[4]);
                        }
                        lvSongs.EndUpdate();
                        if (songs.Count == 0) lblMStatus.Text = "未找到相关歌曲，换个关键词试试。";
                        else lblMStatus.Text = "找到 " + songs.Count + " 首 · 双击歌曲或选中后点击「下载选中歌曲」（MP3）";
                    }));
                }
                catch (Exception ex)
                {
                    this.Invoke((Action)(() => lblMStatus.Text = "搜索失败：" + ex.Message));
                }
                finally { this.Invoke((Action)(() => { mBusy = false; btnMSearch.Text = "搜索"; btnMSearch.Enabled = true; })); }
            });
            t.IsBackground = true;
            t.Start();
        }

        List<object[]> ParseNeteaseSongs(string json)
        {
            List<object[]> list = new List<object[]>();
            try
            {
                JavaScriptSerializer ser = new JavaScriptSerializer();
                ser.MaxJsonLength = int.MaxValue;
                Dictionary<string, object> root = ser.Deserialize<Dictionary<string, object>>(json);
                if (root == null || !root.ContainsKey("result")) return list;
                Dictionary<string, object> res = root["result"] as Dictionary<string, object>;
                if (res == null || !res.ContainsKey("songs")) return list;
                object[] arr = ToArr(res["songs"]);
                if (arr == null) return list;
                foreach (object o in arr)
                {
                    Dictionary<string, object> d = o as Dictionary<string, object>;
                    if (d == null) continue;
                    long id = 0;
                    object ido;
                    if (d.TryGetValue("id", out ido) && ido != null) long.TryParse(Convert.ToString(ido), out id);
                    string name = d.ContainsKey("name") ? Convert.ToString(d["name"]) : "";
                    string artist = "", album = "", dur = "";
                    object ao;
                    if (d.TryGetValue("artists", out ao))
                    {
                        object[] ar = ToArr(ao);
                        if (ar != null && ar.Length > 0 && ar[0] is Dictionary<string, object>)
                        {
                            Dictionary<string, object> a0 = (Dictionary<string, object>)ar[0];
                            if (a0.ContainsKey("name")) artist = Convert.ToString(a0["name"]);
                        }
                    }
                    object alo;
                    if (d.TryGetValue("album", out alo) && alo is Dictionary<string, object>)
                    {
                        Dictionary<string, object> al = (Dictionary<string, object>)alo;
                        if (al.ContainsKey("name")) album = Convert.ToString(al["name"]);
                    }
                    object duo;
                    if (d.TryGetValue("duration", out duo) && duo != null)
                    {
                        long ms = 0; long.TryParse(Convert.ToString(duo), out ms);
                        dur = (ms / 60000) + ":" + ((ms / 1000) % 60).ToString("00");
                    }
                    list.Add(new object[] { id, name, artist, album, dur });
                }
            }
            catch { }
            return list;
        }

        void MusicDownloadSelected()
        {
            if (mBusy) return;
            if (lvSongs.SelectedIndices.Count == 0) { MessageBox.Show(this, "请先在列表中选择一首歌曲。", "音乐下载", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            int idx = lvSongs.SelectedIndices[0];
            if (idx < 0 || idx >= mSongs.Count) return;
            object[] s = mSongs[idx];
            long id = Convert.ToInt64(s[0]);
            string name = (string)s[1];
            MusicDownloadByUrl("https://music.163.com/song?id=" + id, name);
        }

        void MusicByUrl()
        {
            if (mBusy) return;
            string url = txtMSearch.Text.Trim();
            if (url.Length == 0 || !url.StartsWith("http"))
            {
                MessageBox.Show(this, "请在搜索框内粘贴音乐链接（网易云 / QQ音乐 等单曲链接）。", "音乐下载", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            MusicDownloadByUrl(url, url);
        }

        void MusicDownloadByUrl(string url, string label)
        {
            if (!EnsureVideoTool()) return;
            mBusy = true;
            string dir = Path.Combine(vOutDir, "音乐");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            pbMusic.Visible = true;
            pbMusic.Value = 0;
            lblMStatus.Text = "正在解析链接并下载…";
            Thread t = new Thread(() =>
            {
                try
                {
                    string args = "--no-check-certificate --newline -f standard -o \"" + Path.Combine(dir, "%(title)s.%(ext)s") + "\" --progress-template \"DP|%(progress._percent_str)s|%(progress._speed_str)s|%(progress._eta_str)s\" \"" + url + "\"";
                    ProcessStartInfo psi = new ProcessStartInfo(VideoToolExe(), args);
                    psi.CreateNoWindow = true;
                    psi.UseShellExecute = false;
                    psi.RedirectStandardOutput = true;
                    psi.RedirectStandardError = true;
                    psi.StandardOutputEncoding = Encoding.UTF8;
                    Process p = Process.Start(psi);
                    string line;
                    while ((line = p.StandardOutput.ReadLine()) != null)
                    {
                        if (line.StartsWith("DP|"))
                        {
                            string[] parts = line.Split('|');
                            string pct = parts.Length > 1 ? parts[1].Trim() : "";
                            string spd = parts.Length > 2 ? parts[2].Trim() : "";
                            string eta = parts.Length > 3 ? parts[3].Trim() : "";
                            int pv = 0;
                            float pf;
                            if (float.TryParse(pct.TrimEnd('%'), out pf)) pv = (int)pf;
                            string msg = "正在下载：" + pct;
                            if (spd.Length > 0) msg += "  " + spd;
                            if (eta.Length > 0) msg += "  剩余 " + eta;
                            this.Invoke((Action)(() => { pbMusic.Value = Math.Min(100, Math.Max(0, pv)); lblMStatus.Text = msg; }));
                        }
                        else if (line.StartsWith("ERROR"))
                        {
                            this.Invoke((Action)(() => lblMStatus.Text = "下载失败：" + line));
                        }
                    }
                    p.WaitForExit();
                    this.Invoke((Action)(() =>
                    {
                        if (p.ExitCode == 0) lblMStatus.Text = "下载完成，已保存到「下载\\音乐」文件夹。";
                        else if (!lblMStatus.Text.StartsWith("下载失败")) lblMStatus.Text = "下载未完成（可能链接受限）。";
                    }));
                }
                catch (Exception ex)
                {
                    this.Invoke((Action)(() => lblMStatus.Text = "下载异常：" + ex.Message));
                }
                finally { this.Invoke((Action)(() => { mBusy = false; pbMusic.Visible = false; })); }
            });
            t.IsBackground = true;
            t.Start();
        }

        // 从嵌入资源解压组件到 exe 同目录（单 exe 自部署，无需安装器）
        bool UnpackResource(string resName, string targetDir, string display)
        {
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                using (Stream rs = asm.GetManifestResourceStream(resName))
                {
                    if (rs == null)
                    {
                        File.AppendAllText(Path.Combine(BASE_DIR, "error.log"),
                            "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] 缺少内嵌资源 " + resName + "\r\n");
                        return false;
                    }
                    if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);
                    string tmp = Path.Combine(targetDir, "pkg_" + Guid.NewGuid().ToString("N") + ".zip");
                    using (FileStream fs = new FileStream(tmp, FileMode.Create))
                        rs.CopyTo(fs);
                    ZipFile.ExtractToDirectory(tmp, targetDir);
                    File.Delete(tmp);
                }
                Log("[部署] " + display + " 组件已自动部署到 " + targetDir + " " + DateTime.Now.ToString("HH:mm:ss"));
                return true;
            }
            catch (Exception ex)
            {
                try { File.AppendAllText(Path.Combine(BASE_DIR, "error.log"),
                    "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] 组件部署失败 " + display + "：" + ex + "\r\n"); } catch { }
                return false;
            }
        }

        // ================= 一键重装 =================
        void ShowResetPage(bool show)
        {
            if (pageReset == null) return;
            pageTimer.Visible = !show;
            pagePuzzle.Visible = false;
            if (pageClean != null) pageClean.Visible = false;
            if (pageGcm != null) pageGcm.Visible = false;
            pageReset.Visible = show;
            if (show) { lblPeer.Text = T("一键重装", "Reinstall"); lblPeer.ForeColor = C_ON; }
        }

        void ConfirmReset(string mode)
        {
            string msg = "";
            if (mode == "wizard") msg = "将打开 Windows 重置向导。\n可选择「保留我的文件」或「删除所有内容」进行系统重装。";
            else if (mode == "winre") msg = "系统将重启并进入恢复环境。\n可在此选择「重置此电脑」、修复启动或进入命令提示符。";
            else msg = "将打开 Windows「设置 → 系统 → 恢复」页面。";
            if (MessageBox.Show(this, msg + "\n\n操作将影响系统，请确认已备份重要文件。\n是否继续？", "一键重装系统", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
            try
            {
                if (mode == "wizard")
                {
                    Process.Start("systemreset.exe");
                    Log("[重装] 打开重置向导 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                }
                else if (mode == "winre")
                {
                    Process.Start(new ProcessStartInfo("shutdown.exe", "/r /o /t 0") { UseShellExecute = false, CreateNoWindow = true });
                    Log("[重装] 重启进入恢复环境 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                }
                else
                {
                    Process.Start(new ProcessStartInfo("ms-settings:reset") { UseShellExecute = true });
                    Log("[重装] 打开恢复设置 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "启动失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ================= 操作选择 =================
        void SelectAction(int idx)
        {
            if (idx == 2)
            {
                ShowResetPage(true);
                return;
            }
            if (idx == 3)
            {
                ShowCleanPage(true);
                return;
            }
            if (idx == 4)
            {
                ShowGcmPage(true);
                return;
            }
            if (idx == 5)
            {
                ShowVideoPage(true);
                return;
            }
            if (idx == 6)
            {
                ShowMusicPage(true);
                return;
            }
            if (idx == 7)
            {
                ShowPuzzlePage(true);
                return;
            }
            if (idx == 8)
            {
                LaunchSamPicker();
                return;
            }
            if (idx == 9)
            {
                LaunchDnkhTool();
                return;
            }
            if (pageReset != null && pageReset.Visible) ShowResetPage(false);
            if (pageClean != null && pageClean.Visible) ShowCleanPage(false);
            if (pageGcm != null && pageGcm.Visible) ShowGcmPage(false);
            if (pageVideo != null && pageVideo.Visible) ShowVideoPage(false);
            if (pageMusic != null && pageMusic.Visible) ShowMusicPage(false);
            curAction = ACTIONS[idx];
            curActionColor = ACTION_COLOR[idx];
            lblPeer.Text = ACTIONS[idx];
            lblPeer.ForeColor = curActionColor;
            for (int i = 0; i < ACTIONS.Length; i++)
            {
                sidebarSel = idx;
                convItem[i].BackColor = (i == idx) ? C_SEL : C_SIDE;
                convName[i].ForeColor = (i == idx) ? Color.White : C_TEXT;
                foreach (Control c in convItem[i].Controls)
                    if (c is Panel && c.Width == 3 && c.Height == 32) c.Visible = (i == idx);
            }
            if (!taskActive) { lblStatus.Text = T("状态：空闲", "Status: Idle"); lblStatus.ForeColor = C_SUB; }
        }

        // ================= 任务 =================
        string CurrentMode()
        {
            if (rbDatetime.Checked) return "datetime";
            if (rbDaily.Checked) return "daily";
            if (rbWeekly.Checked) return "weekly";
            return "countdown";
        }

        void StartTask()
        {
            if (taskActive) { MessageBox.Show(T("已有任务在进行中，请先取消。", "A task is already running. Cancel it first."), T("提示", "Notice"), MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            string mode = CurrentMode();
            cfg.Mode = mode;
            cfg.Action = curAction;
            cfg.NeedConfirm = chkConfirm.Checked;
            cfg.Sound = chkSound.Checked;
            cfg.ConfirmSecs = (int)numConfirm.Value;
            cfg.Hours = (int)numH.Value; cfg.Mins = (int)numM.Value; cfg.Secs = (int)numS.Value;
            cfg.AtTime = dtpTime.Value;
            cfg.Week = new bool[7];
            for (int i = 0; i < 7; i++) cfg.Week[i] = weekCb[i].Checked;

            bool periodic = false;
            DateTime target;
            int total;
            string desc;
            switch (mode)
            {
                case "countdown":
                    total = cfg.Hours * 3600 + cfg.Mins * 60 + cfg.Secs;
                    if (total <= 0) { MessageBox.Show(T("倒计时时间必须大于 0。", "Countdown must be greater than 0."), T("提示", "Notice"), MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                    target = DateTime.Now.AddSeconds(total);
                    desc = "将在 " + Fmt(total) + " 后" + curAction;
                    break;
                case "datetime":
                    target = cfg.AtTime;
                    if (target <= DateTime.Now) { MessageBox.Show(T("指定的时间必须在当前时间之后。", "The time must be in the future."), T("提示", "Notice"), MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                    total = (int)(target - DateTime.Now).TotalSeconds;
                    desc = "将在 " + target.ToString("yyyy-MM-dd HH:mm:ss") + " 执行" + curAction;
                    break;
                case "daily":
                    periodic = true;
                    target = CalcNextDaily(dtpDaily.Value);
                    total = Math.Max((int)(target - DateTime.Now).TotalSeconds, 1);
                    desc = "每天 " + dtpDaily.Value.ToString("HH:mm") + " 自动" + curAction;
                    break;
                case "weekly":
                    periodic = true;
                    if (!AnyWeekChecked()) { MessageBox.Show(T("请至少选择一个星期。", "Select at least one weekday."), T("提示", "Notice"), MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                    target = CalcNextWeekly(dtpWeekly.Value, cfg.Week);
                    total = Math.Max((int)(target - DateTime.Now).TotalSeconds, 1);
                    desc = "每周" + WeekNames() + " " + dtpWeekly.Value.ToString("HH:mm") + " 自动" + curAction;
                    break;
                default:
                    return;
            }

            taskActive = true;
            endTime = target;
            totalSec = total;
            curDesc = desc;
            confirmShown = false;
            cfg.Active = !periodic;
            cfg.Periodic = periodic;
            lblTimeSub.Text = periodic ? T("周期任务运行中", "Periodic task running") : T("任务进行中", "Task running");
            lblTyping.Visible = true;
            lblDetail.Text = desc;
            lblDetail.ForeColor = C_TEXT;
            lblStatus.Text = T("状态：任务进行中", "Status: Running");
            lblStatus.ForeColor = C_ON;
            ringPanel.Invalidate();
            Config.Save(BASE_DIR, cfg);
            Log("[设置] " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " -> " + desc);
        }

        bool AnyWeekChecked()
        {
            foreach (CheckBox cb in weekCb) if (cb.Checked) return true;
            return false;
        }

        string WeekNames()
        {
            string[] wd = langEn ? new[] { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" } : new[] { "一", "二", "三", "四", "五", "六", "日" };
            var sb = new StringBuilder();
            for (int i = 0; i < 7; i++) if (weekCb[i].Checked) sb.Append(wd[i]);
            return sb.ToString();
        }

        DateTime CalcNextDaily(DateTime at)
        {
            DateTime t = DateTime.Today.Add(at.TimeOfDay);
            if (t <= DateTime.Now) t = t.AddDays(1);
            return t;
        }

        DateTime CalcNextWeekly(DateTime at, bool[] week)
        {
            DateTime t = DateTime.Today.Add(at.TimeOfDay);
            for (int i = 0; i < 8; i++)
            {
                int dow = ((int)t.DayOfWeek + 6) % 7; // 周一=0
                if (week[dow] && t > DateTime.Now) return t;
                t = t.AddDays(1);
            }
            return t;
        }

        void CancelTask()
        {
            if (!taskActive) { MessageBox.Show(T("当前没有进行中的任务。", "No task is currently running."), T("提示", "Notice"), MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            taskActive = false;
            totalSec = 0;
            curDesc = "";
            confirmShown = false;
            cfg.Active = false;
            cfg.Periodic = false;
            if (confirmWin != null && !confirmWin.IsDisposed) confirmWin.CancelByUser();
            lblTime.Text = "--:--:--";
            lblTimeSub.Text = T("空闲 · 已取消", "Idle · Cancelled");
            lblTyping.Visible = false;
            lblDetail.Text = T("任务已取消，可重新设置", "Task cancelled — set a new one");
            lblDetail.ForeColor = Color.FromArgb(109, 118, 134);
            lblStatus.Text = "状态：空闲";
            lblStatus.ForeColor = C_SUB;
            ringPanel.Invalidate();
            Config.Save(BASE_DIR, cfg);
            Log("[取消] " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        }

        void Immediate()
        {
            if (MessageBox.Show("确定要立即" + curAction + "吗？\n未保存的工作可能丢失。", "确认", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            bool ok = RunSystemAction(curAction);
            Log("[立即执行] " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + curAction + (ok ? " 成功" : " 失败"));
            if (!ok) MessageBox.Show("操作执行失败。", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        // ================= 计时 =================
        void Tick()
        {
            if (!taskActive) return;
            double rem = (endTime - DateTime.Now).TotalSeconds;
            if (rem <= 0)
            {
                // 到点
                if (cfg.Periodic)
                {
                    if (cfg.NeedConfirm && !confirmShown)
                    {
                        confirmShown = true;
                        ShowConfirm(true);
                    }
                    else if (!cfg.NeedConfirm)
                    {
                        ExecutePeriodic();
                    }
                }
                else
                {
                    taskActive = false;
                    if (cfg.NeedConfirm && !confirmShown)
                    {
                        confirmShown = true;
                        ShowConfirm(false);
                    }
                    else if (!cfg.NeedConfirm)
                    {
                        ExecuteOnce();
                    }
                }
                return;
            }
            lblTime.Text = Fmt((int)rem);
            lblTimeSub.Text = cfg.Periodic ? "下次执行 " + endTime.ToString("HH:mm:ss") : "任务进行中";
            ringPanel.Invalidate();
        }

        string Fmt(int s)
        {
            int hh = s / 3600, mm = (s % 3600) / 60, ss = s % 60;
            return hh.ToString("00") + ":" + mm.ToString("00") + ":" + ss.ToString("00");
        }

        void ShowConfirm(bool periodic)
        {
            if (cfg.Sound) { try { SystemSounds.Exclamation.Play(); } catch { } }
            if (confirmWin != null && !confirmWin.IsDisposed) confirmWin.Close();
            confirmWin = new ConfirmForm(curAction, cfg.ConfirmSecs, langEn,
                () => { if (periodic) ExecutePeriodic(); else ExecuteOnce(); },
                () =>
                {
                    if (periodic)
                    {
                        // 跳过本次，计算下一次
                        confirmShown = false;
                        if (cfg.Mode == "daily") endTime = CalcNextDaily(dtpDaily.Value);
                        else endTime = CalcNextWeekly(dtpWeekly.Value, cfg.Week);
                        totalSec = Math.Max((int)(endTime - DateTime.Now).TotalSeconds, 1);
                        Log("[周期跳过] " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " 跳过本次" + curAction);
                    }
                    else
                    {
                        taskActive = false;
                        totalSec = 0;
                        curDesc = "";
                        confirmShown = false;
                        lblTime.Text = "--:--:--";
                        lblTimeSub.Text = "空闲 · 已取消";
                        lblTyping.Visible = false;
                        lblDetail.Text = "到点后你选择了取消";
                        lblDetail.ForeColor = Color.FromArgb(109, 118, 134);
                        lblStatus.Text = "状态：空闲";
                        lblStatus.ForeColor = C_SUB;
                        cfg.Active = false; cfg.Periodic = false;
                        Log("[到点取消] " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " 用户取消" + curAction);
                    }
                    ringPanel.Invalidate();
                    Config.Save(BASE_DIR, cfg);
                });
            confirmWin.ShowDialog(this);
        }

        void ExecuteOnce()
        {
            taskActive = false;
            totalSec = 0;
            curDesc = "";
            confirmShown = false;
            cfg.Active = false; cfg.Periodic = false;
            lblTime.Text = "--:--:--";
            lblTimeSub.Text = "空闲";
            lblTyping.Visible = false;
            lblDetail.Text = "已执行「" + curAction + "」";
            lblDetail.ForeColor = Color.FromArgb(109, 118, 134);
            lblStatus.Text = "状态：空闲";
            lblStatus.ForeColor = C_SUB;
            ringPanel.Invalidate();
            Config.Save(BASE_DIR, cfg);
            Log("[执行] " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + curAction);
            RunSystemAction(curAction);
        }

        void ExecutePeriodic()
        {
            confirmShown = false;
            // 计算下一次
            if (cfg.Mode == "daily") endTime = CalcNextDaily(dtpDaily.Value);
            else endTime = CalcNextWeekly(dtpWeekly.Value, cfg.Week);
            totalSec = Math.Max((int)(endTime - DateTime.Now).TotalSeconds, 1);
            Log("[周期执行] " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + curAction + "，下次 " + endTime.ToString("yyyy-MM-dd HH:mm:ss"));
            Config.Save(BASE_DIR, cfg);
            if (cfg.Sound) { try { SystemSounds.Asterisk.Play(); } catch { } }
            RunSystemAction(curAction);
        }

        // ================= 日志 / 关于 =================
        void ShowLog()
        {
            LogForm f = new LogForm(logFile, langEn);
            f.ShowDialog(this);
        }

        void LaunchSamPicker()
        {
            string exeDir = Path.GetDirectoryName(Application.ExecutablePath);
            string samDir = Path.Combine(exeDir, "sam_tool");
            string picker = Path.Combine(samDir, "SAM.Picker.exe");
            if (!File.Exists(picker))
            {
                Cursor.Current = Cursors.WaitCursor;
                bool ok = UnpackResource("SamZip", samDir, "Steam 成就管理");
                Cursor.Current = Cursors.Default;
                picker = Path.Combine(samDir, "SAM.Picker.exe");
                if (!ok || !File.Exists(picker))
                {
                    MessageBox.Show(this, "未找到 Steam 成就管理组件（sam_tool\\SAM.Picker.exe）且自动部署失败。\n请重新安装小工具箱后再试。", "小工具箱", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                MessageBox.Show(this, "Steam 成就管理组件已自动部署完成。", "小工具箱", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            if (Process.GetProcessesByName("steam").Length == 0)
            {
                MessageBox.Show(this, "Steam 未运行。\n请先启动 Steam 并登录账号，再打开 Steam 成就管理。", "小工具箱", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try
            {
                Process.Start(new ProcessStartInfo(picker) { WorkingDirectory = samDir });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "启动 Steam 成就管理失败：" + ex.Message, "小工具箱", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ================= 电脑开荒（dnkhw，MIT 开源） =================
        string DnkhToolDir()
        {
            return Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "dnkh_tool");
        }

        void LaunchDnkhTool()
        {
            string dir = DnkhToolDir();
            string exe = Path.Combine(dir, "Dnkhw.exe");
            if (!File.Exists(exe))
            {
                Cursor.Current = Cursors.WaitCursor;
                bool ok = UnpackResource("DnkhZip", dir, "电脑开荒");
                Cursor.Current = Cursors.Default;
                exe = Path.Combine(dir, "Dnkhw.exe");
                if (!ok || !File.Exists(exe))
                {
                    MessageBox.Show(this, "未找到电脑开荒组件（dnkh_tool\\Dnkhw.exe）且自动部署失败。\n请重新安装小工具箱后再试。", "小工具箱", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                MessageBox.Show(this, "电脑开荒组件已自动部署完成（绿色本地组件，无需联网安装）。", "小工具箱", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            try
            {
                Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = dir });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "启动电脑开荒失败：" + ex.Message, "小工具箱", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        void ShowAbout()
        {
            string msg;
            if (langEn)
            {
                msg = "Mini Toolbox v3.0\r\n\r\n"
                    + "Features:\r\n"
                    + "【Scheduled Shutdown】\r\n"
                    + "· 2 power actions: Shutdown / Lock\r\n"
                    + "· 4 timer modes: Countdown / At Time / Daily / Weekly\r\n"
                    + "· Confirm popup (with auto-execute timeout) & sound\r\n"
                    + "· Lives in system tray; closing the window never stops a running task\r\n"
                    + "· Task log, auto-start, config auto-save & restore\r\n\r\n"
                    + "【Reinstall System】\r\n"
                    + "· Official reset wizard: keep files or wipe\r\n"
                    + "· Restart into WinRE, open recovery settings\r\n\r\n"
                    + "【C: Cleanup】\r\n"
                    + "· Balanced cleanup: whitelist-protected, safe\r\n"
                    + "· Deep cleanup: full system cache scan\r\n"
                    + "· Engine based on open-source Light-Help (GPL-3.0)\r\n\r\n"
                    + "【Game Cheats】\r\n"
                    + "· Single-player trainer manager: FLiNG, Xiaoxing, CE scripts, GCM-exclusive, community\r\n"
                    + "· One-click download, unzip, archive & auto-update\r\n"
                    + "· Based on Game-Cheats-Manager (GPL-3.0), single-player only\r\n\r\n"
                    + "【Steam Achievements】\r\n"
                    + "· One-click launch Steam Achievement Manager: unlock/lock achievements, edit stats\r\n"
                    + "· Requires Steam running & signed in\r\n\r\n"
                    + "【Puzzle Game】\r\n"
                    + "· Unlimited online images: downloads only when you press Next\r\n"
                    + "· 3x3 / 4x4 modes, drag & drop, auto timer\r\n"
                    + "· Cache auto-cleaned on exit\r\n\r\n"
                    + "【Video Download】\r\n"
                    + "· 1000+ sites: Bilibili, Douyin, Kuaishou, YouTube, Weibo, Tencent, iQiyi, Youku...\r\n"
                    + "· Paste link, parse quality, visual progress & speed\r\n"
                    + "· Auto FFmpeg merge for HD formats, based on yt-dlp\r\n\r\n"
                    + "【Music Download】\r\n"
                    + "· Search NetEase library, double-click to download MP3\r\n"
                    + "· Paste NetEase / QQ Music links to download\r\n\r\n"
                    + "【PC Setup】\r\n"
                    + "· New-PC setup: health check, Bing wallpaper, drive relocation\r\n"
                    + "· Pure software install (winget), startup manager, disk analysis\r\n"
                    + "· Green local component (dnkhw, MIT), data stays local\r\n\r\n"
                    + "Safety: save your work before power actions; confirm popup recommended.";
            }
            else
            {
                msg = "小工具箱 v3.0\r\n\r\n"
                    + "功能介绍：\r\n"
                    + "【定时关机】\r\n"
                    + "· 2 种电源操作：关机 / 锁定\r\n"
                    + "· 4 种计时方式：倒计时 / 指定时间 / 每天重复 / 每周指定\r\n"
                    + "· 到点弹窗确认（可设超时自动执行）、执行提示音\r\n"
                    + "· 系统托盘常驻，任务进行中关闭窗口不中断\r\n"
                    + "· 任务日志记录、开机自启、配置自动保存恢复\r\n\r\n"
                    + "【一键重装系统】\r\n"
                    + "· 打开官方重装向导：保留文件或清除重装\r\n"
                    + "· 重启进入恢复环境、直达系统恢复设置\r\n\r\n"
                    + "【C盘清理】\r\n"
                    + "· 平衡保护清理：白名单保护，温和安全\r\n"
                    + "· 极致深度清理：全量扫描系统缓存\r\n"
                    + "· 引擎基于开源项目 Light-Help（GPL-3.0）\r\n\r\n"
                    + "【游戏作弊】\r\n"
                    + "· 单机游戏修改器管理：风灵月影 / 小幸姐 / CE 脚本 / GCM 专属 / 社区上传五大来源\r\n"
                    + "· 中英文即时搜索，双击一键下载、解压、归档与自动更新\r\n"
                    + "· 基于开源项目 Game-Cheats-Manager（GPL-3.0），严格仅限单机模式\r\n\r\n"
                    + "【Steam 成就】\r\n"
                    + "· 一键打开 Steam 成就管理器：解锁/锁定成就、修改统计数据\r\n"
                    + "· 需先启动 Steam 并登录账号\r\n\r\n"
                    + "【拼图小游戏】\r\n"
                    + "· 在线图库无上限：点「下一张」才联网下载，每张都是新图\r\n"
                    + "· 3x3 / 4x4 两种难度，拖动拼块完成拼图，自动计时\r\n"
                    + "· 退出拼图页自动清理本地缓存，不留痕迹\r\n\r\n"
                    + "【视频下载】\r\n"
                    + "· 支持 B站 / 抖音 / 快手 / YouTube / 微博 / 腾讯 / 爱奇艺 / 优酷等 1000+ 平台\r\n"
                    + "· 粘贴链接一键解析清晰度，可视化进度、速度与剩余时间\r\n"
                    + "· 高清格式自动下载 FFmpeg 合并，基于开源项目 yt-dlp\r\n\r\n"
                    + "【音乐下载】\r\n"
                    + "· 网易云曲库在线搜索，双击即下载 MP3\r\n"
                    + "· 支持粘贴网易云 / QQ音乐 等歌曲链接下载\r\n\r\n"
                    + "【电脑开荒】\r\n"
                    + "· 新机开荒一站式：系统体检、Bing 壁纸、默认路径换盘\r\n"
                    + "· 纯净软件安装（winget 官方源）、启动项管理、系统优化、磁盘分析\r\n"
                    + "· 绿色本地组件（基于开源项目 dnkhw，MIT），数据仅存本地不上传\r\n\r\n"
                    + "安全提示：执行电源操作前请保存工作，建议开启确认弹窗。";
            }
            MessageBox.Show(this, msg, T("关于", "About"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ================= 系统操作 =================
        bool RunSystemAction(string action)
        {
            try
            {
                switch (action)
                {
                    case "关机":
                        Process.Start(new ProcessStartInfo("shutdown", "/s /t 0 /f") { CreateNoWindow = true });
                        return true;
                    case "锁定":
                        Process.Start(new ProcessStartInfo("rundll32.exe", "user32.dll,LockWorkStation") { CreateNoWindow = true });
                        return true;
                }
            }
            catch { return false; }
            return false;
        }
    }

    // ================= 到点确认窗 =================
    class ConfirmForm : Form
    {
        Action onConfirm, onCancel;
        System.Windows.Forms.Timer t;
        int remain;
        string action;
        bool langEn;
        Label lblMsg, lblCount;
        string T(string zh, string en) { return langEn ? en : zh; }

        public ConfirmForm(string act, int secs, bool en, Action confirm, Action cancel)
        {
            action = act;
            langEn = en;
            onConfirm = confirm;
            onCancel = cancel;
            remain = secs;
            Text = T("到点确认", "Time Reached");
            ClientSize = new Size(420, 200);
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = Color.FromArgb(31, 35, 41);
            Font = new Font("Microsoft YaHei UI", 9F);
            TopMost = true;

            Label title = new Label();
            title.Text = T("已到预定时间", "Time is up");
            title.ForeColor = Color.FromArgb(230, 233, 238);
            title.Font = new Font("Microsoft YaHei UI", 15F, FontStyle.Bold);
            title.Location = new Point(20, 18);
            title.AutoSize = true;
            Controls.Add(title);

            lblMsg = new Label();
            lblMsg.Text = T("是否执行「" + action + "」？", "Execute \"" + action + "\" now?");
            lblMsg.ForeColor = Color.FromArgb(139, 149, 165);
            lblMsg.Font = new Font("Microsoft YaHei UI", 10F);
            lblMsg.Location = new Point(20, 58);
            lblMsg.AutoSize = true;
            Controls.Add(lblMsg);

            lblCount = new Label();
            lblCount.Text = remain + T(" 秒后自动执行", "s auto-execute");
            lblCount.ForeColor = Color.FromArgb(61, 126, 255);
            lblCount.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold);
            lblCount.Location = new Point(20, 88);
            lblCount.AutoSize = true;
            Controls.Add(lblCount);

            Button bYes = new Button();
            bYes.Text = T("立即执行", "Run Now");
            bYes.BackColor = Color.FromArgb(196, 61, 43);
            bYes.ForeColor = Color.White;
            bYes.FlatStyle = FlatStyle.Flat;
            bYes.FlatAppearance.BorderSize = 0;
            bYes.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            bYes.Size = new Size(175, 40);
            bYes.Location = new Point(20, 136);
            bYes.Click += (s, e) => { t.Stop(); DialogResult = DialogResult.OK; onConfirm(); Close(); };
            Controls.Add(bYes);

            Button bNo = new Button();
            bNo.Text = T("取消", "Cancel");
            bNo.BackColor = Color.FromArgb(51, 58, 70);
            bNo.ForeColor = Color.FromArgb(154, 164, 178);
            bNo.FlatStyle = FlatStyle.Flat;
            bNo.FlatAppearance.BorderSize = 0;
            bNo.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            bNo.Size = new Size(175, 40);
            bNo.Location = new Point(210, 136);
            bNo.Click += (s, e) => { t.Stop(); DialogResult = DialogResult.Cancel; onCancel(); Close(); };
            Controls.Add(bNo);

            t = new System.Windows.Forms.Timer();
            t.Interval = 1000;
            t.Tick += (s, e) =>
            {
                remain--;
                lblCount.Text = remain + T(" 秒后自动执行", "s auto-execute");
                if (remain <= 0)
                {
                    t.Stop();
                    DialogResult = DialogResult.OK;
                    onConfirm();
                    Close();
                }
            };
            t.Start();
        }

        public void CancelByUser()
        {
            if (t != null) t.Stop();
            if (!IsDisposed) Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (t != null) t.Stop();
            base.OnFormClosing(e);
        }
    }

    // ================= 日志查看器 =================
    class LogForm : Form
    {
        TextBox txt;
        string path;
        bool langEn;
        string T(string zh, string en) { return langEn ? en : zh; }

        public LogForm(string logPath, bool en)
        {
            path = logPath;
            langEn = en;
            Text = T("任务日志", "Task Log");
            ClientSize = new Size(560, 420);
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.FromArgb(31, 35, 41);
            Font = new Font("Microsoft YaHei UI", 9F);
            try { this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            Panel head = new Panel();
            head.BackColor = Color.FromArgb(38, 43, 51);
            head.Dock = DockStyle.Top;
            head.Height = 48;
            Controls.Add(head);

            Label t = new Label();
            t.Text = T("任务日志（shutdown_history.log）", "Task Log (shutdown_history.log)");
            t.ForeColor = Color.FromArgb(230, 233, 238);
            t.Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold);
            t.Location = new Point(16, 13);
            t.AutoSize = true;
            head.Controls.Add(t);

            txt = new TextBox();
            txt.Multiline = true;
            txt.ReadOnly = true;
            txt.ScrollBars = ScrollBars.Both;
            txt.BackColor = Color.FromArgb(25, 29, 36);
            txt.ForeColor = Color.FromArgb(200, 208, 220);
            txt.Font = new Font("Consolas", 9.5F);
            txt.BorderStyle = BorderStyle.None;
            txt.Dock = DockStyle.Fill;
            Controls.Add(txt);

            Panel foot = new Panel();
            foot.BackColor = Color.FromArgb(30, 34, 41);
            foot.Dock = DockStyle.Bottom;
            foot.Height = 52;
            Controls.Add(foot);

            Button bRefresh = MakeBtn(T("刷新", "Refresh"), 16, 8, 90, 34);
            bRefresh.Click += (s, e) => Reload();
            foot.Controls.Add(bRefresh);

            Button bClear = MakeBtn(T("清空日志", "Clear Log"), 116, 8, 100, 34);
            bClear.Click += (s, e) => ClearLog();
            foot.Controls.Add(bClear);

            Button bClose = MakeBtn(T("关闭", "Close"), 450, 8, 90, 34);
            bClose.Click += (s, e) => Close();
            foot.Controls.Add(bClose);

            Reload();
        }

        Button MakeBtn(string text, int x, int y, int w, int h)
        {
            RoundBtn b = new RoundBtn(Color.FromArgb(43, 58, 80), Color.FromArgb(230, 233, 238));
            b.Text = text;
            b.ForeColor = Color.FromArgb(230, 233, 238);
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.Font = new Font("Microsoft YaHei UI", 9F);
            b.Cursor = Cursors.Hand;
            b.Location = new Point(x, y);
            b.Size = new Size(w, h);
            return b;
        }

        void Reload()
        {
            try
            {
                txt.Text = File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : "（暂无日志）";
            }
            catch { txt.Text = "（读取日志失败）"; }
            txt.SelectionStart = txt.TextLength;
            txt.ScrollToCaret();
        }

        void ClearLog()
        {
            if (MessageBox.Show("确定清空全部日志吗？", "确认", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            try { File.WriteAllText(path, "", Encoding.UTF8); } catch { }
            Reload();
        }
    }

    // ============ 美化工具：圆角路径 / 颜色加减（命名空间级，全局共用） ============
    static class UiKit
    {
        public static GraphicsPath RoundedPath(Rectangle rc, int r)
        {
            GraphicsPath p = new GraphicsPath();
            int d = r * 2;
            p.AddArc(rc.X, rc.Y, d, d, 180, 90);
            p.AddArc(rc.Right - d, rc.Y, d, d, 270, 90);
            p.AddArc(rc.Right - d, rc.Bottom - d, d, d, 0, 90);
            p.AddArc(rc.X, rc.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
        public static Color Lighten(Color c, int a) { return Color.FromArgb(Math.Min(255, c.R + a), Math.Min(255, c.G + a), Math.Min(255, c.B + a)); }
        public static Color Darken(Color c, int a) { return Color.FromArgb(Math.Max(0, c.R - a), Math.Max(0, c.G - a), Math.Max(0, c.B - a)); }
    }

    // 圆角渐变按钮：全局美化（渐变底色 + hover 提亮 + 按下变暗）
    class RoundBtn : Button
    {
        public Color Bg, Fg;
        bool hover_, down_;
        public RoundBtn(Color bg, Color fg) { Bg = bg; Fg = fg; TabStop = false; }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rc = ClientRectangle; rc.Inflate(-1, -1);
            using (GraphicsPath path = UiKit.RoundedPath(rc, 7))
            {
                Color c = hover_ ? UiKit.Lighten(Bg, 26) : (down_ ? UiKit.Darken(Bg, 20) : Bg);
                using (LinearGradientBrush br = new LinearGradientBrush(rc, UiKit.Lighten(c, 10), UiKit.Darken(c, 12), 90f))
                    g.FillPath(br, path);
                TextRenderer.DrawText(g, Text, Font, rc, Fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
        }
        protected override void OnMouseEnter(EventArgs e) { hover_ = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover_ = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs mevent) { down_ = true; Invalidate(); base.OnMouseDown(mevent); }
        protected override void OnMouseUp(MouseEventArgs mevent) { down_ = false; Invalidate(); base.OnMouseUp(mevent); }
    }
}
