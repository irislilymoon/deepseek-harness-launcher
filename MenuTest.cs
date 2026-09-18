using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MenuTest
{
    class Program
    {
        private static readonly Color HoverGray = Color.FromArgb(0xEC, 0xEC, 0xF0);
        private static readonly Color SeparatorGray = Color.FromArgb(0xE3, 0xE3, 0xE8);
        private static readonly Color TextMain = Color.FromArgb(0x1B, 0x1F, 0x27);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWCP_ROUND = 2;

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            ContextMenuStrip menu = BuildMenu();
            Timer t = new Timer();
            t.Interval = 8000;
            t.Tick += delegate { Application.Exit(); };
            t.Start();
            menu.Show(500, 300);
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

            ToolStripMenuItem itemOpen = new ToolStripMenuItem("打开 Web 页面");
            itemOpen.Padding = new Padding(20, 9, 20, 9);
            itemOpen.ForeColor = TextMain;

            ToolStripMenuItem itemExit = new ToolStripMenuItem("退出");
            itemExit.Padding = new Padding(20, 9, 20, 9);
            itemExit.ForeColor = TextMain;

            ToolStripSeparator sep = new ToolStripSeparator();
            sep.Margin = new Padding(16, 4, 16, 4);

            menu.Items.Add(itemOpen);
            menu.Items.Add(sep);
            menu.Items.Add(itemExit);

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
            }

            protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
            {
                if (!e.Item.Selected) return;
                using (SolidBrush bg = new SolidBrush(HoverGray))
                    e.Graphics.FillRectangle(bg, new Rectangle(Point.Empty, e.Item.Size));
            }

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                using (SolidBrush b = new SolidBrush(e.TextColor))
                using (StringFormat sf = new StringFormat(StringFormat.GenericTypographic))
                {
                    sf.FormatFlags |= StringFormatFlags.NoWrap;
                    float dy = 0f;
                    string inkStr = "?";
                    try
                    {
                        sf.SetMeasurableCharacterRanges(
                            new[] { new CharacterRange(0, e.Text.Length) });
                        using (Region r = e.Graphics.MeasureCharacterRanges(
                            e.Text, e.Item.Font, new RectangleF(0, 0, 4096, 4096), sf)[0])
                        {
                            RectangleF ink = r.GetBounds(e.Graphics);
                            inkStr = ink.ToString();
                            dy = (e.TextRectangle.Height - ink.Height) / 2f - ink.Top;
                        }
                    }
                    catch { }
                    try
                    {
                        System.IO.File.AppendAllText(
                            Environment.GetEnvironmentVariable("TEMP") + "\\menutest.log",
                            e.Text + " | TextRect=" + e.TextRectangle.ToString()
                            + " | ink=" + inkStr
                            + " | dy=" + dy.ToString("F2") + "\r\n");
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
