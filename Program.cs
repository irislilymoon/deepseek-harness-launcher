using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace DeepSeekRunner
{
    class Program
    {
        // dsh 打印认证 URL 前的默认值（正常情况下会被启动输出覆盖）
        private static volatile string webUrl = "http://127.0.0.1:3080";
        private static Process dshProcess;
        private static NotifyIcon trayIcon;
        private static DateTime startTime;

        // ---- 单实例 ----
        private static Mutex instanceMutex;          // 持有互斥，防止重复实例
        private static EventWaitHandle openSignal;   // 二次启动时通知已有实例打开页面

        // ---- 菜单配色（贴近参考图：白卡片、灰色悬停、浅灰分隔线）----
        private static readonly Color HoverGray = Color.FromArgb(0xEC, 0xEC, 0xF0);
        private static readonly Color SeparatorGray = Color.FromArgb(0xE3, 0xE3, 0xE8);
        private static readonly Color TextMain = Color.FromArgb(0x1B, 0x1F, 0x27);

        // Win11 系统级窗口圆角：DWM 直接提供抗锯齿圆角与标准柔和阴影
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWCP_ROUND = 2;

        [STAThread]
        static void Main(string[] args)
        {
            // ---- 单实例：再次双击时仅通知已有实例打开浏览器页面，本进程直接退出 ----
            bool createdNew;
            instanceMutex = new Mutex(true, "Local\\DeepseekHarness", out createdNew);
            if (!createdNew)
            {
                TrySignalExisting();
                return;
            }
            openSignal = new EventWaitHandle(false, EventResetMode.AutoReset,
                "Local\\DeepseekHarnessOpen");
            Thread waiter = new Thread(WaitOpenSignal);
            waiter.IsBackground = true;
            waiter.Start();

            startTime = DateTime.Now;

            // 隐藏终端启动 dsh web（dsh 自行用默认浏览器打开带 token 的认证页面）
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c npx --yes @deepseek-ai/dsh web",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            try
            {
                dshProcess = Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageBox.Show("启动失败: " + ex.Message, "DeepSeek Harness",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // 捕获 dsh 打印的认证 URL（形如 "dsh web: http://127.0.0.1:3080/?..."）
            dshProcess.OutputDataReceived += OnOutputLine;
            dshProcess.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e) { };
            dshProcess.BeginOutputReadLine();
            dshProcess.BeginErrorReadLine();

            // 后台监视：dsh 子进程退出时，托盘程序同步退出
            Thread monitor = new Thread(MonitorChild);
            monitor.IsBackground = true;
            monitor.Start();

            // 托盘图标与右键菜单
            ContextMenuStrip menu = BuildMenu();

            trayIcon = new NotifyIcon();
            trayIcon.Icon = BuildTrayIcon();
            trayIcon.Text = "DeepSeek Harness";
            trayIcon.ContextMenuStrip = menu;
            trayIcon.MouseClick += OnTrayMouseClick;   // 单击左键打开页面
            trayIcon.Visible = true;

            Application.Run();
        }

        static ContextMenuStrip BuildMenu()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Font = new Font("Segoe UI", 9.75f, FontStyle.Regular, GraphicsUnit.Point);
            menu.ShowImageMargin = false;
            menu.BackColor = Color.White;
            menu.Renderer = new ModernMenuRenderer();
            menu.Padding = Padding.Empty;

            ToolStripMenuItem itemOpen = new ToolStripMenuItem("打开 Web 页面", null, OnOpenPage);
            itemOpen.Padding = new Padding(20, 9, 20, 9);
            itemOpen.ForeColor = TextMain;

            ToolStripMenuItem itemExit = new ToolStripMenuItem("退出", null, OnExit);
            itemExit.Padding = new Padding(20, 9, 20, 9);
            itemExit.ForeColor = TextMain;

            ToolStripSeparator sep = new ToolStripSeparator();
            sep.Margin = new Padding(16, 4, 16, 4);

            menu.Items.Add(itemOpen);
            menu.Items.Add(sep);
            menu.Items.Add(itemExit);

            // Win11 DWM 系统圆角：必须在显示前（HandleCreated）应用，
            // 否则阴影窗口偶尔残留半透明线框；失败则退化为直角，不影响功能
            menu.HandleCreated += delegate(object s, EventArgs e)
            {
                try
                {
                    int pref = DWMWCP_ROUND;
                    DwmSetWindowAttribute(menu.Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
                }
                catch { }
            };
            return menu;
        }

        // 由内嵌的 deepseek-color.png 按系统托盘尺寸高质量渲染，保证清晰
        static Icon BuildTrayIcon()
        {
            try
            {
                using (Stream s = typeof(Program).Assembly
                    .GetManifestResourceStream("DeepSeekRunner.deepseek-color.png"))
                {
                    if (s == null) return SystemIcons.Application;
                    using (Bitmap src = new Bitmap(s))
                    {
                        int w = SystemInformation.SmallIconSize.Width;
                        if (w < 16) w = 16;
                        Bitmap bmp = new Bitmap(w, w);
                        using (Graphics g = Graphics.FromImage(bmp))
                        {
                            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                            g.SmoothingMode = SmoothingMode.HighQuality;
                            g.DrawImage(src, new Rectangle(0, 0, w, w));
                        }
                        return Icon.FromHandle(bmp.GetHicon());
                    }
                }
            }
            catch
            {
                return Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
        }

        static void OnTrayMouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) OnOpenPage(null, EventArgs.Empty);
        }

        static void OnOutputLine(object sender, DataReceivedEventArgs e)
        {
            if (e.Data == null) return;
            Match m = Regex.Match(e.Data, @"^dsh web:\s+(http://\S+)");
            if (m.Success) webUrl = m.Groups[1].Value;
        }

        // 向已有实例发送"打开页面"信号（短重试覆盖首实例启动瞬间的窗口期）
        static void TrySignalExisting()
        {
            for (int i = 0; i < 10; i++)
            {
                try
                {
                    using (EventWaitHandle existing =
                        EventWaitHandle.OpenExisting("Local\\DeepseekHarnessOpen"))
                    {
                        existing.Set();
                        return;
                    }
                }
                catch (WaitHandleCannotBeOpenedException) { }
                catch (Exception) { return; }
                Thread.Sleep(200);
            }
        }

        // 后台等待：收到二次启动信号 → 用认证 URL 打开页面，浏览器自动带到前台
        static void WaitOpenSignal()
        {
            while (true)
            {
                openSignal.WaitOne();
                OnOpenPage(null, EventArgs.Empty);
            }
        }

        static void OnOpenPage(object sender, EventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(webUrl) { UseShellExecute = true });
            }
            catch { }
        }

        static void OnExit(object sender, EventArgs e)
        {
            KillTree();
            ExitApp();
        }

        // 终止 cmd -> npx -> node 整个进程树（等效于原来关闭终端窗口）
        static void KillTree()
        {
            try
            {
                if (dshProcess != null && !dshProcess.HasExited)
                {
                    Process.Start(new ProcessStartInfo("taskkill",
                        "/PID " + dshProcess.Id + " /T /F")
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }).WaitForExit(5000);
                }
            }
            catch { }
        }

        static void MonitorChild()
        {
            while (true)
            {
                Thread.Sleep(2000);
                try
                {
                    if (dshProcess == null || dshProcess.HasExited)
                    {
                        bool early = (DateTime.Now - startTime).TotalSeconds < 10;
                        int code = 0;
                        try { code = dshProcess.ExitCode; } catch { }
                        if (early)
                        {
                            MessageBox.Show(
                                "DeepSeek Harness 启动失败或已退出（退出码 " + code + "）。\r\n" +
                                "请确认已安装 Node.js，且命令行可运行 npx。",
                                "DeepSeek Harness",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        }
                        ExitApp();
                        return;
                    }
                }
                catch { }
            }
        }

        static void ExitApp()
        {
            if (trayIcon != null)
            {
                trayIcon.Visible = false;
                trayIcon.Dispose();
            }
            Application.Exit();
        }

        // ---- 扁平菜单渲染：纯白卡片 + 灰色悬停 + 浅灰分隔线，文字几何级垂直居中 ----
        class ModernMenuRenderer : ToolStripProfessionalRenderer
        {
            public ModernMenuRenderer() { RoundedEdges = false; }

            protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
            {
                using (SolidBrush b = new SolidBrush(Color.White))
                    e.Graphics.FillRectangle(b, e.AffectedBounds);
            }

            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
            {
                // 无边框：圆角与柔和阴影由系统 DWM 提供
            }

            protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
            {
                if (!e.Item.Selected) return;
                using (SolidBrush bg = new SolidBrush(HoverGray))
                    e.Graphics.FillRectangle(bg, new Rectangle(Point.Empty, e.Item.Size));
            }

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                // 量出字形墨迹的精确边界，按墨迹（而非行框）做垂直居中，
                // 避免字体行框上下留白不等导致文字视觉偏移
                using (SolidBrush b = new SolidBrush(e.TextColor))
                using (StringFormat sf = new StringFormat(StringFormat.GenericTypographic))
                {
                    sf.FormatFlags |= StringFormatFlags.NoWrap;
                    float dy = 0f;
                    try
                    {
                        sf.SetMeasurableCharacterRanges(
                            new[] { new CharacterRange(0, e.Text.Length) });
                        using (Region r = e.Graphics.MeasureCharacterRanges(
                            e.Text, e.Item.Font, new RectangleF(0, 0, 4096, 4096), sf)[0])
                        {
                            RectangleF ink = r.GetBounds(e.Graphics);
                            dy = (e.TextRectangle.Height - ink.Height) / 2f - ink.Top;
                        }
                    }
                    catch { }
                    e.Graphics.DrawString(e.Text, e.Item.Font, b,
                        e.TextRectangle.Left, e.TextRectangle.Top + dy, sf);
                }
            }

            protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
            {
                Rectangle r = e.Item.ContentRectangle;
                int y = r.Top + r.Height / 2;
                using (Pen p = new Pen(SeparatorGray))
                    e.Graphics.DrawLine(p, 16, y, e.Item.Width - 16, y);
            }
        }
    }
}
