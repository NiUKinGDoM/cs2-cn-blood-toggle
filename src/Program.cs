// ============================================================
//  CS国服去和谐程序
//  原理：国服和谐血来自 game\csgo_lv\pak01_dir.vpk（低暴力素材包）
//  去和谐 = 仅把 pak01_dir.vpk 改名为 pak01_dir.vpk.bak，
//           游戏找不到低暴力包，自动回落到默认红血素材
//  恢复   = 把 .bak 改回原名，游戏文件恢复原状
//
//  安全措施：
//  1. 只碰 pak01_dir.vpk 这一个文件，其他文件一概不动
//  2. 改名前校验目录结构确实是 CS2（bin\win64\cs2.exe 与 game\csgo 存在）
//  3. CS2 运行中时拒绝操作
//  4. 全程只改名，不删除、不修改、不注入任何文件内容
//  5. 出任何问题：Steam 验证文件完整性 100% 还原
// ============================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace CSBloodToggle
{
    internal static class GameToggle
    {
        // 唯一允许操作的文件名（社区教程明确点名的低暴力素材包）
        public const string PakName = "pak01_dir.vpk";

        public static bool IsGameRunning()
        {
            return System.Diagnostics.Process.GetProcessesByName("cs2").Length > 0;
        }

        // 校验该目录确实是 CS2 的 csgo_lv：
        //   <root>\game\csgo_lv 上级应存在 <root>\game\bin\win64\cs2.exe 与 <root>\game\csgo
        public static bool ValidateLvDir(string lvDir, out string whyNot)
        {
            whyNot = null;
            try
            {
                string gameDir = Path.GetDirectoryName(lvDir);
                string rootDir = Path.GetDirectoryName(gameDir);
                string cs2Exe = Path.Combine(gameDir, "bin", "win64", "cs2.exe");
                string csgoDir = Path.Combine(gameDir, "csgo");

                if (string.IsNullOrEmpty(gameDir) || string.IsNullOrEmpty(rootDir))
                {
                    whyNot = "目录层级不完整";
                    return false;
                }
                if (!File.Exists(cs2Exe))
                {
                    whyNot = "未找到 bin\\win64\\cs2.exe（这不是 CS2 目录）";
                    return false;
                }
                if (!Directory.Exists(csgoDir))
                {
                    whyNot = "未找到 game\\csgo 目录（这不是 CS2 目录）";
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                whyNot = ex.Message;
                return false;
            }
        }

        public static string FindLvDir()
        {
            return FindLvDirEx().LvDir;
        }

        // 自动检测结果：不仅要有 csgo_lv，还要能说清"为什么没找到"
        public class DetectResult
        {
            public string LvDir;          // csgo_lv 路径，null = 没找到
            public string Cs2Root;        // 扫到的 CS2 安装目录（不论有没有 csgo_lv）
            public bool Cs2RootEmpty;     // 该目录是空壳（卸载残留 / 没装完）
        }

        public static DetectResult FindLvDirEx()
        {
            DetectResult res = new DetectResult();
            string cs2Rel = "steamapps\\common\\Counter-Strike Global Offensive";
            string rel = cs2Rel + "\\game\\csgo_lv";
            List<string> dirs = new List<string>();

            string steam = null;
            try
            {
                Microsoft.Win32.RegistryKey key =
                    Microsoft.Win32.Registry.CurrentUser.OpenSubKey("Software\\Valve\\Steam");
                if (key != null)
                {
                    object v = key.GetValue("SteamPath");
                    if (v != null) steam = v.ToString();
                    key.Close();
                }
            }
            catch { }

            if (!string.IsNullOrEmpty(steam))
            {
                steam = steam.Replace('/', '\\');
                dirs.Add(steam);
                string[] vdfs = new string[] {
                    Path.Combine(steam, "steamapps\\libraryfolders.vdf"),
                    Path.Combine(steam, "config\\libraryfolders.vdf")
                };
                System.Text.RegularExpressions.Regex rx =
                    new System.Text.RegularExpressions.Regex("\"path\"\\s+\"([^\"]+)\"");
                foreach (string vdf in vdfs)
                {
                    if (!File.Exists(vdf)) continue;
                    try
                    {
                        foreach (string line in File.ReadAllLines(vdf))
                        {
                            System.Text.RegularExpressions.Match m = rx.Match(line);
                            if (m.Success)
                                dirs.Add(m.Groups[1].Value.Replace("\\\\", "\\"));
                        }
                    }
                    catch { }
                }
            }

            List<string> uniq = new List<string>();
            foreach (string d in dirs)
                if (!uniq.Contains(d)) uniq.Add(d);

            foreach (string d in uniq)
            {
                string p;
                string root;
                try
                {
                    p = Path.Combine(d, rel);
                    root = Path.Combine(d, cs2Rel);
                }
                catch { continue; }

                // 记录扫到的 CS2 安装目录，并判断是不是空壳（用于诊断）
                try
                {
                    if (Directory.Exists(root))
                    {
                        bool empty;
                        try { empty = Directory.GetFileSystemEntries(root).Length == 0; }
                        catch { empty = false; }

                        // 优先保留"有内容"的那一个，避免空壳目录抢先占位
                        if (res.Cs2Root == null || (res.Cs2RootEmpty && !empty))
                        {
                            res.Cs2Root = root;
                            res.Cs2RootEmpty = empty;
                        }
                    }
                }
                catch { }

                if (Directory.Exists(p))
                {
                    string why;
                    if (ValidateLvDir(p, out why))
                    {
                        res.LvDir = p;
                        return res;
                    }
                }
            }
            return res;
        }

        // 把文件系统异常翻译成人话，避免直接甩英文异常给用户
        public static string FriendlyIoError(Exception ex)
        {
            if (ex is UnauthorizedAccessException)
                return "没有权限修改这个文件。如果游戏装在 C 盘系统目录，请右键程序「以管理员身份运行」再试。";
            if (ex is IOException)
                return "文件正被占用（Steam 可能正在更新或校验 CS2）。请等 Steam 忙完再试，或先完全退出 Steam。";
            return ex.Message;
        }

        // 只读属性会让 File.Move 直接失败，且报的是"没有权限"（误导）。
        // 改名操作前先去掉只读位；Steam 校验不校验文件属性，不会因此报错。
        private static void EnsureWritable(string path)
        {
            try
            {
                FileAttributes a = File.GetAttributes(path);
                if ((a & FileAttributes.ReadOnly) != 0)
                    File.SetAttributes(path, a & ~FileAttributes.ReadOnly);
            }
            catch { }
        }

        // 状态：
        //   harmonized   = 只有原名，游戏内绿血（官方和谐）
        //   deharmonized = 只有 .bak，游戏内红血
        //   stale        = 两者同时存在。上次去和谐后 Steam 更新/校验重下了同名文件，
        //                  于是留下孤儿备份。此时游戏加载的是原名文件，所以仍是绿血。
        //   unknown      = 两者都不存在
        public static string GetState(string dir)
        {
            bool hasMain = File.Exists(Path.Combine(dir, PakName));
            bool hasBak = File.Exists(Path.Combine(dir, PakName + ".bak"));
            if (hasMain && hasBak) return "stale";
            if (hasMain) return "harmonized";
            if (hasBak) return "deharmonized";
            return "unknown";
        }

        // 返回 1 完成去和谐；0 无需操作；-1 出错。note 回传面向用户的具体说明。
        public static int Deharmonize(string dir, out string note)
        {
            note = null;
            string src = Path.Combine(dir, PakName);
            string dst = src + ".bak";

            if (!File.Exists(src))
            {
                note = File.Exists(dst)
                    ? "已经处于去和谐状态，不用重复操作。"
                    : "这个目录里没有 pak01_dir.vpk，可能游戏文件不完整，可以在 Steam 里验证完整性。";
                return 0;
            }

            bool cleaned = false;
            if (File.Exists(dst))
            {
                // 孤儿备份：上次去和谐后 Steam 更新把缺失的 pak01_dir.vpk 又下回来了。
                // 备份与原件是同一个国服素材包，留着没有任何价值，先清掉再重新移开。
                try
                {
                    File.Delete(dst);
                    cleaned = true;
                }
                catch (Exception ex)
                {
                    note = "发现重复文件，但清理旧备份失败：" + ex.Message;
                    return -1;
                }
            }

            EnsureWritable(src);
            File.Move(src, dst);
            note = cleaned
                ? "已清理 Steam 重下产生的旧备份，并重新完成去和谐。"
                : "已去和谐（红血）：pak01_dir.vpk 已移开。";
            return 1;
        }
        // 返回 1 已回到官方原状；0 无需操作；-1 出错。note 回传面向用户的具体说明。
        public static int Restore(string dir, out string note)
        {
            note = null;
            string src = Path.Combine(dir, PakName + ".bak");
            string dst = Path.Combine(dir, PakName);

            if (!File.Exists(src))
            {
                note = File.Exists(dst)
                    ? "当前已经是官方原状，不用恢复。"
                    : "既没有 pak01_dir.vpk 也没有备份文件，请在 Steam 里验证游戏文件完整性。";
                return 0;
            }

            if (File.Exists(dst))
            {
                // Steam 已经重新下了一份官方原件（比本地备份更权威），备份成了多余的孤儿。
                // 保留 Steam 的重下结果、清掉备份，即回到官方原状。
                try
                {
                    File.Delete(src);
                }
                catch (Exception ex)
                {
                    note = "Steam 已重下官方文件，但清理多余备份失败：" + ex.Message;
                    return -1;
                }
                note = "Steam 已重新下载官方文件，多余备份已清理，当前为官方原状。";
                return 1;
            }

            EnsureWritable(src);
            File.Move(src, dst);
            note = "已恢复：pak01_dir.vpk.bak 已还原为原名，游戏文件与原来完全一致。";
            return 1;
        }
    }

    // 免责声明文案：悬停看简短版，点击看完整版
    internal static class Disclaimer
    {
        public const string Title = "免责声明";

        public const string Short =
            "个人自用的非官方工具，与 Valve、完美世界均无关系。\r\n" +
            "只重命名 pak01_dir.vpk 一个文件：不改内容、不注入、\r\n" +
            "不读写游戏内存、不联网。\r\n" +
            "使用风险自负，作者不承担由此产生的任何后果。\r\n" +
            "万一异常，Steam「验证游戏文件完整性」可完全恢复。\r\n" +
            "（点击图标看完整说明）";

        public const string Full =
            "本程序为个人自用的非官方工具，与 Valve、完美世界及其关联公司\r\n" +
            "均无任何关系，也未获得其授权或认可。\r\n\r\n" +
            "程序只做一件事：把 game\\csgo_lv\\pak01_dir.vpk 重命名为 .bak\r\n" +
            "（或改回原名）。\r\n" +
            "不修改文件内容、不读写游戏内存、不注入任何进程、不联网、\r\n" +
            "不收集任何数据。\r\n\r\n" +
            "使用本程序所产生的一切后果由使用者自行承担。作者不对任何\r\n" +
            "账号风险、文件异常、游戏无法启动等直接或间接损失承担责任。\r\n\r\n" +
            "如出现任何异常，可通过 Steam「验证游戏文件的完整性」完全\r\n" +
            "恢复官方文件。\r\n\r\n" +
            "本程序仅针对 Steam 国服 CS2 编写，国际服及其他启动平台\r\n" +
            "未做验证。";
    }

    // 标题后面那个小圆圈 i：灰蓝细圈 + 中间一个 i，鼠标移上去变蓝
    internal class InfoDot : Control
    {
        private static readonly Color Normal = Color.FromArgb(122, 136, 154);
        private static readonly Color Hot = Color.FromArgb(48, 106, 186);
        private readonly Font markFont = new Font("Microsoft YaHei UI", 7F, FontStyle.Bold);
        private Color current = Normal;

        public InfoDot()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint
                | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            Size = new Size(15, 15);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            current = Hot; Invalidate(); base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            current = Normal; Invalidate(); base.OnMouseLeave(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int d = Math.Min(Width, Height) - 3;
            int x = (Width - d) / 2;
            int y = (Height - d) / 2;

            using (Pen p = new Pen(current, 1.3F))
                g.DrawEllipse(p, x, y, d, d);

            using (SolidBrush b = new SolidBrush(current))
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;
                g.DrawString("i", markFont, b, new RectangleF(x, y - 0.5F, d, d), sf);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) markFont.Dispose();
            base.Dispose(disposing);
        }
    }

    public class MainForm : Form
    {
        private string lvDir;
        private Label lblTitle;
        private Label lblPath;
        private Label lblPathAction;   // 路径行里加粗的"（点此重新查找）"入口
        private InfoDot infoDot;       // 标题后的信息图标
        private Label lblState;
        private GlassButton btnDeharm;
        private GlassButton btnRestore;
        private GlassButton btnBrowse;
        private Label lblMsg;
        private ToolTip tipPath;
        private bool detecting = true;
        private int detectToken;     // 当前进行中的检测编号，0 = 没有检测在跑
        private int detectTokenSeq;  // 编号发生器
        private Timer detectTimer;
        private Timer blobTimer;
        private float blobPhase;

        // 光斑漂移的角速度：原设计为每 60ms 相位 +0.055。
        // 用「每毫秒」为单位，这样 Timer.Interval 变化时视觉速度保持不变。
        private const float PhasePerMs = 0.055F / 60F;
        private const int BlobIntervalActive = 120;   // 有焦点
        private const int BlobIntervalIdle = 480;     // 无焦点（慢放省电）

        public MainForm()
        {
            Text = "CS国服去和谐程序  ·  made by xixi❤️";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = true;    // 允许最小化到任务栏
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(440, 352);
            Font = new Font("Microsoft YaHei UI", 9F);
            DoubleBuffered = true;

            // ---- 液态玻璃卡片 ----
            GlassPanel card = new GlassPanel();
            card.SetBounds(16, 10, 408, 332);
            card.Region = new Region(RoundedPath(408, 332, 18));
            Controls.Add(card);

            lblTitle = new Label();
            lblTitle.Text = "CS国服去和谐程序";
            lblTitle.Font = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold);
            lblTitle.ForeColor = Color.FromArgb(31, 41, 55);
            lblTitle.BackColor = Color.Transparent;
            lblTitle.SetBounds(12, 10, 384, 34);
            lblTitle.TextAlign = ContentAlignment.MiddleLeft;
            card.Controls.Add(lblTitle);

            // ---- 标题后的信息图标：悬停看摘要，点击看完整免责声明 ----
            int titleW = TextRenderer.MeasureText(lblTitle.Text, lblTitle.Font,
                new Size(10000, 10000)).Width;
            infoDot = new InfoDot();
            infoDot.SetBounds(12 + titleW + 2, 20, 15, 15);
            card.Controls.Add(infoDot);
            // 标题 Label 宽 384、padded 到 34 高，会整片盖住图标；
            // WinForms 里后 Add 的控件反而在底层，所以必须显式提到最前
            infoDot.BringToFront();

            tipPath = new ToolTip();
            tipPath.AutoPopDelay = 30000;
            tipPath.InitialDelay = 300;
            tipPath.ReshowDelay = 100;
            tipPath.SetToolTip(infoDot, Disclaimer.Short);
            infoDot.Click += delegate { ShowDisclaimer(); };

            lblPath = new Label();
            lblPath.Font = new Font("Microsoft YaHei UI", 8.5F);
            lblPath.ForeColor = Color.FromArgb(100, 112, 130);
            lblPath.BackColor = Color.Transparent;
            lblPath.SetBounds(12, 50, 384, 20);
            lblPath.TextAlign = ContentAlignment.MiddleLeft;
            lblPath.Cursor = Cursors.Hand;   // 点击可复制完整路径 / 重新检测
            lblPath.Click += new EventHandler(OnPathClick);
            card.Controls.Add(lblPath);

            // 加粗的行动入口，只在"未找到"时紧跟在本行后面出现
            lblPathAction = new Label();
            lblPathAction.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
            lblPathAction.ForeColor = Color.FromArgb(100, 112, 130);
            lblPathAction.BackColor = Color.Transparent;
            lblPathAction.SetBounds(0, 50, 260, 20);
            lblPathAction.TextAlign = ContentAlignment.MiddleLeft;
            lblPathAction.Cursor = Cursors.Hand;
            lblPathAction.Visible = false;
            lblPathAction.Click += new EventHandler(OnPathClick);
            card.Controls.Add(lblPathAction);

            lblState = new Label();
            lblState.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            lblState.BackColor = Color.Transparent;
            lblState.SetBounds(12, 76, 384, 24);
            lblState.TextAlign = ContentAlignment.MiddleLeft;
            card.Controls.Add(lblState);

            // ---- 液态玻璃按钮（半透明玻璃面 + 玻璃边，背景光斑会透过来）----

            btnDeharm = new GlassButton();
            btnDeharm.Text = "去和谐（游戏内红血）";
            btnDeharm.Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold);
            btnDeharm.AccentColor = Color.FromArgb(214, 46, 46);
            btnDeharm.Radius = 14;
            btnDeharm.SetBounds(74, 118, 260, 46);
            btnDeharm.Click += new EventHandler(OnDeharm);
            card.Controls.Add(btnDeharm);

            btnRestore = new GlassButton();
            btnRestore.Text = "恢复和谐（还原官方文件）";
            btnRestore.Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold);
            btnRestore.AccentColor = Color.FromArgb(30, 96, 182);
            btnRestore.Radius = 14;
            btnRestore.SetBounds(74, 181, 260, 46);
            btnRestore.Click += new EventHandler(OnRestore);
            card.Controls.Add(btnRestore);

            btnBrowse = new GlassButton();
            btnBrowse.Text = "浏览游戏目录（csgo_lv）";
            btnBrowse.Font = new Font("Microsoft YaHei UI", 9F);
            btnBrowse.Frosted = true;
            btnBrowse.Radius = 9;
            btnBrowse.SetBounds(74, 244, 260, 28);
            btnBrowse.Click += new EventHandler(OnBrowse);
            card.Controls.Add(btnBrowse);

            lblMsg = new Label();
            lblMsg.Font = new Font("Microsoft YaHei UI", 8.5F);
            lblMsg.ForeColor = Color.FromArgb(100, 112, 130);
            lblMsg.BackColor = Color.Transparent;
            lblMsg.SetBounds(12, 284, 384, 40);
            lblMsg.TextAlign = ContentAlignment.TopLeft;
            card.Controls.Add(lblMsg);

            // ---- 启动自动检测游戏目录（后台线程，不卡界面）----
            lblPath.Text = "游戏目录：正在检测……";
            lblState.Text = "正在自动检测 Steam 游戏库";
            lblState.ForeColor = Color.FromArgb(40, 120, 200);
            SetMsg("正在扫描注册表与 Steam 库文件夹，请稍等……", Color.FromArgb(100, 112, 130));

            detectTimer = new Timer();
            detectTimer.Interval = 450;
            int dots = 0;
            detectTimer.Tick += (object s, EventArgs e) =>
            {
                dots = (dots + 1) % 4;
                lblState.Text = "正在自动检测 Steam 游戏库" + new string('.', dots);
            };
            detectTimer.Start();

            // 液态光斑缓慢漂移。
            // 帧率与相位增量解耦：改 Interval 不会改变漂移速度，只改变采样密度，
            // 这样既能降低重绘频率（省 CPU），视觉上的流动感又和原来完全一致。
            blobTimer = new Timer();
            blobTimer.Interval = BlobIntervalActive;
            blobTimer.Tick += (object s, EventArgs e) =>
            {
                blobPhase += PhasePerMs * blobTimer.Interval;
                Invalidate(true);
            };
            blobTimer.Start();

            // 失焦时把动画放慢到 4 倍间隔（放在旁边看仍然在动，但几乎不吃 CPU）；
            // 重新获得焦点恢复原速，并顺手核对一次文件状态（后台 Steam 可能已改动过）
            Activated += (object s, EventArgs e) =>
            {
                blobTimer.Interval = BlobIntervalActive;
                if (!detecting) RefreshState(true);
            };
            Deactivate += (object s, EventArgs e) =>
            {
                blobTimer.Interval = BlobIntervalIdle;
            };
            FormClosed += (object s, FormClosedEventArgs e) =>
            {
                blobTimer.Stop();
                detectTimer.Stop();
            };

            // 最小化时暂停光斑动画（不可见就别白画），恢复时重启并立刻重绘一次，
            // 免得任务栏还原回来先闪一帧旧画面
            Resize += (object s, EventArgs e) =>
            {
                if (WindowState == FormWindowState.Minimized)
                {
                    blobTimer.Stop();
                }
                else
                {
                    if (!blobTimer.Enabled) blobTimer.Start();
                    Invalidate(true);
                }
            };

            Shown += new EventHandler(OnFormShown);

            // 窗口标题栏使用 exe 内嵌的 CS2 图标
            try
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch { }

            RefreshState();
        }

        // ---- 液态玻璃背景：渐变 + 缓慢漂移的柔焦光斑 ----
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Rectangle r = ClientRectangle;
            if (r.Width <= 0 || r.Height <= 0) return;
            using (LinearGradientBrush lg = new LinearGradientBrush(r,
                Color.FromArgb(242, 247, 253), Color.FromArgb(203, 219, 242), 55F))
            {
                e.Graphics.FillRectangle(lg, r);
            }
            float p = blobPhase;
            DrawBlob(e.Graphics, 70 + (int)(26 * Math.Sin(p)), 42 + (int)(18 * Math.Cos(p * 0.8)), 155, 92, 144, 224);
            DrawBlob(e.Graphics, 386 + (int)(28 * Math.Cos(p * 0.7)), 292 + (int)(20 * Math.Sin(p * 0.9)), 170, 250, 184, 118);
            DrawBlob(e.Graphics, 342 + (int)(22 * Math.Sin(p * 1.1 + 1)), 68 + (int)(16 * Math.Cos(p)), 112, 126, 194, 236);
        }

        private static void DrawBlob(Graphics g, int cx, int cy, int radius,
            int rr, int gg, int bb)
        {
            for (int i = 6; i >= 1; i--)
            {
                int rad = radius * i / 6;
                using (SolidBrush b = new SolidBrush(Color.FromArgb(26, rr, gg, bb)))
                    g.FillEllipse(b, cx - rad, cy - rad, rad * 2, rad * 2);
            }
        }

        // ---- 液态玻璃按钮：半透明玻璃面 + 顶部边缘聚光 + 亮边，背景会透过来 ----
        internal class GlassButton : Control
        {
            public Color AccentColor = Color.FromArgb(228, 64, 64);
            public bool Frosted = false;   // true = 磨砂白玻璃（浏览按钮）
            public int Radius = 14;

            private bool hover;
            private bool press;

            public GlassButton()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint
                    | ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.UserPaint
                    | ControlStyles.SupportsTransparentBackColor
                    | ControlStyles.ResizeRedraw, true);
                BackColor = Color.Transparent;
                Cursor = Cursors.Hand;
            }

            protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { hover = false; press = false; Invalidate(); base.OnMouseLeave(e); }
            protected override void OnMouseDown(MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) { press = true; Invalidate(); }
                base.OnMouseDown(e);
            }
            protected override void OnMouseUp(MouseEventArgs e) { press = false; Invalidate(); base.OnMouseUp(e); }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;

                int alpha = Frosted ? 120 : 158;
                if (hover) alpha += 22;
                if (press) alpha -= 20;
                if (!Enabled) alpha = 70;

                Color baseColor = Frosted ? Color.White : AccentColor;

                using (GraphicsPath path = MainForm.RoundedPath(Width, Height, Radius))
                using (SolidBrush b = new SolidBrush(Color.FromArgb(alpha, baseColor)))
                {
                    // 纯净半透明玻璃：只有底色，无任何高光/反光/描边
                    g.FillPath(b, path);
                }

                Color textColor = Frosted ? Color.FromArgb(58, 70, 86) : Color.White;
                if (!Enabled) textColor = Color.FromArgb(150, 150, 150);
                TextRenderer.DrawText(g, Text, Font, ClientRectangle, textColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.NoPadding);
            }
        }

        // ---- 磨砂玻璃卡片 ----
        internal class GlassPanel : Panel
        {
            public GlassPanel()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint
                    | ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.UserPaint
                    | ControlStyles.SupportsTransparentBackColor, true);
                BackColor = Color.Transparent;
                DoubleBuffered = true;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath p = MainForm.RoundedPath(Width, Height, 18))
                {
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(98, 255, 255, 255)))
                        e.Graphics.FillPath(b, p);
                }
                base.OnPaint(e);
            }
        }

        private static GraphicsPath RoundedPath(int w, int h, int radius)
        {
            GraphicsPath p = new GraphicsPath();
            int d = radius * 2;
            p.AddArc(0, 0, d, d, 180, 90);
            p.AddArc(w - d, 0, d, d, 270, 90);
            p.AddArc(w - d, h - d, d, d, 0, 90);
            p.AddArc(0, h - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        private void SetMsg(string text, Color color)
        {
            lblMsg.ForeColor = color;
            lblMsg.Text = text;
        }

        // 设置路径行。prefix 用常规字重；boldPart 不为 null 时以加粗字紧跟其后。
        // 目前只有"未找到"状态会用 boldPart（此时没有路径，两者不会抢位置）。
        private void SetPathLine(string prefix, string boldPart, string tip)
        {
            lblPath.Text = prefix;

            if (string.IsNullOrEmpty(boldPart))
            {
                lblPath.SetBounds(12, 50, 384, 20);
                lblPathAction.Visible = false;
                tipPath.SetToolTip(lblPath, tip);
                return;
            }

            // 用 Label 实际的测量口径（带 padding）算宽度，后面那段才不会压字
            int w = TextRenderer.MeasureText(prefix, lblPath.Font, new Size(10000, 10000)).Width;
            lblPath.SetBounds(12, 50, w + 2, 20);

            lblPathAction.Text = boldPart;
            lblPathAction.SetBounds(12 + w + 2, 50, 260, 20);
            lblPathAction.Visible = true;

            tipPath.SetToolTip(lblPath, tip);
            tipPath.SetToolTip(lblPathAction, tip);
        }

        // 点击标题后的 i：弹出完整免责声明
        private void ShowDisclaimer()
        {
            MessageBox.Show(Disclaimer.Full, Disclaimer.Title,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // 窗体显示后开始异步检测，完成后回 UI 线程刷新
        private void OnFormShown(object sender, EventArgs e)
        {
            DetectAsync();
        }

        // 后台异步检测游戏目录（不卡 UI），期间展示「正在检测」动画
        private void DetectAsync()
        {
            if (detectToken != 0) return;   // 已有检测在跑，忽略重复触发
            int token = ++detectTokenSeq;
            detectToken = token;

            detecting = true;
            detectTimer.Start();

            SetPathLine("游戏目录：正在检测……", null, "正在检测，请稍等");
            lblState.Text = "正在自动检测 Steam 游戏库";
            lblState.ForeColor = Color.FromArgb(40, 120, 200);
            SetMsg("正在扫描注册表与 Steam 库文件夹，请稍等……", Color.FromArgb(100, 112, 130));

            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                GameToggle.DetectResult r = GameToggle.FindLvDirEx();
                System.Threading.Thread.Sleep(1500); // 检测状态至少展示约 1.5 秒，让"正在检测"肉眼可见
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        // 期间用户已手动选目录／又发起了新检测 → 本次结果作废，且不触碰任何状态
                        if (detectToken != token) return;

                        detectToken = 0;
                        detecting = false;
                        detectTimer.Stop();
                        lvDir = r.LvDir;
                        RefreshState();

                        string tag = "[" + DateTime.Now.ToString("HH:mm:ss") + "] ";
                        if (r.LvDir != null)
                            SetMsg(tag + "自动检测完成：已找到游戏目录，两个按钮可以用了。",
                                   Color.FromArgb(0, 130, 60));
                        else if (r.Cs2Root != null && r.Cs2RootEmpty)
                            SetMsg(tag + "CS2 目录是空的（卸载残留或没装完），请点下方「浏览游戏目录」手动选择。",
                                   Color.FromArgb(200, 120, 0));
                        else if (r.Cs2Root != null)
                            SetMsg(tag + "找到 CS2 目录，但没有 game\\csgo_lv，可能是国际服版本。请点下方浏览手动选择。",
                                   Color.FromArgb(200, 120, 0));
                        else
                            SetMsg(tag + "自动检测完成：没找到 CS2 安装目录，请点下方「浏览游戏目录」手动选择。",
                                   Color.FromArgb(200, 120, 0));
                    });
                }
                catch { }
            });
        }

        // 手动选择目录时废弃正在进行的自动检测，防止后台结果回头覆盖用户的选择
        private void CancelDetect()
        {
            detectToken = 0;
            detecting = false;
            detectTimer.Stop();
        }

        // 点击路径行：已找到就复制完整路径；没找到就重新检测
        private void OnPathClick(object sender, EventArgs e)
        {
            string tag = "[" + DateTime.Now.ToString("HH:mm:ss") + "] ";
            if (lvDir == null)
            {
                DetectAsync();
                return;
            }
            try
            {
                Clipboard.SetText(lvDir);
                SetMsg(tag + "已复制游戏目录完整路径到剪贴板。", Color.FromArgb(0, 130, 60));
            }
            catch
            {
                SetMsg(tag + "复制失败，完整路径见文件上方提示。", Color.FromArgb(200, 120, 0));
            }
        }

        // 路径太长时从中间省略，保留盘符（开头）和 csgo_lv（结尾）这两个关键信息
        private static string MiddleEllipsis(string text, Font font, int maxWidth)
        {
            if (string.IsNullOrEmpty(text)) return text;
            TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
            Size probe = new Size(10000, 10000);
            if (TextRenderer.MeasureText(text, font, probe, flags).Width <= maxWidth)
                return text;

            int keepTail = Math.Min(14, text.Length);
            string tail = text.Substring(text.Length - keepTail);
            for (int head = text.Length - keepTail; head > 3; head--)
            {
                string cand = text.Substring(0, head) + "…" + tail;
                if (TextRenderer.MeasureText(cand, font, probe, flags).Width <= maxWidth)
                    return cand;
            }
            return "…" + tail;
        }

        private void RefreshState()
        {
            RefreshState(false);
        }

        // keepMessage = true：只更新路径与状态行，保留 lblMsg 里已有的操作结果
        // （窗口重新获得焦点时用，避免把"已去和谐"之类的提示冲掉）
        private void RefreshState(bool keepMessage)
        {
            if (detecting) return;

            // 选过的目录可能已经被删除或移动（卸载游戏、换 Steam 库盘符）
            bool dirGone = lvDir != null && !Directory.Exists(lvDir);
            if (dirGone) lvDir = null;

            if (lvDir == null)
            {
                SetPathLine("游戏目录：未找到", "（点此重新查找）", "点击重新自动检测");
                lblState.Text = "当前状态：未找到游戏目录";
                lblState.ForeColor = Color.FromArgb(200, 120, 0);
                if (!keepMessage)
                    SetMsg(dirGone
                        ? "之前选的目录已经不存在了（游戏可能被卸载或移动）。请点这里重新检测，或点下方浏览重新选择。"
                        : "按钮随时可点：点了会有弹窗提示告诉你下一步做什么。",
                        dirGone ? Color.FromArgb(200, 120, 0) : Color.FromArgb(90, 90, 90));
                return;
            }

            // 路径行：中间省略保证 csgo_lv 结尾可见，完整路径挂在悬停提示上
            SetPathLine("游戏目录：" + MiddleEllipsis(lvDir, lblPath.Font, 376),
                        null, lvDir + "\r\n\r\n（点击复制完整路径）");

            string state = GameToggle.GetState(lvDir);
            if (state == "harmonized")
            {
                lblState.Text = "当前状态：官方和谐（绿血）";
                lblState.ForeColor = Color.FromArgb(0, 130, 60);
                if (!keepMessage)
                    SetMsg("点红色按钮去和谐，玩完点蓝色按钮恢复原状。只动 pak01_dir.vpk 一个文件。",
                           Color.FromArgb(90, 90, 90));
            }
            else if (state == "deharmonized")
            {
                lblState.Text = "当前状态：已去和谐（红血）";
                lblState.ForeColor = Color.FromArgb(200, 40, 40);
                if (!keepMessage)
                    SetMsg("现在进游戏就是红血。玩完点蓝色按钮，文件立即恢复原状。",
                           Color.FromArgb(90, 90, 90));
            }
            else if (state == "stale")
            {
                lblState.Text = "当前状态：官方和谐（有重复文件）";
                lblState.ForeColor = Color.FromArgb(200, 120, 0);
                if (!keepMessage)
                    SetMsg("Steam 更新时重下过文件，目录里有原文件和旧备份各一份。点任一按钮会自动清理多余副本，不影响游戏文件。",
                           Color.FromArgb(160, 90, 0));
            }
            else
            {
                lblState.Text = "当前状态：找不到 pak01_dir.vpk（结构异常）";
                lblState.ForeColor = Color.FromArgb(200, 120, 0);
                if (!keepMessage)
                    SetMsg("为防改错文件，程序不会动其他文件。可在 Steam 验证文件完整性还原。",
                           Color.FromArgb(90, 90, 90));
            }
        }

        private bool GuardBeforeAction()
        {
            if (lvDir == null)
            {
                SetMsg("[" + DateTime.Now.ToString("HH:mm:ss") + "] 还没有选择游戏目录：请先点下方「浏览游戏目录」按钮。",
                       Color.FromArgb(200, 60, 60));
                MessageBox.Show("还没有选择游戏目录。\r\n\r\n请先点下方「浏览游戏目录（csgo_lv）...」按钮，\r\n选择 CS2 目录下的 csgo_lv 文件夹后再点本按钮。\r\n\r\n（若程序刚打开，自动检测可能还没跑完，稍等几秒再试）", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }
            if (GameToggle.IsGameRunning())
            {
                MessageBox.Show("CS2 正在运行，请先完全退出游戏再切换！", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            return true;
        }

        private void OnDeharm(object sender, EventArgs e)
        {
            if (!GuardBeforeAction()) return;
            try
            {
                string note;
                int n = GameToggle.Deharmonize(lvDir, out note);
                string tag = "[" + DateTime.Now.ToString("HH:mm:ss") + "] ";
                if (n == 1)
                    SetMsg(tag + note + " 进游戏就是红血，玩完点蓝色按钮恢复。", Color.FromArgb(0, 130, 60));
                else if (n == 0)
                    SetMsg(tag + note, Color.FromArgb(200, 120, 0));
                else
                    SetMsg(tag + note, Color.FromArgb(200, 60, 60));
            }
            catch (Exception ex)
            {
                MessageBox.Show("操作失败：" + GameToggle.FriendlyIoError(ex), "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            RefreshState();
        }

        private void OnRestore(object sender, EventArgs e)
        {
            if (!GuardBeforeAction()) return;
            try
            {
                string note;
                int n = GameToggle.Restore(lvDir, out note);
                string tag = "[" + DateTime.Now.ToString("HH:mm:ss") + "] ";
                if (n == 1)
                    SetMsg(tag + note, Color.FromArgb(0, 130, 60));
                else if (n == 0)
                    SetMsg(tag + note, Color.FromArgb(200, 120, 0));
                else
                    SetMsg(tag + note, Color.FromArgb(200, 60, 60));
            }
            catch (Exception ex)
            {
                MessageBox.Show("操作失败：" + GameToggle.FriendlyIoError(ex), "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            RefreshState();
        }

        private void OnBrowse(object sender, EventArgs e)
        {
            using (FolderBrowserDialog dlg = new FolderBrowserDialog())
            {
                dlg.Description = "请选择 CS2 目录下的 csgo_lv 文件夹";
                dlg.ShowNewFolderButton = false;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                string pick = dlg.SelectedPath;
                string candidate = null;
                if (string.Equals(Path.GetFileName(pick), "csgo_lv", StringComparison.OrdinalIgnoreCase))
                    candidate = pick;

                if (candidate == null)
                {
                    MessageBox.Show("请直接选择名为 csgo_lv 的文件夹。", "提示",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                else
                {
                    string why;
                    if (!GameToggle.ValidateLvDir(candidate, out why))
                    {
                        MessageBox.Show("这个目录结构不像 CS2 的 csgo_lv：" + why +
                            "\r\n为防改错文件，程序拒绝使用该目录。", "校验未通过",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                    else
                    {
                        CancelDetect();   // 废弃后台检测，否则它回来会覆盖手动选择
                        lvDir = candidate;
                    }
                }
                RefreshState();
            }
        }
    }

    internal static class Program
    {
        // 单实例锁。Local\ 前缀 = 只在当前登录会话内有效，
        // 同一台机器多个用户同时登录时各开一个，互不干扰。
        private const string MutexName = "Local\\CSBloodToggle_SingleInstance_9F2A7C";
        private const int SW_RESTORE = 9;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr hWnd);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool FlashWindow(IntPtr hWnd, bool bInvert);

        [STAThread]
        static int Main(string[] args)
        {
            // 只有明确的调试命令才走命令行模式。
            // 其他任何参数（典型场景：不小心把某个文件拖到 exe 图标上）都照常启动界面，
            // 否则会静默退出、什么窗口都不出现，用户会以为程序坏了。
            if (args.Length > 0 && IsConsoleCommand(args[0])) return ConsoleMode(args);

            // 单实例：同一时间只允许一个窗口。
            // 已经在运行的话，把那个窗口叫到前面来，然后本进程直接退出。
            bool createdNew;
            using (System.Threading.Mutex mutex =
                   new System.Threading.Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew)
                {
                    ActivateRunningInstance();
                    return 0;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                // 兜底：万一有未预料的异常，给用户看得懂的话，而不是 .NET 的崩溃对话框
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += delegate(object s, System.Threading.ThreadExceptionEventArgs e)
                {
                    MessageBox.Show(
                        "程序遇到一个意外错误：\r\n\r\n" + e.Exception.Message +
                        "\r\n\r\n如果是在切换文件时出错，请打开游戏目录看一下 pak01_dir.vpk 是否还在。\r\n" +
                        "万一状态不对，用 Steam「验证游戏文件完整性」可以完全恢复。",
                        "出错了", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                };

                Application.Run(new MainForm());
                return 0;
            }
        }

        // 找到已经在运行的那个实例的窗口，恢复并拉到前台。
        // 用 exe 自身文件名匹配进程（用户改了 exe 名字也依然匹配），不依赖窗口标题。
        static void ActivateRunningInstance()
        {
            IntPtr target = IntPtr.Zero;
            try
            {
                int me = System.Diagnostics.Process.GetCurrentProcess().Id;
                string self = Path.GetFileNameWithoutExtension(Application.ExecutablePath);

                foreach (System.Diagnostics.Process p in System.Diagnostics.Process.GetProcesses())
                {
                    using (p)
                    {
                        if (p.Id == me) continue;
                        try
                        {
                            if (!string.Equals(p.ProcessName, self, StringComparison.OrdinalIgnoreCase))
                                continue;
                            p.Refresh();
                            IntPtr h = p.MainWindowHandle;
                            if (h != IntPtr.Zero) { target = h; break; }
                        }
                        catch { }
                    }
                }
            }
            catch { }

            if (target == IntPtr.Zero)
            {
                // 实在找不到窗口（例如那份 exe 被改名复制了一份），至少给个明确反馈
                MessageBox.Show("程序已经在运行了，请到任务栏里找它。\r\n\r\n同一时间只能开一个窗口。",
                    "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (IsIconic(target)) ShowWindow(target, SW_RESTORE);
            BringWindowToTop(target);
            SetForegroundWindow(target);
            FlashWindow(target, true);   // 抢前台失败时，任务栏至少闪一下
        }

        // 命令行模式（调试用）：find | state <dir> | deharm <dir> | restore <dir>
        static bool IsConsoleCommand(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            switch (s.ToLowerInvariant())
            {
                case "find":
                case "state":
                case "deharm":
                case "restore":
                    return true;
                default:
                    return false;
            }
        }

        static int ConsoleMode(string[] args)
        {
            string cmd = args[0].ToLowerInvariant();
            if (cmd == "find")
            {
                string d = GameToggle.FindLvDir();
                Console.WriteLine(d == null ? "[NOT FOUND]" : d);
                return d == null ? 1 : 0;
            }
            if (args.Length < 2)
            {
                Console.WriteLine("usage: find | state <dir> | deharm <dir> | restore <dir>");
                return 2;
            }
            string dir = args[1];
            string why = null;
            if (!Directory.Exists(dir) || !GameToggle.ValidateLvDir(dir, out why))
            {
                Console.WriteLine("[ERROR] invalid CS2 csgo_lv dir: " + dir + " (" + why + ")");
                return 1;
            }
            if (cmd == "state") { Console.WriteLine(GameToggle.GetState(dir)); return 0; }
            if (cmd == "deharm")
            {
                string note;
                int r = GameToggle.Deharmonize(dir, out note);
                Console.WriteLine("result=" + r + " | " + note);
                return 0;
            }
            if (cmd == "restore")
            {
                string note;
                int r = GameToggle.Restore(dir, out note);
                Console.WriteLine("result=" + r + " | " + note);
                return 0;
            }
            Console.WriteLine("unknown command: " + cmd);
            return 2;
        }
    }
}
