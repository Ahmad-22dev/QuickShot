// QuickShot - Launch-Ready Screenshot & Annotation Studio for Windows.
// Features: Smart Window Snap, Pixel Magnifier & HEX Picker, Smooth Curve Pen,
// Arrow/Line/Rect/Ellipse (Shift snap), Highlighter, Privacy Mosaic Blur, Numbered Steps,
// Text, Undo/Redo, Live Stroke Preview, Floating Desktop Pin, Delayed Countdown,
// Custom Dark Glass Menus & Toasts. Zero external dependencies (C# 5 / .NET 4).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Printing;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace QuickShot
{
    static class Native
    {
        public delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);
        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr SetWindowsHookEx(int idHook, HookProc fn, IntPtr hMod, uint threadId);
        [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr h, int n, IntPtr w, IntPtr l);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, IntPtr pid);
        [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a, uint b, bool attach);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
        [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr h);
        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hWnd, int attr, out RECT pvAttribute, int cbAttribute);
        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hWnd, int attr, out int pvAttribute, int cbAttribute);

        public static List<Rectangle> SnapshotVisibleWindows(Rectangle virtualScreen)
        {
            var list = new List<Rectangle>();
            EnumWindows(delegate(IntPtr hWnd, IntPtr lp)
            {
                if (!IsWindowVisible(hWnd) || IsIconic(hWnd)) return true;
                int ex = GetWindowLong(hWnd, -20 /* GWL_EXSTYLE */);
                if ((ex & 0x80 /* WS_EX_TOOLWINDOW */) != 0) return true;
                int cloaked = 0;
                try { if (DwmGetWindowAttribute(hWnd, 14 /* DWMWA_CLOAKED */, out cloaked, 4) == 0 && cloaked != 0) return true; } catch { }
                RECT rc = new RECT();
                bool ok = false;
                try { ok = DwmGetWindowAttribute(hWnd, 9 /* DWMWA_EXTENDED_FRAME_BOUNDS */, out rc, Marshal.SizeOf(typeof(RECT))) == 0; } catch { }
                if (!ok && !GetWindowRect(hWnd, out rc)) return true;
                Rectangle r = Rectangle.FromLTRB(rc.Left - virtualScreen.Left, rc.Top - virtualScreen.Top, rc.Right - virtualScreen.Left, rc.Bottom - virtualScreen.Top);
                r = Rectangle.Intersect(r, new Rectangle(0, 0, virtualScreen.Width, virtualScreen.Height));
                if (r.Width >= 80 && r.Height >= 60 && (r.Width < virtualScreen.Width - 2 || r.Height < virtualScreen.Height - 2))
                    list.Add(r);
                return true;
            }, IntPtr.Zero);
            return list;
        }
    }

    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            bool dpiOk = false;
            try { dpiOk = Native.SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch (EntryPointNotFoundException) { }
            if (!dpiOk) { try { Native.SetProcessDPIAware(); } catch { } }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (Array.IndexOf(args, "--self-test") >= 0)
                return RunSelfTest();

            int demoIdx = Array.IndexOf(args, "--demo-render");
            if (demoIdx >= 0 && demoIdx + 1 < args.Length)
                return RenderDemoPreview(args[demoIdx + 1], false);

            int hoverIdx = Array.IndexOf(args, "--demo-render-hover");
            if (hoverIdx >= 0 && hoverIdx + 1 < args.Length)
                return RenderDemoPreview(args[hoverIdx + 1], true);

            int pinIdx = Array.IndexOf(args, "--demo-render-pin");
            if (pinIdx >= 0 && pinIdx + 1 < args.Length)
                return RenderDemoPinPreview(args[pinIdx + 1]);

            bool fresh;
            using (var mutex = new Mutex(true, "QuickShot.SingleInstance.7f3a", out fresh))
            {
                if (!fresh)
                {
                    ToastForm.ShowToast("QuickShot is already running", "Press PrtSc or click the purple tray icon.", null);
                    Application.Run(new SingleToastContext());
                    return 0;
                }
                bool silent = Array.IndexOf(args, "--silent") >= 0;
                Application.Run(new TrayApp(silent));
                return 0;
            }
        }

        class SingleToastContext : ApplicationContext
        {
            public SingleToastContext()
            {
                var t = new System.Windows.Forms.Timer();
                t.Interval = 2200;
                t.Tick += delegate { t.Stop(); t.Dispose(); ExitThread(); };
                t.Start();
            }
        }

        static int RunSelfTest()
        {
            try
            {
                Rectangle r = new Rectangle(0, 0, 640, 400);
                using (Bitmap shot = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppRgb))
                {
                    using (Graphics g = Graphics.FromImage(shot))
                    {
                        g.Clear(Color.FromArgb(34, 42, 62));
                        using (var br = new SolidBrush(Color.FromArgb(240, 244, 250))) g.FillRectangle(br, 60, 60, 260, 180);
                    }
                    var shapes = new List<Shape>();
                    shapes.Add(new Shape { Kind = Tool.Pen, Color = UI.Accent, Width = 4f });
                    var pen = new Shape { Kind = Tool.Pen, Color = Color.FromArgb(245, 58, 68), Width = 4f };
                    pen.Pts.Add(new Point(80, 90)); pen.Pts.Add(new Point(130, 110)); pen.Pts.Add(new Point(190, 88));
                    shapes.Add(pen);
                    shapes.Add(new Shape { Kind = Tool.Line, Color = Color.Gold, Width = 4f, A = new Point(70, 160), B = new Point(220, 160) });
                    shapes.Add(new Shape { Kind = Tool.Arrow, Color = Color.FromArgb(245, 58, 68), Width = 4f, A = new Point(360, 110), B = new Point(250, 150) });
                    shapes.Add(new Shape { Kind = Tool.Rect, Color = UI.Accent, Width = 3f, A = new Point(75, 75), B = new Point(200, 145) });
                    shapes.Add(new Shape { Kind = Tool.Ellipse, Color = Color.FromArgb(0, 199, 190), Width = 3f, A = new Point(210, 75), B = new Point(310, 145) });
                    var mk = new Shape { Kind = Tool.Marker, Color = Color.Yellow, Width = 16f };
                    mk.Pts.Add(new Point(80, 200)); mk.Pts.Add(new Point(280, 200));
                    shapes.Add(mk);
                    shapes.Add(new Shape { Kind = Tool.Blur, Color = Color.Black, Width = 6f, A = new Point(80, 165), B = new Point(250, 195) });
                    shapes.Add(new Shape { Kind = Tool.Step, Color = UI.Accent, Width = 4f, A = new Point(365, 110), StepNumber = 1 });
                    shapes.Add(new Shape { Kind = Tool.Text, Color = Color.White, FontSize = 20f, A = new Point(350, 160), Text = "Launch Ready!" });

                    using (Graphics g = Graphics.FromImage(shot))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.TextRenderingHint = TextRenderingHint.AntiAlias;
                        foreach (Shape sh in shapes) sh.Draw(g, shot, false);
                    }
                    using (var pin = new PinForm((Bitmap)shot.Clone(), new Point(100, 100)))
                    {
                        if (pin.ClientSize.Width <= 0) return 2;
                        int baseW = pin.ClientSize.Width;
                        pin.SetScaleForTest(1.5f);
                        if (pin.ClientSize.Width <= baseW) return 4;
                        pin.SetScaleForTest(0.5f);
                        if (pin.ClientSize.Width >= baseW) return 5;
                    }
                    var wins = Native.SnapshotVisibleWindows(SystemInformation.VirtualScreen);
                    if (wins == null) return 3;
                }
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.ToString());
                return 1;
            }
        }

        static int RenderDemoPreview(string outPath, bool hoverSnapMode)
        {
            try
            {
                Rectangle vs = new Rectangle(0, 0, 1280, 720);
                using (Bitmap bg = new Bitmap(vs.Width, vs.Height, PixelFormat.Format32bppRgb))
                {
                    using (Graphics g = Graphics.FromImage(bg))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                        // Realistic desktop mock background (dark IDE + light doc card)
                        using (var br = new LinearGradientBrush(vs, Color.FromArgb(20, 24, 36), Color.FromArgb(32, 26, 48), 35f))
                            g.FillRectangle(br, vs);
                        Rectangle card = new Rectangle(240, 110, 720, 440);
                        using (GraphicsPath gp = UI.RoundRect(card, 14))
                        using (var br = new SolidBrush(Color.FromArgb(248, 249, 252)))
                        using (var pn = new Pen(Color.FromArgb(215, 220, 232), 1.5f))
                        {
                            g.FillPath(br, gp);
                            g.DrawPath(pn, gp);
                        }
                        using (var titleF = new Font("Segoe UI", 24, FontStyle.Bold, GraphicsUnit.Pixel))
                        using (var bodyF = new Font("Segoe UI", 15, FontStyle.Regular, GraphicsUnit.Pixel))
                        using (var monoF = new Font("Consolas", 14, FontStyle.Regular, GraphicsUnit.Pixel))
                        using (var dk = new SolidBrush(Color.FromArgb(24, 28, 38)))
                        using (var mu = new SolidBrush(Color.FromArgb(100, 108, 125)))
                        {
                            g.DrawString("QuickShot Pro — Account Settings", titleF, dk, 275, 140);
                            g.DrawString("API Token (hidden with Privacy Blur):", bodyF, mu, 275, 195);
                            g.DrawString("demo_token_9928471029_HIDDEN_EXAMPLE", monoF, dk, 275, 222);
                            g.DrawString("Hover any window to auto-snap, annotate cleanly, or Pin to desktop.", bodyF, dk, 275, 285);
                            using (var ab = new SolidBrush(UI.Accent))
                            using (GraphicsPath bp = UI.RoundRect(new RectangleF(275, 340, 180, 44), 8))
                                g.FillPath(ab, bp);
                            using (var wf = new SolidBrush(Color.White))
                                g.DrawString("Launch Build", bodyF, wf, 315, 352);
                        }
                    }
                    var mockWins = new List<Rectangle>();
                    mockWins.Add(new Rectangle(240, 110, 720, 440));
                    using (var ov = new Overlay(null, (Bitmap)bg.Clone(), vs, mockWins))
                    {
                        if (hoverSnapMode) ov.SetupDemoHoverState();
                        else ov.SetupDemoState();
                        using (Bitmap shotOut = new Bitmap(vs.Width, vs.Height, PixelFormat.Format32bppRgb))
                        {
                            ov.RenderToBitmapForTest(shotOut);
                            shotOut.Save(outPath, ImageFormat.Png);
                        }
                    }
                }
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.ToString());
                return 1;
            }
        }

        static int RenderDemoPinPreview(string outPath)
        {
            try
            {
                using (Bitmap card = new Bitmap(520, 320, PixelFormat.Format32bppRgb))
                {
                    using (Graphics g = Graphics.FromImage(card))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                        g.Clear(Color.FromArgb(248, 249, 252));
                        using (var tf = new Font("Segoe UI", 20, FontStyle.Bold, GraphicsUnit.Pixel))
                        using (var bf = new Font("Segoe UI", 14, FontStyle.Regular, GraphicsUnit.Pixel))
                        using (var dk = new SolidBrush(Color.FromArgb(24, 28, 38)))
                        using (var mu = new SolidBrush(Color.FromArgb(100, 108, 125)))
                        {
                            g.DrawString("Pinned Reference Card", tf, dk, 28, 26);
                            g.DrawString("Drag any corner or edge, scroll wheel, or click − / + to resize.", bf, mu, 28, 60);
                            using (var ab = new SolidBrush(UI.Accent))
                            using (GraphicsPath bp = UI.RoundRect(new RectangleF(28, 105, 464, 72), 10))
                                g.FillPath(ab, bp);
                            using (var wf = new SolidBrush(Color.White))
                                g.DrawString("Always on top · Double-click to copy · Ctrl+Wheel for opacity", bf, wf, 48, 132);
                        }
                    }
                    using (var pin = new PinForm((Bitmap)card.Clone(), new Point(0, 0)))
                    using (Bitmap frame = new Bitmap(680, 460, PixelFormat.Format32bppRgb))
                    {
                        pin.SetDemoHoverForTest(1.15f);
                        using (Graphics g = Graphics.FromImage(frame))
                        {
                            g.SmoothingMode = SmoothingMode.AntiAlias;
                            using (var br = new LinearGradientBrush(new Rectangle(0, 0, frame.Width, frame.Height), Color.FromArgb(18, 22, 34), Color.FromArgb(30, 24, 46), 35f))
                                g.FillRectangle(br, 0, 0, frame.Width, frame.Height);
                            int px = (frame.Width - pin.ClientSize.Width) / 2;
                            int py = (frame.Height - pin.ClientSize.Height) / 2;
                            // Soft shadow under floating pin
                            for (int i = 10; i >= 1; i--)
                                using (var sb = new SolidBrush(Color.FromArgb(14, 0, 0, 0)))
                                    g.FillRectangle(sb, px - i, py - i + 4, pin.ClientSize.Width + i * 2, pin.ClientSize.Height + i * 2);
                            g.TranslateTransform(px, py);
                            pin.RenderToGraphicsForTest(g);
                            g.ResetTransform();
                        }
                        frame.Save(outPath, ImageFormat.Png);
                    }
                }
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.ToString());
                return 1;
            }
        }
    }

    // ---------------------------------------------------------------- Design System & Custom Dark Menu
    static class UI
    {
        public static readonly Color Accent = Color.FromArgb(138, 99, 255);
        public static readonly Color AccentDeep = Color.FromArgb(98, 62, 224);
        public static readonly Color AccentGlow = Color.FromArgb(170, 140, 255);
        public static readonly Color Success = Color.FromArgb(52, 211, 153);
        public static readonly Color Danger = Color.FromArgb(248, 113, 113);

        public static readonly Color BarTop = Color.FromArgb(246, 30, 33, 44);
        public static readonly Color BarBot = Color.FromArgb(246, 20, 22, 30);
        public static readonly Color BarBorder = Color.FromArgb(255, 58, 64, 84);
        public static readonly Color BarInner = Color.FromArgb(30, 255, 255, 255);
        public static readonly Color BtnHover = Color.FromArgb(255, 48, 53, 70);
        public static readonly Color Divider = Color.FromArgb(95, 130, 140, 175);
        public static readonly Color InkLight = Color.FromArgb(244, 246, 252);
        public static readonly Color InkMuted = Color.FromArgb(156, 164, 184);

        public static GraphicsPath RoundRect(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = Math.Min(rad * 2f, Math.Min(r.Width, r.Height));
            if (d <= 0.5f) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void DrawGlassPanel(Graphics g, Rectangle r, float radius, bool shadow)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            if (shadow)
            {
                for (int i = 3; i >= 1; i--)
                {
                    Rectangle sr = new Rectangle(r.X - i + 1, r.Y + i + 1, r.Width + i * 2 - 2, r.Height + i * 2 - 2);
                    using (GraphicsPath sp = RoundRect(sr, radius + i))
                    using (var sb = new SolidBrush(Color.FromArgb(22 * (4 - i), 0, 0, 0)))
                        g.FillPath(sb, sp);
                }
            }
            using (GraphicsPath gp = RoundRect(r, radius))
            using (var bg = new LinearGradientBrush(r, BarTop, BarBot, 90f))
            using (var border = new Pen(BarBorder, 1f))
            {
                g.FillPath(bg, gp);
                g.DrawPath(border, gp);
            }
            Rectangle inner = Rectangle.Inflate(r, -1, -1);
            if (inner.Width > 4 && inner.Height > 4)
            {
                using (GraphicsPath ip = RoundRect(inner, Math.Max(1f, radius - 1f)))
                using (var hi = new Pen(BarInner, 1f))
                    g.DrawPath(hi, ip);
            }
        }

        public static ContextMenuStrip CreateDarkMenu()
        {
            var menu = new ContextMenuStrip();
            menu.ShowImageMargin = false;
            menu.Renderer = new DarkMenuRenderer();
            menu.BackColor = Color.FromArgb(24, 26, 34);
            menu.ForeColor = InkLight;
            menu.Font = new Font("Segoe UI", 9.5f);
            menu.Padding = new Padding(4, 6, 4, 6);
            return menu;
        }

        public static bool TrySetClipboardImage(Image img)
        {
            for (int i = 0; i < 6; i++)
            {
                try { Clipboard.SetImage(img); return true; }
                catch (ExternalException) { Thread.Sleep(45); }
            }
            return false;
        }

        public static bool TrySetClipboardText(string text)
        {
            for (int i = 0; i < 6; i++)
            {
                try { Clipboard.SetText(text); return true; }
                catch (ExternalException) { Thread.Sleep(45); }
            }
            return false;
        }
    }

    class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkColorTable()) { }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            Rectangle r = new Rectangle(4, 1, e.Item.Width - 8, e.Item.Height - 2);
            if (e.Item.Selected && e.Item.Enabled)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath gp = UI.RoundRect(r, 5))
                using (var br = new SolidBrush(Color.FromArgb(62, 54, 96)))
                    e.Graphics.FillPath(br, gp);
            }
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? UI.InkLight : UI.InkMuted;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using (var p = new Pen(Color.FromArgb(52, 56, 72)))
                e.Graphics.DrawLine(p, 10, y, e.Item.Width - 10, y);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using (var p = new Pen(UI.BarBorder))
                e.Graphics.DrawRectangle(p, 0, 0, e.AffectedBounds.Width - 1, e.AffectedBounds.Height - 1);
        }
    }

    class DarkColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Color.FromArgb(24, 26, 34); } }
        public override Color MenuBorder { get { return UI.BarBorder; } }
        public override Color ImageMarginGradientBegin { get { return Color.FromArgb(24, 26, 34); } }
        public override Color ImageMarginGradientMiddle { get { return Color.FromArgb(24, 26, 34); } }
        public override Color ImageMarginGradientEnd { get { return Color.FromArgb(24, 26, 34); } }
    }

    // ---------------------------------------------------------------- sleek floating dark-glass Toast
    class ToastForm : Form
    {
        static ToastForm active;
        string title, subtitle, clickPath;
        System.Windows.Forms.Timer timer;
        int phase; // 0=fadein, 1=hold, 2=fadeout
        int holdTicks;

        protected override bool ShowWithoutActivation { get { return true; } }

        ToastForm(string title, string subtitle, string clickPath)
        {
            this.title = title;
            this.subtitle = subtitle;
            this.clickPath = clickPath;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.FromArgb(22, 24, 32);
            Opacity = 0.0;
            ClientSize = new Size(310, 62);
            Cursor = clickPath != null ? Cursors.Hand : Cursors.Default;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);

            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(wa.Right - ClientSize.Width - 18, wa.Bottom - ClientSize.Height - 18);

            timer = new System.Windows.Forms.Timer();
            timer.Interval = 25;
            timer.Tick += OnTick;
            timer.Start();
        }

        public static void ShowToast(string title, string subtitle, string clickPath)
        {
            if (active != null && !active.IsDisposed) { active.Close(); active = null; }
            active = new ToastForm(title, subtitle, clickPath);
            active.Show();
        }

        void OnTick(object sender, EventArgs e)
        {
            if (phase == 0)
            {
                Opacity = Math.Min(0.96, Opacity + 0.14);
                if (Opacity >= 0.95) phase = 1;
            }
            else if (phase == 1)
            {
                holdTicks++;
                if (holdTicks >= 76) phase = 2; // ~1.9s hold
            }
            else
            {
                Opacity = Math.Max(0.0, Opacity - 0.10);
                if (Opacity <= 0.02) { timer.Stop(); Close(); }
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (clickPath != null && File.Exists(clickPath))
                try { Process.Start("explorer.exe", "/select,\"" + clickPath + "\""); } catch { }
            Close();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            Rectangle r = new Rectangle(0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
            using (var bg = new LinearGradientBrush(r, UI.BarTop, UI.BarBot, 90f)) g.FillRectangle(bg, r);
            using (var pn = new Pen(UI.BarBorder)) g.DrawRectangle(pn, r);
            // Left purple accent bar
            using (var ab = new SolidBrush(UI.Accent)) g.FillRectangle(ab, 0, 0, 4, ClientSize.Height);
            // Check badge
            using (var cb = new SolidBrush(Color.FromArgb(40, 52, 211, 153))) g.FillEllipse(cb, 16, 17, 28, 28);
            using (var cp = new Pen(UI.Success, 2.2f))
            {
                cp.StartCap = LineCap.Round; cp.EndCap = LineCap.Round; cp.LineJoin = LineJoin.Round;
                g.DrawLines(cp, new[] { new Point(24, 31), new Point(28, 35), new Point(36, 26) });
            }
            using (var tf = new Font("Segoe UI", 13, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var sf = new Font("Segoe UI", 11.5f, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var tb = new SolidBrush(UI.InkLight))
            using (var sb = new SolidBrush(UI.InkMuted))
            {
                g.DrawString(title, tf, tb, 54, 13);
                g.DrawString(subtitle, sf, sb, new RectangleF(54, 32, ClientSize.Width - 62, 22));
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            if (timer != null) { timer.Stop(); timer.Dispose(); timer = null; }
            if (active == this) active = null;
        }
    }

    // ---------------------------------------------------------------- visual countdown for Delayed Capture
    class CountdownForm : Form
    {
        int remaining;
        Action onDone;
        System.Windows.Forms.Timer timer;

        protected override bool ShowWithoutActivation { get { return true; } }

        public CountdownForm(int seconds, Action onDone)
        {
            remaining = seconds;
            this.onDone = onDone;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.FromArgb(24, 26, 36);
            Opacity = 0.94;
            ClientSize = new Size(176, 56);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(wa.Left + (wa.Width - ClientSize.Width) / 2, wa.Top + 26);

            timer = new System.Windows.Forms.Timer();
            timer.Interval = 1000;
            timer.Tick += delegate
            {
                remaining--;
                if (remaining <= 0)
                {
                    timer.Stop();
                    Close();
                    Application.DoEvents();
                    Thread.Sleep(60);
                    if (onDone != null) onDone();
                }
                else Invalidate();
            };
            timer.Start();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            Rectangle r = new Rectangle(0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
            using (var bg = new LinearGradientBrush(r, UI.BarTop, UI.BarBot, 90f)) g.FillRectangle(bg, r);
            using (var pn = new Pen(UI.Accent, 1.5f)) g.DrawRectangle(pn, r);
            using (var cb = new SolidBrush(UI.Accent)) g.FillEllipse(cb, 14, 12, 32, 32);
            using (var nf = new Font("Segoe UI", 17, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var tf = new Font("Segoe UI", 12.5f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            using (var wb = new SolidBrush(Color.White))
            {
                g.DrawString(remaining.ToString(), nf, wb, new RectangleF(14, 12, 32, 32), sf);
                g.DrawString("Capturing...", tf, wb, 56, 19);
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            if (timer != null) { timer.Stop(); timer.Dispose(); timer = null; }
        }
    }

    // ---------------------------------------------------------------- Dark Shortcuts & About Card
    class ShortcutsForm : Form
    {
        public ShortcutsForm()
        {
            Text = "QuickShot Pro — Shortcuts";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(22, 24, 32);
            ForeColor = UI.InkLight;
            ClientSize = new Size(460, 445);
            SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
            KeyPreview = true;
            KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Close(); };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            using (var tf = new Font("Segoe UI", 18, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var sub = new Font("Segoe UI", 12, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var kf = new Font("Consolas", 11.5f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var lf = new Font("Segoe UI", 12.5f, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var wb = new SolidBrush(UI.InkLight))
            using (var mb = new SolidBrush(UI.InkMuted))
            using (var ab = new SolidBrush(UI.AccentGlow))
            {
                g.DrawString("QuickShot Pro", tf, wb, 24, 18);
                g.DrawString("Fast screenshot, annotation & desktop pin utility", sub, mb, 24, 44);
                using (var p = new Pen(UI.BarBorder)) g.DrawLine(p, 24, 70, ClientSize.Width - 24, 70);

                string[][] rows = {
                    new[] { "PrtSc",           "Capture region or click any window to auto-snap" },
                    new[] { "Shift + PrtSc",   "Instantly save entire screen to Pictures\\QuickShot" },
                    new[] { "C (before drag)", "Copy HEX color under pixel magnifier" },
                    new[] { "P / L / A / R / E", "Pen, Line, Arrow, Rectangle, Ellipse (Shift snaps)" },
                    new[] { "H / B / N / T",   "Highlighter, Privacy Blur, Numbered Steps, Text" },
                    new[] { "Scroll Wheel",    "Change brush / blur / text size live" },
                    new[] { "Ctrl+Z / Ctrl+Y", "Undo / Redo annotation" },
                    new[] { "Ctrl+T",          "Pin screenshot floating on top of desktop" },
                    new[] { "Pin: Drag / + - 0", "Resize pinned shot (or Wheel; Ctrl+Wheel opacity)" },
                    new[] { "Ctrl+C / Enter",  "Copy to clipboard (or double-click selection)" },
                    new[] { "Ctrl+Shift+S",    "Quick Save PNG without dialog" },
                    new[] { "Ctrl+S / Ctrl+P", "Save As... / Print" },
                    new[] { "Esc / Right-Click", "Reset selection or close overlay" }
                };
                int y = 84;
                foreach (string[] row in rows)
                {
                    g.DrawString(row[0], kf, ab, 24, y);
                    g.DrawString(row[1], lf, wb, 160, y);
                    y += 26;
                }
            }
        }
    }

    // ---------------------------------------------------------------- tray + hotkey
    class TrayApp : ApplicationContext
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string AppName = "QuickShot";
        NotifyIcon tray;
        Icon trayIcon;
        Control invoker;
        IntPtr hook;
        Native.HookProc proc;
        Overlay overlay;
        CountdownForm countdown;

        public static string Folder
        {
            get
            {
                string f = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "QuickShot");
                Directory.CreateDirectory(f);
                return f;
            }
        }

        public static string UniqueFilePath(string dir, string prefix)
        {
            string stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string path = Path.Combine(dir, prefix + "_" + stamp + ".png");
            int n = 2;
            while (File.Exists(path))
            {
                path = Path.Combine(dir, prefix + "_" + stamp + "_" + n + ".png");
                n++;
            }
            return path;
        }

        public TrayApp(bool silent)
        {
            invoker = new Control();
            invoker.CreateControl();

            ContextMenuStrip menu = UI.CreateDarkMenu();
            menu.Items.Add("Take screenshot               PrtSc", null, delegate { StartCapture(); });
            menu.Items.Add("Delayed capture (3 sec)", null, delegate { StartDelayedCapture(3); });
            menu.Items.Add("Delayed capture (5 sec)", null, delegate { StartDelayedCapture(5); });
            menu.Items.Add("Save whole screen        Shift+PrtSc", null, delegate { SaveFullScreen(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Open screenshots folder", null, delegate { Process.Start("explorer.exe", "\"" + Folder + "\""); });
            menu.Items.Add("Keyboard shortcuts...", null, delegate { new ShortcutsForm().Show(); });
            var auto = new ToolStripMenuItem("Start with Windows");
            auto.Checked = IsAutoStart();
            auto.Click += delegate { SetAutoStart(!auto.Checked); auto.Checked = IsAutoStart(); };
            menu.Items.Add(auto);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit QuickShot", null, delegate { ExitThread(); });

            trayIcon = MakeIcon();
            tray = new NotifyIcon();
            tray.Icon = trayIcon;
            tray.Text = "QuickShot Pro — press PrtSc";
            tray.ContextMenuStrip = menu;
            tray.Visible = true;
            tray.MouseClick += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) StartCapture(); };

            proc = HookCallback;
            hook = Native.SetWindowsHookEx(13 /* WH_KEYBOARD_LL */, proc, Native.GetModuleHandle(null), 0);
            if (hook == IntPtr.Zero)
                ToastForm.ShowToast("QuickShot Pro", "Could not hook PrtSc — click the tray icon to capture.", null);
            else if (!silent)
                ToastForm.ShowToast("QuickShot Pro is ready", "Press PrtSc to capture · Window Snap, Blur & Pin active", null);
        }

        public void Notify(string title, string text, string clickPath)
        {
            ToastForm.ShowToast(title, text, clickPath);
        }

        public void NotifySaved(string path)
        {
            ToastForm.ShowToast("Screenshot saved", Path.GetFileName(path) + " · Click to reveal", path);
        }

        IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && Marshal.ReadInt32(lParam) == 0x2C /* VK_SNAPSHOT */)
            {
                bool alt = (Native.GetAsyncKeyState(0x12) & 0x8000) != 0;
                if (!alt)
                {
                    int msg = wParam.ToInt32();
                    if (msg == 0x100 || msg == 0x104)
                    {
                        bool shift = (Native.GetAsyncKeyState(0x10) & 0x8000) != 0;
                        invoker.BeginInvoke((MethodInvoker)delegate { if (shift) SaveFullScreen(); else StartCapture(); });
                    }
                    return new IntPtr(1);
                }
            }
            return Native.CallNextHookEx(hook, nCode, wParam, lParam);
        }

        void StartDelayedCapture(int seconds)
        {
            if (countdown != null && !countdown.IsDisposed) { countdown.Close(); countdown = null; }
            countdown = new CountdownForm(seconds, delegate { countdown = null; StartCapture(); });
            countdown.Show();
        }

        void StartCapture()
        {
            if (overlay != null) { overlay.ForceForeground(); return; }
            Rectangle vs = SystemInformation.VirtualScreen;
            List<Rectangle> wins = Native.SnapshotVisibleWindows(vs);
            Bitmap shot = CaptureScreen(vs);
            overlay = new Overlay(this, shot, vs, wins);
            overlay.FormClosed += delegate { overlay = null; };
            overlay.Show();
            overlay.ForceForeground();
        }

        void SaveFullScreen()
        {
            Rectangle vs = SystemInformation.VirtualScreen;
            using (Bitmap shot = CaptureScreen(vs))
            {
                string path = UniqueFilePath(Folder, "Screenshot");
                shot.Save(path, ImageFormat.Png);
                NotifySaved(path);
            }
        }

        public static Bitmap CaptureScreen(Rectangle vs)
        {
            var bmp = new Bitmap(vs.Width, vs.Height, PixelFormat.Format32bppRgb);
            using (Graphics g = Graphics.FromImage(bmp))
                g.CopyFromScreen(vs.Left, vs.Top, 0, 0, vs.Size, CopyPixelOperation.SourceCopy);
            return bmp;
        }

        static bool IsAutoStart()
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey))
                return k != null && k.GetValue(AppName) != null;
        }

        static void SetAutoStart(bool on)
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (on) k.SetValue(AppName, "\"" + Application.ExecutablePath + "\" --silent");
                else k.DeleteValue(AppName, false);
            }
        }

        static Icon MakeIcon()
        {
            using (var b = new Bitmap(32, 32))
            using (Graphics g = Graphics.FromImage(b))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (GraphicsPath path = UI.RoundRect(new RectangleF(1, 1, 30, 30), 7))
                using (var br = new LinearGradientBrush(new Rectangle(0, 0, 32, 32), Color.FromArgb(164, 108, 255), Color.FromArgb(88, 46, 198), 45f))
                    g.FillPath(br, path);
                using (var p = new Pen(Color.White, 2.8f))
                {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; p.LineJoin = LineJoin.Round;
                    g.DrawLines(p, new[] { new Point(7, 13), new Point(7, 7), new Point(13, 7) });
                    g.DrawLines(p, new[] { new Point(19, 25), new Point(25, 25), new Point(25, 19) });
                }
                using (var p = new Pen(Color.White, 2.4f))
                {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                    g.DrawLine(p, 11, 21, 21, 11);
                    g.DrawLine(p, 21, 11, 16, 11);
                    g.DrawLine(p, 21, 11, 21, 16);
                }
                IntPtr h = b.GetHicon();
                Icon ic = (Icon)Icon.FromHandle(h).Clone();
                Native.DestroyIcon(h);
                return ic;
            }
        }

        protected override void ExitThreadCore()
        {
            if (countdown != null && !countdown.IsDisposed) { countdown.Close(); countdown = null; }
            if (hook != IntPtr.Zero) { Native.UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }
            if (overlay != null) overlay.Close();
            if (tray != null) { tray.Visible = false; tray.Dispose(); }
            if (trayIcon != null) { trayIcon.Dispose(); trayIcon = null; }
            if (invoker != null) { invoker.Dispose(); invoker = null; }
            base.ExitThreadCore();
        }
    }

    // ---------------------------------------------------------------- floating Pin window (Snipaste-style + interactive resizing)
    class PinForm : Form
    {
        Bitmap img;
        float scale = 1f;
        const float MinScale = 0.15f;
        const float MaxScale = 4.0f;

        bool dragging, hovered;
        int resizeHit = -1; // 0=NW,1=N,2=NE,3=E,4=SE,5=S,6=SW,7=W
        Point dragScreenStart;
        Rectangle startBounds;
        string hoverBtn;
        string hudText;
        int hudTicks;
        System.Windows.Forms.Timer hudTimer;

        public PinForm(Bitmap bmp, Point screenPos)
        {
            img = bmp;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Location = screenPos;
            ClientSize = new Size(Math.Max(48, bmp.Width), Math.Max(48, bmp.Height));
            ShowInTaskbar = false;
            TopMost = true;
            Text = "QuickShot Pin";
            Cursor = Cursors.SizeAll;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);

            ContextMenuStrip menu = UI.CreateDarkMenu();
            menu.Items.Add("Zoom In                 +", null, delegate { AdjustScale(0.15f, true); });
            menu.Items.Add("Zoom Out                -", null, delegate { AdjustScale(-0.15f, true); });
            var sizeMenu = new ToolStripMenuItem("Set size");
            float[] presets = { 0.5f, 0.75f, 1.0f, 1.5f, 2.0f };
            foreach (float pVal in presets)
            {
                float pv = pVal;
                sizeMenu.DropDownItems.Add(((int)Math.Round(pv * 100)) + "%", null, delegate { SetScale(pv, true); });
            }
            menu.Items.Add(sizeMenu);
            menu.Items.Add("Reset size & opacity    0", null, delegate { Opacity = 1.0; SetScale(1f, true); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Copy image              Ctrl+C", null, delegate { CopyPin(); });
            menu.Items.Add("Save image...           Ctrl+S", null, delegate { SavePin(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Close pin               Esc", null, delegate { Close(); });
            ContextMenuStrip = menu;

            hudTimer = new System.Windows.Forms.Timer();
            hudTimer.Interval = 60;
            hudTimer.Tick += delegate
            {
                if (hudTicks > 0) { hudTicks--; if (hudTicks == 0) { hudText = null; hudTimer.Stop(); } Invalidate(); }
            };
        }

        public void SetScaleForTest(float newScale) { SetScale(newScale, false); }

        public void SetDemoHoverForTest(float newScale)
        {
            SetScale(newScale, false);
            hovered = true;
            hoverBtn = "plus";
            hudText = null;
        }

        public void RenderToGraphicsForTest(Graphics g)
        {
            OnPaint(new PaintEventArgs(g, new Rectangle(0, 0, ClientSize.Width, ClientSize.Height)));
        }

        void CopyPin()
        {
            if (UI.TrySetClipboardImage(img))
                ToastForm.ShowToast("Copied pin", "Image copied to clipboard", null);
        }

        void ShowHud(string text)
        {
            hudText = text;
            hudTicks = 18;
            hudTimer.Start();
            Invalidate();
        }

        float ClampScale(float target)
        {
            float minByPx = Math.Max(48f / Math.Max(1, img.Width), 48f / Math.Max(1, img.Height));
            float lo = Math.Max(MinScale, Math.Min(1f, minByPx));
            return Math.Max(lo, Math.Min(MaxScale, target));
        }

        void SetScale(float target, bool keepCenter)
        {
            float oldScale = scale;
            scale = ClampScale(target);
            int nw = Math.Max(48, (int)Math.Round(img.Width * scale));
            int nh = Math.Max(48, (int)Math.Round(img.Height * scale));
            if (keepCenter && IsHandleCreated)
            {
                int cx = Left + Width / 2;
                int cy = Top + Height / 2;
                Bounds = new Rectangle(cx - nw / 2, cy - nh / 2, nw, nh);
            }
            else
            {
                ClientSize = new Size(nw, nh);
            }
            if (Math.Abs(scale - oldScale) > 0.001f || keepCenter)
                ShowHud((int)Math.Round(scale * 100) + "%");
            Invalidate();
        }

        void AdjustScale(float delta, bool keepCenter)
        {
            SetScale(scale + delta, keepCenter);
        }

        // Hover control pill geometry (bottom-center inside pin)
        Rectangle PillRect()
        {
            if (ClientSize.Width < 110 || ClientSize.Height < 70) return Rectangle.Empty;
            bool compact = ClientSize.Width < 196;
            int w = compact ? 92 : 172;
            int h = 28;
            return new Rectangle((ClientSize.Width - w) / 2, ClientSize.Height - h - 10, w, h);
        }

        struct PinBtn { public string Id; public Rectangle R; }

        List<PinBtn> GetPillButtons()
        {
            var list = new List<PinBtn>();
            Rectangle pr = PillRect();
            if (pr.IsEmpty) return list;
            bool compact = pr.Width < 120;
            int y = pr.Y + 3, bh = pr.Height - 6;
            if (compact)
            {
                list.Add(new PinBtn { Id = "minus", R = new Rectangle(pr.X + 4, y, 26, bh) });
                list.Add(new PinBtn { Id = "plus", R = new Rectangle(pr.X + 33, y, 26, bh) });
                list.Add(new PinBtn { Id = "close", R = new Rectangle(pr.X + 62, y, 26, bh) });
            }
            else
            {
                list.Add(new PinBtn { Id = "minus", R = new Rectangle(pr.X + 4, y, 26, bh) });
                list.Add(new PinBtn { Id = "reset", R = new Rectangle(pr.X + 32, y, 48, bh) });
                list.Add(new PinBtn { Id = "plus", R = new Rectangle(pr.X + 82, y, 26, bh) });
                list.Add(new PinBtn { Id = "copy", R = new Rectangle(pr.X + 112, y, 26, bh) });
                list.Add(new PinBtn { Id = "close", R = new Rectangle(pr.X + 141, y, 26, bh) });
            }
            return list;
        }

        string HitPill(Point p)
        {
            if (!hovered) return null;
            foreach (PinBtn b in GetPillButtons())
                if (b.R.Contains(p)) return b.Id;
            return null;
        }

        int HitEdge(Point p)
        {
            int w = ClientSize.Width, h = ClientSize.Height;
            int c = 13, e = 7;
            bool left = p.X <= c, right = p.X >= w - c, top = p.Y <= c, bot = p.Y >= h - c;
            if (left && top) return 0;
            if (right && top) return 2;
            if (right && bot) return 4;
            if (left && bot) return 6;
            if (p.Y <= e) return 1;
            if (p.X >= w - e) return 3;
            if (p.Y >= h - e) return 5;
            if (p.X <= e) return 7;
            return -1;
        }

        Cursor CursorForHit(int h)
        {
            if (h == 0 || h == 4) return Cursors.SizeNWSE;
            if (h == 2 || h == 6) return Cursors.SizeNESW;
            if (h == 1 || h == 5) return Cursors.SizeNS;
            if (h == 3 || h == 7) return Cursors.SizeWE;
            return Cursors.SizeAll;
        }

        void SavePin()
        {
            using (var dlg = new SaveFileDialog())
            {
                dlg.Title = "Save pinned screenshot";
                dlg.Filter = "PNG image (*.png)|*.png|JPEG image (*.jpg)|*.jpg";
                dlg.FileName = Path.GetFileName(TrayApp.UniqueFilePath(TrayApp.Folder, "Pinned"));
                dlg.InitialDirectory = TrayApp.Folder;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                ImageFormat f = Path.GetExtension(dlg.FileName).ToLowerInvariant() == ".jpg" ? ImageFormat.Jpeg : ImageFormat.Png;
                try { img.Save(dlg.FileName, f); ToastForm.ShowToast("Pin saved", Path.GetFileName(dlg.FileName), dlg.FileName); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not save"); }
            }
        }

        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { hovered = false; hoverBtn = null; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(img, ClientRectangle);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Color rim = hovered ? UI.Accent : Color.FromArgb(165, 42, 46, 62);
            using (var p = new Pen(rim, hovered ? 2f : 1f))
                g.DrawRectangle(p, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);

            if (hovered)
            {
                // Crisp corner resize grip handles
                int d = 8;
                Point[] corners = {
                    new Point(5, 5),
                    new Point(ClientSize.Width - 6, 5),
                    new Point(ClientSize.Width - 6, ClientSize.Height - 6),
                    new Point(5, ClientSize.Height - 6)
                };
                using (var fb = new SolidBrush(Color.White))
                using (var sp = new Pen(UI.Accent, 1.8f))
                    foreach (Point c in corners)
                    {
                        g.FillEllipse(fb, c.X - d / 2, c.Y - d / 2, d, d);
                        g.DrawEllipse(sp, c.X - d / 2, c.Y - d / 2, d, d);
                    }

                // Floating dark-glass resize & quick-action pill
                Rectangle pr = PillRect();
                if (!pr.IsEmpty)
                {
                    UI.DrawGlassPanel(g, pr, 8, true);
                    var pBtns = GetPillButtons();
                    g.TextRenderingHint = TextRenderingHint.AntiAlias;
                    using (var f = new Font("Segoe UI", 11.5f, FontStyle.Bold, GraphicsUnit.Pixel))
                    using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    using (var accBr = new SolidBrush(UI.AccentGlow))
                    {
                        foreach (PinBtn b in pBtns)
                        {
                            if (hoverBtn == b.Id)
                            {
                                Color hc = b.Id == "close" ? Color.FromArgb(90, 248, 113, 113) : UI.BtnHover;
                                using (GraphicsPath gp = UI.RoundRect(b.R, 5))
                                using (var br = new SolidBrush(hc)) g.FillPath(br, gp);
                            }
                            int cx = b.R.X + b.R.Width / 2, cy = b.R.Y + b.R.Height / 2;
                            if (b.Id == "minus")
                            {
                                using (var mp = new Pen(UI.InkLight, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                                    g.DrawLine(mp, cx - 5, cy, cx + 5, cy);
                            }
                            else if (b.Id == "plus")
                            {
                                using (var pp = new Pen(UI.InkLight, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                                {
                                    g.DrawLine(pp, cx - 5, cy, cx + 5, cy);
                                    g.DrawLine(pp, cx, cy - 5, cx, cy + 5);
                                }
                            }
                            else if (b.Id == "reset")
                            {
                                g.DrawString(((int)Math.Round(scale * 100)) + "%", f, accBr, b.R, sf);
                            }
                            else if (b.Id == "copy")
                            {
                                Rectangle cr = Rectangle.Inflate(b.R, -7, -5);
                                using (var cp = new Pen(UI.InkLight, 1.5f))
                                {
                                    g.DrawRectangle(cp, cr.X + 2, cr.Y, cr.Width - 2, cr.Height - 2);
                                    g.DrawLine(cp, cr.X, cr.Y + 3, cr.X, cr.Bottom);
                                    g.DrawLine(cp, cr.X, cr.Bottom, cr.Right - 3, cr.Bottom);
                                }
                            }
                            else if (b.Id == "close")
                            {
                                using (var xp = new Pen(UI.Danger, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                                {
                                    g.DrawLine(xp, cx - 4, cy - 4, cx + 4, cy + 4);
                                    g.DrawLine(xp, cx + 4, cy - 4, cx - 4, cy + 4);
                                }
                            }
                        }
                    }
                }
            }

            if (hudText != null)
            {
                Rectangle br = new Rectangle((ClientSize.Width - 78) / 2, 10, 78, 24);
                UI.DrawGlassPanel(g, br, 6, false);
                using (var f = new Font("Segoe UI", 11.5f, FontStyle.Bold, GraphicsUnit.Pixel))
                    TextRenderer.DrawText(g, hudText, f, br, UI.InkLight, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            string hitBtn = HitPill(e.Location);
            if (hitBtn != null)
            {
                if (hitBtn == "minus") AdjustScale(-0.15f, true);
                else if (hitBtn == "plus") AdjustScale(0.15f, true);
                else if (hitBtn == "reset") { Opacity = 1.0; SetScale(1f, true); }
                else if (hitBtn == "copy") CopyPin();
                else if (hitBtn == "close") Close();
                return;
            }
            resizeHit = HitEdge(e.Location);
            dragScreenStart = PointToScreen(e.Location);
            startBounds = Bounds;
            dragging = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (dragging)
            {
                Point curScreen = PointToScreen(e.Location);
                int dx = curScreen.X - dragScreenStart.X;
                int dy = curScreen.Y - dragScreenStart.Y;
                if (resizeHit < 0)
                {
                    Location = new Point(startBounds.X + dx, startBounds.Y + dy);
                }
                else
                {
                    float wTarget = startBounds.Width;
                    float hTarget = startBounds.Height;
                    bool affectsX = resizeHit == 0 || resizeHit == 2 || resizeHit == 3 || resizeHit == 4 || resizeHit == 6 || resizeHit == 7;
                    bool affectsY = resizeHit == 0 || resizeHit == 1 || resizeHit == 2 || resizeHit == 4 || resizeHit == 5 || resizeHit == 6;
                    bool fromLeft = resizeHit == 0 || resizeHit == 6 || resizeHit == 7;
                    bool fromTop = resizeHit == 0 || resizeHit == 1 || resizeHit == 2;

                    if (affectsX) wTarget = startBounds.Width + (fromLeft ? -dx : dx);
                    if (affectsY) hTarget = startBounds.Height + (fromTop ? -dy : dy);

                    float sx = wTarget / Math.Max(1f, img.Width);
                    float sy = hTarget / Math.Max(1f, img.Height);
                    float chosen = (affectsX && affectsY) ? (Math.Abs(sx - scale) >= Math.Abs(sy - scale) ? sx : sy) : (affectsX ? sx : sy);
                    scale = ClampScale(chosen);
                    int nw = Math.Max(48, (int)Math.Round(img.Width * scale));
                    int nh = Math.Max(48, (int)Math.Round(img.Height * scale));
                    int nx = fromLeft ? startBounds.Right - nw : startBounds.Left;
                    int ny = fromTop ? startBounds.Bottom - nh : startBounds.Top;
                    if (!affectsX) nx = startBounds.Left + (startBounds.Width - nw) / 2;
                    if (!affectsY) ny = startBounds.Top + (startBounds.Height - nh) / 2;
                    Bounds = new Rectangle(nx, ny, nw, nh);
                    ShowHud((int)Math.Round(scale * 100) + "%");
                }
                return;
            }
            string hb = HitPill(e.Location);
            if (hb != hoverBtn) { hoverBtn = hb; Invalidate(); }
            if (hb != null) { if (Cursor != Cursors.Hand) Cursor = Cursors.Hand; return; }
            Cursor c = CursorForHit(HitEdge(e.Location));
            if (Cursor != c) Cursor = c;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) { dragging = false; resizeHit = -1; }
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && HitPill(e.Location) == null && HitEdge(e.Location) < 0)
                CopyPin();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if ((ModifierKeys & Keys.Control) != 0)
            {
                double op = Math.Max(0.25, Math.Min(1.0, Opacity + (e.Delta > 0 ? 0.1 : -0.1)));
                Opacity = op;
                ShowHud((int)Math.Round(op * 100) + "%");
            }
            else
            {
                AdjustScale(e.Delta > 0 ? 0.10f : -0.10f, true);
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) { Close(); e.Handled = true; }
            else if (e.KeyCode == Keys.Add || e.KeyCode == Keys.Oemplus) { AdjustScale(0.15f, true); e.Handled = true; }
            else if (e.KeyCode == Keys.Subtract || e.KeyCode == Keys.OemMinus) { AdjustScale(-0.15f, true); e.Handled = true; }
            else if (e.KeyCode == Keys.D0 || e.KeyCode == Keys.NumPad0) { Opacity = 1.0; SetScale(1f, true); e.Handled = true; }
            else if (e.Control && e.KeyCode == Keys.C) { CopyPin(); e.Handled = true; }
            else if (e.Control && e.KeyCode == Keys.S) { SavePin(); e.Handled = true; }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            if (hudTimer != null) { hudTimer.Stop(); hudTimer.Dispose(); hudTimer = null; }
            if (img != null) { img.Dispose(); img = null; }
        }
    }

    // ---------------------------------------------------------------- annotations
    enum Tool { None, Pen, Line, Arrow, Rect, Ellipse, Marker, Blur, Step, Text }

    class Shape
    {
        public Tool Kind;
        public Color Color;
        public float Width;
        public List<Point> Pts = new List<Point>();
        public Point A, B;
        public string Text = "";
        public float FontSize;
        public int StepNumber;

        static StringFormat fmt;
        static StringFormat Fmt
        {
            get
            {
                if (fmt == null)
                {
                    fmt = (StringFormat)StringFormat.GenericTypographic.Clone();
                    fmt.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;
                }
                return fmt;
            }
        }

        public static Rectangle Norm(Point a, Point b)
        {
            return Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
        }

        Pen MakePen(Color c, float w)
        {
            var p = new Pen(c, w);
            p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; p.LineJoin = LineJoin.Round;
            return p;
        }

        public void Draw(Graphics g, Bitmap sourceShot, bool editing)
        {
            switch (Kind)
            {
                case Tool.Pen:
                case Tool.Marker:
                {
                    if (Pts.Count == 0) return;
                    Color c = Kind == Tool.Marker ? Color.FromArgb(115, Color) : Color;
                    if (Pts.Count == 1)
                    {
                        using (var b = new SolidBrush(c))
                            g.FillEllipse(b, Pts[0].X - Width / 2f, Pts[0].Y - Width / 2f, Width, Width);
                    }
                    else if (Pts.Count == 2)
                    {
                        using (Pen p = MakePen(c, Width)) g.DrawLine(p, Pts[0], Pts[1]);
                    }
                    else
                    {
                        // Silky smooth curve stroke
                        using (Pen p = MakePen(c, Width)) g.DrawCurve(p, Pts.ToArray(), 0.35f);
                    }
                    break;
                }
                case Tool.Line:
                    using (Pen sh = MakePen(Color.FromArgb(75, 0, 0, 0), Width + 1.5f))
                        g.DrawLine(sh, A.X + 1, A.Y + 1.5f, B.X + 1, B.Y + 1.5f);
                    using (Pen p = MakePen(Color, Width)) g.DrawLine(p, A, B);
                    break;
                case Tool.Arrow:
                    DrawProArrow(g);
                    break;
                case Tool.Rect:
                {
                    Rectangle r = Norm(A, B);
                    if (r.Width > 0 && r.Height > 0)
                    {
                        using (Pen sh = MakePen(Color.FromArgb(70, 0, 0, 0), Width + 1.5f))
                            g.DrawRectangle(sh, r.X + 1, r.Y + 1.5f, r.Width, r.Height);
                        using (Pen p = MakePen(Color, Width)) g.DrawRectangle(p, r);
                    }
                    break;
                }
                case Tool.Ellipse:
                {
                    Rectangle r = Norm(A, B);
                    if (r.Width > 0 && r.Height > 0)
                    {
                        using (Pen sh = MakePen(Color.FromArgb(70, 0, 0, 0), Width + 1.5f))
                            g.DrawEllipse(sh, r.X + 1, r.Y + 1.5f, r.Width, r.Height);
                        using (Pen p = MakePen(Color, Width)) g.DrawEllipse(p, r);
                    }
                    break;
                }
                case Tool.Blur:
                    DrawMosaic(g, sourceShot, Norm(A, B));
                    break;
                case Tool.Step:
                    DrawStep(g);
                    break;
                case Tool.Text:
                    DrawText(g, editing);
                    break;
            }
        }

        void DrawProArrow(Graphics g)
        {
            float dx = B.X - A.X, dy = B.Y - A.Y;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 2f) return;
            float ux = dx / len, uy = dy / len;
            float headLen = Math.Min(len * 0.65f, Math.Max(14f, Width * 3.8f));
            float headW = headLen * 0.52f;
            PointF shaftEnd = new PointF(B.X - ux * (headLen * 0.78f), B.Y - uy * (headLen * 0.78f));
            PointF left = new PointF(B.X - ux * headLen - uy * headW, B.Y - uy * headLen + ux * headW);
            PointF right = new PointF(B.X - ux * headLen + uy * headW, B.Y - uy * headLen - ux * headW);

            // Soft shadow
            using (Pen sh = MakePen(Color.FromArgb(80, 0, 0, 0), Width + 1.2f))
                g.DrawLine(sh, A.X + 1, A.Y + 1.5f, shaftEnd.X + 1, shaftEnd.Y + 1.5f);
            using (var shb = new SolidBrush(Color.FromArgb(80, 0, 0, 0)))
                g.FillPolygon(shb, new[] { new PointF(B.X + 1, B.Y + 1.5f), new PointF(left.X + 1, left.Y + 1.5f), new PointF(right.X + 1, right.Y + 1.5f) });

            using (Pen p = MakePen(Color, Width))
                g.DrawLine(p, A, shaftEnd);
            using (var br = new SolidBrush(Color))
                g.FillPolygon(br, new[] { (PointF)B, left, right });
        }

        void DrawMosaic(Graphics g, Bitmap src, Rectangle area)
        {
            if (src == null) return;
            Rectangle r = Rectangle.Intersect(area, new Rectangle(0, 0, src.Width, src.Height));
            if (r.Width < 2 || r.Height < 2) return;
            int cell = Math.Max(9, (int)Math.Round(Width * 2.4f));
            int sw = Math.Max(1, r.Width / cell);
            int sh = Math.Max(1, r.Height / cell);
            using (var tiny = new Bitmap(sw, sh, PixelFormat.Format24bppRgb))
            {
                using (Graphics tg = Graphics.FromImage(tiny))
                {
                    tg.InterpolationMode = InterpolationMode.Bilinear;
                    tg.DrawImage(src, new Rectangle(0, 0, sw, sh), r, GraphicsUnit.Pixel);
                }
                InterpolationMode prevInterp = g.InterpolationMode;
                PixelOffsetMode prevOff = g.PixelOffsetMode;
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(tiny, r, new Rectangle(0, 0, sw, sh), GraphicsUnit.Pixel);
                g.InterpolationMode = prevInterp;
                g.PixelOffsetMode = prevOff;
            }
        }

        void DrawStep(Graphics g)
        {
            float rad = Math.Max(14f, 11f + Width * 1.4f);
            RectangleF cr = new RectangleF(A.X - rad, A.Y - rad, rad * 2f, rad * 2f);
            using (var sh = new SolidBrush(Color.FromArgb(100, 0, 0, 0)))
                g.FillEllipse(sh, cr.X + 1.5f, cr.Y + 2.5f, cr.Width, cr.Height);
            using (var br = new SolidBrush(Color))
                g.FillEllipse(br, cr);
            using (var ring = new Pen(Color.White, 2.2f))
                g.DrawEllipse(ring, cr);
            string num = StepNumber.ToString();
            using (var f = new Font("Segoe UI", rad * 1.15f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var tb = new SolidBrush(GetContrastInk(Color)))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString(num, f, tb, new RectangleF(cr.X, cr.Y + 0.5f, cr.Width, cr.Height), sf);
        }

        static Color GetContrastInk(Color bg)
        {
            double lum = 0.299 * bg.R + 0.587 * bg.G + 0.114 * bg.B;
            return lum > 175.0 ? Color.FromArgb(24, 24, 28) : Color.White;
        }

        void DrawText(Graphics g, bool editing)
        {
            using (var f = new Font("Segoe UI", FontSize, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                using (var halo = new SolidBrush(Color.FromArgb(150, 0, 0, 0)))
                    g.DrawString(Text, f, halo, new PointF(A.X + 1.5f, A.Y + 1.5f), Fmt);
                using (var b = new SolidBrush(Color))
                    g.DrawString(Text, f, b, A, Fmt);
                if (!editing) return;
                float lh = f.GetHeight(g);
                string[] lines = Text.Split('\n');
                SizeF sz = g.MeasureString(Text.Length == 0 ? " " : Text, f, PointF.Empty, Fmt);
                float w = Math.Max(sz.Width, 6f), h = Math.Max(sz.Height, lh);
                float lastW = g.MeasureString(lines[lines.Length - 1], f, PointF.Empty, Fmt).Width;
                float cx = A.X + lastW + 1, cy = A.Y + (lines.Length - 1) * lh;
                using (var cp = new Pen(Color, 1.8f)) g.DrawLine(cp, cx, cy, cx, cy + lh);
                using (var dp = new Pen(Color.FromArgb(200, UI.AccentGlow), 1.2f))
                {
                    dp.DashStyle = DashStyle.Dash;
                    g.DrawRectangle(dp, A.X - 5, A.Y - 3, w + 12, h + 6);
                }
            }
        }
    }

    class Btn { public string Id, Tip, KeyHint; public Rectangle R; public bool Vertical, Primary, SepBefore; }

    // ---------------------------------------------------------------- capture overlay
    class Overlay : Form
    {
        enum DragMode { None, Create, Move, Resize, Draw }

        static readonly string[][] VTools =
        {
            new[] { "pen",     "Pen",               "P",      "" },
            new[] { "line",    "Line (Shift snap)", "L",      "" },
            new[] { "arrow",   "Arrow (Shift snap)","A",      "" },
            new[] { "rect",    "Rectangle",         "R",      "" },
            new[] { "ellipse", "Ellipse",           "E",      "" },
            new[] { "marker",  "Highlighter",       "H",      "" },
            new[] { "blur",    "Privacy Blur",      "B",      "sep" },
            new[] { "step",    "Numbered Step",     "N",      "" },
            new[] { "text",    "Text",              "T",      "" },
            new[] { "color",   "Color & Size",      "Scroll", "sep" },
            new[] { "undo",    "Undo",              "Ctrl+Z", "" },
            new[] { "redo",    "Redo",              "Ctrl+Y", "" }
        };
        static readonly string[][] HTools =
        {
            new[] { "pin",       "Pin to desktop", "Ctrl+T",       "" },
            new[] { "quicksave", "Quick Save",     "Ctrl+Shift+S", "" },
            new[] { "save",      "Save As...",     "Ctrl+S",       "" },
            new[] { "print",     "Print",          "Ctrl+P",       "" },
            new[] { "copy",      "Copy",           "Ctrl+C",       "primary" },
            new[] { "close",     "Close",          "Esc",          "sep" }
        };
        static readonly Color[] Palette =
        {
            Color.FromArgb(245, 58, 68),   Color.FromArgb(255, 145, 36),  Color.FromArgb(255, 214, 10),  Color.FromArgb(76, 217, 100),
            Color.FromArgb(0, 199, 190),   Color.FromArgb(40, 140, 255),  Color.FromArgb(138, 99, 255),  Color.FromArgb(236, 72, 153),
            Color.FromArgb(255, 128, 171), Color.FromArgb(165, 105, 52),  Color.FromArgb(255, 255, 255), Color.FromArgb(208, 212, 222),
            Color.FromArgb(140, 146, 160), Color.FromArgb(72, 76, 90),    Color.FromArgb(28, 30, 38),    Color.FromArgb(0, 0, 0)
        };

        static Color color = Color.FromArgb(245, 58, 68);
        static float penWidth = 4f;
        static string lastDir;

        TrayApp app;
        Bitmap shot, dim;
        Rectangle captureBounds;
        List<Rectangle> windows;
        Rectangle hoverWin;
        Rectangle sel; bool hasSel;
        DragMode drag; int handle = -1; Point dragStart; Rectangle selStart;
        Point mousePos;
        bool mouseInside;
        Tool tool = Tool.None;
        List<Shape> shapes = new List<Shape>();
        List<Shape> redoStack = new List<Shape>();
        Shape cur, editing;
        int nextStep = 1;
        List<Btn> btns = new List<Btn>();
        Rectangle vBar, hBar, lastChrome;
        Btn hover;
        bool paletteOpen;
        string sizeToast;
        int sizeToastTicks;
        System.Windows.Forms.Timer toastTimer;
        float s = 1f;
        Font tipFont, keyFont, labelFont, letterFont, monoFont;

        public Overlay(TrayApp app, Bitmap shot, Rectangle bounds, List<Rectangle> windows)
        {
            this.app = app;
            this.shot = shot;
            this.captureBounds = bounds;
            this.windows = windows ?? new List<Rectangle>();
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = bounds;
            ShowInTaskbar = false;
            TopMost = true;
            KeyPreview = true;
            Text = "QuickShot";
            Cursor = Cursors.Cross;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque, true);

            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) s = Math.Max(1f, g.DpiX / 96f);
            letterFont = new Font("Segoe UI", 15 * s, FontStyle.Bold, GraphicsUnit.Pixel);
            tipFont = new Font("Segoe UI", 12 * s, GraphicsUnit.Pixel);
            keyFont = new Font("Consolas", 10.5f * s, FontStyle.Bold, GraphicsUnit.Pixel);
            labelFont = new Font("Segoe UI", 11.5f * s, FontStyle.Bold, GraphicsUnit.Pixel);
            monoFont = new Font("Consolas", 11 * s, GraphicsUnit.Pixel);

            dim = new Bitmap(shot.Width, shot.Height, PixelFormat.Format32bppRgb);
            using (Graphics g = Graphics.FromImage(dim))
            {
                g.DrawImage(shot, new Rectangle(0, 0, shot.Width, shot.Height));
                using (var b = new SolidBrush(Color.FromArgb(142, 8, 10, 18))) g.FillRectangle(b, 0, 0, shot.Width, shot.Height);
            }

            toastTimer = new System.Windows.Forms.Timer();
            toastTimer.Interval = 60;
            toastTimer.Tick += delegate
            {
                if (sizeToastTicks > 0)
                {
                    sizeToastTicks--;
                    if (sizeToastTicks == 0) { sizeToast = null; toastTimer.Stop(); }
                    Invalidate();
                }
            };
        }

        // Used only by --demo-render visual verification
        public void SetupDemoState()
        {
            ClientSize = captureBounds.Size;
            hasSel = true;
            sel = new Rectangle(220, 96, 760, 472);
            tool = Tool.Arrow;
            paletteOpen = true;
            shapes.Add(new Shape { Kind = Tool.Blur, Color = Color.Black, Width = 5f, A = new Point(272, 218), B = new Point(560, 245) });
            shapes.Add(new Shape { Kind = Tool.Rect, Color = UI.Accent, Width = 3f, A = new Point(268, 214), B = new Point(564, 249) });
            shapes.Add(new Shape { Kind = Tool.Arrow, Color = Color.FromArgb(245, 58, 68), Width = 4f, A = new Point(662, 232), B = new Point(572, 232) });
            shapes.Add(new Shape { Kind = Tool.Step, Color = Color.FromArgb(245, 58, 68), Width = 4f, A = new Point(682, 232), StepNumber = 1 });
            shapes.Add(new Shape { Kind = Tool.Step, Color = UI.Accent, Width = 4f, A = new Point(478, 362), StepNumber = 2 });
            shapes.Add(new Shape { Kind = Tool.Ellipse, Color = Color.FromArgb(0, 199, 190), Width = 3f, A = new Point(264, 330), B = new Point(466, 394) });
            shapes.Add(new Shape { Kind = Tool.Text, Color = Color.FromArgb(245, 58, 68), FontSize = 18f, A = new Point(708, 222), Text = "Secret hidden!" });
            LayoutBars();
            hover = btns.Find(b => b.Id == "pin");
            sizeToast = "Size: 4 px";
            sizeToastTicks = 10;
        }

        public void SetupDemoHoverState()
        {
            ClientSize = captureBounds.Size;
            hasSel = false;
            mouseInside = true;
            mousePos = new Point(365, 362);
            UpdateHoverWindow(mousePos);
        }

        public void RenderToBitmapForTest(Bitmap target)
        {
            using (Graphics g = Graphics.FromImage(target))
                OnPaint(new PaintEventArgs(g, new Rectangle(0, 0, target.Width, target.Height)));
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Bounds = captureBounds;
            mousePos = PointToClient(Cursor.Position);
            mouseInside = ClientRectangle.Contains(mousePos);
            UpdateHoverWindow(mousePos);
            Invalidate();
        }

        public void ForceForeground()
        {
            IntPtr fg = Native.GetForegroundWindow();
            uint fgThread = fg != IntPtr.Zero ? Native.GetWindowThreadProcessId(fg, IntPtr.Zero) : 0;
            uint me = Native.GetCurrentThreadId();
            if (fgThread != 0 && fgThread != me)
            {
                Native.AttachThreadInput(me, fgThread, true);
                Native.SetForegroundWindow(Handle);
                Native.BringWindowToTop(Handle);
                Native.AttachThreadInput(me, fgThread, false);
            }
            else Native.SetForegroundWindow(Handle);
            Activate();
            Focus();
        }

        // ------------------------------------------------ layout
        int S(float v) { return (int)Math.Round(v * s); }
        static bool Blank(Rectangle r) { return r.Width <= 0 || r.Height <= 0; }
        static Rectangle U(Rectangle a, Rectangle b) { if (Blank(a)) return b; if (Blank(b)) return a; return Rectangle.Union(a, b); }
        static int Clamp(int v, int lo, int hi) { return v < lo ? lo : (v > hi ? hi : v); }

        void UpdateHoverWindow(Point p)
        {
            Rectangle found = Rectangle.Empty;
            foreach (Rectangle r in windows)
                if (r.Contains(p)) { found = r; break; }
            hoverWin = found;
        }

        void ShowToast(string text)
        {
            sizeToast = text;
            sizeToastTicks = 20;
            toastTimer.Start();
            Invalidate();
        }

        void LayoutBars()
        {
            btns.Clear();
            vBar = hBar = Rectangle.Empty;
            if (!hasSel || drag == DragMode.Create) return;
            int bs = S(32), pad = S(5), gap = S(10), sep = S(7);
            int W = ClientSize.Width, H = ClientSize.Height;

            int vTotal = pad * 2;
            for (int i = 0; i < VTools.Length; i++) vTotal += bs + (VTools[i][3] == "sep" ? sep : 0);
            int vw = bs + pad * 2, vh = vTotal;

            int hTotal = pad * 2;
            for (int i = 0; i < HTools.Length; i++)
            {
                bool prim = HTools[i][3] == "primary";
                hTotal += (prim ? S(72) : bs) + (HTools[i][3] == "sep" ? sep : 0);
            }
            int hw = hTotal, hh = bs + pad * 2;

            int vx = sel.Right + gap;
            if (vx + vw > W - S(6)) { vx = sel.Left - gap - vw; if (vx < S(6)) vx = Math.Max(S(6), sel.Right - vw - gap); }
            int vy = Clamp(sel.Bottom - vh, S(6), Math.Max(S(6), H - vh - S(6)));
            vBar = new Rectangle(vx, vy, vw, vh);

            int hy = sel.Bottom + gap;
            if (hy + hh > H - S(6)) { hy = sel.Top - gap - hh; if (hy < S(6)) hy = Math.Max(S(6), sel.Bottom - hh - gap); }
            int hx = Clamp(sel.Right - hw, S(6), Math.Max(S(6), W - hw - S(6)));
            hBar = new Rectangle(hx, hy, hw, hh);
            if (hBar.IntersectsWith(vBar)) { hBar.X = Math.Max(S(6), vBar.Left - hw - gap); }

            int cy = vBar.Y + pad;
            for (int i = 0; i < VTools.Length; i++)
            {
                bool hasSep = VTools[i][3] == "sep";
                if (hasSep) cy += sep;
                btns.Add(new Btn { Id = VTools[i][0], Tip = VTools[i][1], KeyHint = VTools[i][2], Vertical = true, SepBefore = hasSep, R = new Rectangle(vBar.X + pad, cy, bs, bs) });
                cy += bs;
            }
            int cx = hBar.X + pad;
            for (int i = 0; i < HTools.Length; i++)
            {
                bool prim = HTools[i][3] == "primary";
                bool hasSep = HTools[i][3] == "sep";
                if (hasSep) cx += sep;
                int w = prim ? S(72) : bs;
                btns.Add(new Btn { Id = HTools[i][0], Tip = HTools[i][1], KeyHint = HTools[i][2], Vertical = false, Primary = prim, SepBefore = hasSep, R = new Rectangle(cx, hBar.Y + pad, w, bs) });
                cx += w;
            }
        }

        string LabelText() { return sel.Width + " × " + sel.Height; }

        Rectangle LabelRect()
        {
            Size sz = TextRenderer.MeasureText(LabelText(), labelFont);
            int w = sz.Width + S(18), h = sz.Height + S(8);
            int y = sel.Top - h - S(8);
            if (y < S(6)) y = sel.Top + S(8);
            int x = Clamp(sel.Left, S(6), Math.Max(S(6), ClientSize.Width - w - S(6)));
            return new Rectangle(x, y, w, h);
        }

        Rectangle TipRect()
        {
            if (hover == null || drag != DragMode.None) return Rectangle.Empty;
            Size s1 = TextRenderer.MeasureText(hover.Tip, tipFont);
            Size s2 = TextRenderer.MeasureText(hover.KeyHint, keyFont);
            int w = s1.Width + s2.Width + S(26), h = Math.Max(s1.Height, s2.Height) + S(10);
            Rectangle r;
            if (hover.Vertical)
            {
                r = new Rectangle(vBar.Left - w - S(10), hover.R.Top + (hover.R.Height - h) / 2, w, h);
                if (r.Left < S(6)) r.X = vBar.Right + S(10);
            }
            else
            {
                r = new Rectangle(hover.R.Left + (hover.R.Width - w) / 2, hBar.Bottom + S(10), w, h);
                if (r.Bottom > ClientSize.Height - S(6)) r.Y = hBar.Top - h - S(10);
            }
            r.X = Clamp(r.X, S(6), Math.Max(S(6), ClientSize.Width - r.Width - S(6)));
            if (paletteOpen && r.IntersectsWith(PaletteRect())) return Rectangle.Empty;
            return r;
        }

        Btn ColorBtn() { return btns.Find(b => b.Id == "color"); }

        Rectangle PaletteRect()
        {
            Btn cb = ColorBtn();
            if (!paletteOpen || cb == null) return Rectangle.Empty;
            int cell = S(26), pad = S(9), sizeRow = S(32), more = S(28);
            int w = 4 * cell + pad * 2, h = 4 * cell + pad * 2 + sizeRow + more;
            int x = vBar.Left - w - S(10);
            if (x < S(6)) x = vBar.Right + S(10);
            int y = Clamp(cb.R.Bottom - h + S(4), S(6), Math.Max(S(6), ClientSize.Height - h - S(6)));
            Rectangle pr = new Rectangle(x, y, w, h);
            if (!Blank(hBar) && pr.IntersectsWith(hBar))
            {
                int above = hBar.Top - h - S(10);
                if (above >= S(6)) pr.Y = above;
            }
            return pr;
        }

        Rectangle SwatchRect(Rectangle pr, int i)
        {
            int cell = S(26), pad = S(9), inset = S(3);
            return new Rectangle(pr.X + pad + (i % 4) * cell + inset, pr.Y + pad + (i / 4) * cell + inset, cell - inset * 2, cell - inset * 2);
        }

        static readonly float[] SizePresets = { 2f, 4f, 8f, 14f };

        Rectangle SizePresetRect(Rectangle pr, int i)
        {
            int cell = S(26), pad = S(9);
            int y = pr.Y + pad + 4 * cell + S(4);
            return new Rectangle(pr.X + pad + i * cell, y, cell, S(24));
        }

        Rectangle MoreRect(Rectangle pr)
        {
            int cell = S(26), pad = S(9);
            return new Rectangle(pr.X + pad, pr.Y + pad + 4 * cell + S(32), pr.Width - pad * 2, S(24));
        }

        Rectangle Chrome()
        {
            if (!hasSel) return ClientRectangle;
            Rectangle r = U(sel, LabelRect());
            r = U(r, vBar); r = U(r, hBar); r = U(r, PaletteRect()); r = U(r, TipRect());
            int m = S(20) + 8;
            r.Inflate(m, m);
            return r;
        }

        void UpdateChrome()
        {
            LayoutBars();
            if (!hasSel || drag == DragMode.Create) { Invalidate(); return; }
            Rectangle c = Chrome();
            Rectangle inv = U(lastChrome, c);
            if (!Blank(inv)) Invalidate(inv);
            lastChrome = c;
        }

        void InvalidateSel() { Invalidate(Rectangle.Inflate(sel, S(10) + 6, S(10) + 6)); }

        Point[] HandlePts()
        {
            int l = sel.Left, t = sel.Top, r = sel.Right - 1, b = sel.Bottom - 1, cx = (l + r) / 2, cy = (t + b) / 2;
            return new[] { new Point(l, t), new Point(cx, t), new Point(r, t), new Point(r, cy), new Point(r, b), new Point(cx, b), new Point(l, b), new Point(l, cy) };
        }

        int HitHandle(Point p)
        {
            if (!hasSel) return -1;
            int tol = S(8);
            Point[] pts = HandlePts();
            for (int i = 0; i < pts.Length; i++)
                if (Math.Abs(p.X - pts[i].X) <= tol && Math.Abs(p.Y - pts[i].Y) <= tol) return i;
            bool inY = p.Y >= sel.Top && p.Y < sel.Bottom, inX = p.X >= sel.Left && p.X < sel.Right;
            int t2 = Math.Max(3, S(4));
            if (inY && Math.Abs(p.X - sel.Left) <= t2) return 7;
            if (inY && Math.Abs(p.X - (sel.Right - 1)) <= t2) return 3;
            if (inX && Math.Abs(p.Y - sel.Top) <= t2) return 1;
            if (inX && Math.Abs(p.Y - (sel.Bottom - 1)) <= t2) return 5;
            return -1;
        }

        Btn HitBtn(Point p)
        {
            foreach (Btn b in btns) if (b.R.Contains(p)) return b;
            return null;
        }

        Cursor CursorFor(Point p)
        {
            if (HitBtn(p) != null || PaletteRect().Contains(p)) return Cursors.Hand;
            if (!hasSel) return Cursors.Cross;
            int h = HitHandle(p);
            if (h == 0 || h == 4) return Cursors.SizeNWSE;
            if (h == 2 || h == 6) return Cursors.SizeNESW;
            if (h == 1 || h == 5) return Cursors.SizeNS;
            if (h == 3 || h == 7) return Cursors.SizeWE;
            if (sel.Contains(p))
            {
                if (tool == Tool.None) return Cursors.SizeAll;
                if (tool == Tool.Text) return Cursors.IBeam;
            }
            return Cursors.Cross;
        }

        float StrokeWidth(Tool t) { return (t == Tool.Marker ? penWidth * 4.5f : penWidth) * s; }
        float TextSize() { return (15f + penWidth * 2f) * s; }

        static Point ConstrainPoint(Tool t, Point a, Point b)
        {
            if ((ModifierKeys & Keys.Shift) == 0) return b;
            int dx = b.X - a.X, dy = b.Y - a.Y;
            if (t == Tool.Rect || t == Tool.Ellipse)
            {
                int side = Math.Max(Math.Abs(dx), Math.Abs(dy));
                return new Point(a.X + (dx < 0 ? -side : side), a.Y + (dy < 0 ? -side : side));
            }
            if (t == Tool.Line || t == Tool.Arrow)
            {
                double ang = Math.Atan2(dy, dx);
                double snapped = Math.Round(ang / (Math.PI / 4.0)) * (Math.PI / 4.0);
                double len = Math.Sqrt(dx * dx + dy * dy);
                return new Point(a.X + (int)Math.Round(Math.Cos(snapped) * len), a.Y + (int)Math.Round(Math.Sin(snapped) * len));
            }
            return b;
        }

        // ------------------------------------------------ mouse
        protected override void OnMouseDown(MouseEventArgs e)
        {
            mousePos = e.Location;
            if (e.Button == MouseButtons.Right)
            {
                if (paletteOpen) { paletteOpen = false; UpdateChrome(); }
                else if (editing != null) CommitText();
                else if (hasSel) { hasSel = false; shapes.Clear(); redoStack.Clear(); nextStep = 1; tool = Tool.None; UpdateHoverWindow(mousePos); UpdateChrome(); }
                else Close();
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            Point p = e.Location;

            if (paletteOpen)
            {
                Rectangle pr = PaletteRect();
                if (pr.Contains(p))
                {
                    for (int i = 0; i < Palette.Length; i++)
                        if (SwatchRect(pr, i).Contains(p)) { SetColor(Palette[i]); paletteOpen = false; UpdateChrome(); return; }
                    for (int i = 0; i < SizePresets.Length; i++)
                        if (SizePresetRect(pr, i).Contains(p)) { penWidth = SizePresets[i]; ShowToast("Size: " + (int)penWidth + " px"); UpdateChrome(); return; }
                    if (MoreRect(pr).Contains(p)) { paletteOpen = false; UpdateChrome(); PickCustomColor(); }
                    return;
                }
                paletteOpen = false;
                UpdateChrome();
                Btn ob = HitBtn(p);
                if (ob != null && ob.Id == "color") return;
            }

            Btn b = HitBtn(p);
            if (b != null) { OnButton(b.Id); return; }

            CommitText();

            if (!hasSel) { BeginCreate(p); return; }
            int h = HitHandle(p);
            if (h >= 0) { drag = DragMode.Resize; handle = h; dragStart = p; selStart = sel; UpdateChrome(); return; }
            if (sel.Contains(p))
            {
                if (tool == Tool.Text)
                {
                    editing = new Shape { Kind = Tool.Text, Color = color, FontSize = TextSize(), A = p };
                    InvalidateSel();
                    return;
                }
                if (tool == Tool.Step)
                {
                    shapes.Add(new Shape { Kind = Tool.Step, Color = color, Width = penWidth, A = p, StepNumber = nextStep++ });
                    redoStack.Clear();
                    InvalidateSel();
                    return;
                }
                if (tool != Tool.None)
                {
                    cur = new Shape { Kind = tool, Color = color, Width = StrokeWidth(tool), A = p, B = p };
                    cur.Pts.Add(p);
                    drag = DragMode.Draw;
                    UpdateChrome();
                    return;
                }
                drag = DragMode.Move; dragStart = p; selStart = sel;
                UpdateChrome();
                return;
            }
            if (shapes.Count == 0) BeginCreate(p);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && hasSel && tool == Tool.None && sel.Contains(e.Location) && HitBtn(e.Location) == null)
                DoCopy();
        }

        void BeginCreate(Point p)
        {
            hasSel = true;
            drag = DragMode.Create;
            dragStart = p;
            hover = null;
            sel = new Rectangle(p, new Size(1, 1));
            UpdateChrome();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            Point p = e.Location;
            mousePos = p;
            mouseInside = true;
            int W = ClientSize.Width, H = ClientSize.Height;
            switch (drag)
            {
                case DragMode.Create:
                    sel = Rectangle.Intersect(Shape.Norm(dragStart, p), ClientRectangle);
                    Invalidate();
                    break;
                case DragMode.Move:
                {
                    int nx = Clamp(selStart.X + p.X - dragStart.X, 0, Math.Max(0, W - selStart.Width));
                    int ny = Clamp(selStart.Y + p.Y - dragStart.Y, 0, Math.Max(0, H - selStart.Height));
                    sel = new Rectangle(nx, ny, selStart.Width, selStart.Height);
                    UpdateChrome();
                    break;
                }
                case DragMode.Resize:
                {
                    int l = selStart.Left, t = selStart.Top, r = selStart.Right, b = selStart.Bottom;
                    int dx = p.X - dragStart.X, dy = p.Y - dragStart.Y;
                    switch (handle)
                    {
                        case 0: l += dx; t += dy; break;
                        case 1: t += dy; break;
                        case 2: r += dx; t += dy; break;
                        case 3: r += dx; break;
                        case 4: r += dx; b += dy; break;
                        case 5: b += dy; break;
                        case 6: l += dx; b += dy; break;
                        case 7: l += dx; break;
                    }
                    sel = Rectangle.Intersect(Rectangle.FromLTRB(Math.Min(l, r), Math.Min(t, b), Math.Max(l, r), Math.Max(t, b)), ClientRectangle);
                    UpdateChrome();
                    break;
                }
                case DragMode.Draw:
                    if (cur.Kind == Tool.Pen || cur.Kind == Tool.Marker)
                    {
                        Point last = cur.Pts[cur.Pts.Count - 1];
                        int dx = p.X - last.X, dy = p.Y - last.Y;
                        if (dx * dx + dy * dy >= 9) cur.Pts.Add(p); // smooth point spacing
                    }
                    else cur.B = ConstrainPoint(cur.Kind, cur.A, p);
                    InvalidateSel();
                    break;
                default:
                {
                    if (!hasSel)
                    {
                        UpdateHoverWindow(p);
                        Invalidate();
                    }
                    Btn hb = HitBtn(p);
                    if (hb != hover) { hover = hb; UpdateChrome(); }
                    Cursor c = CursorFor(p);
                    if (Cursor != c) Cursor = c;
                    break;
                }
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            DragMode d = drag;
            drag = DragMode.None;
            if (d == DragMode.Create)
            {
                if (sel.Width < 5 || sel.Height < 5)
                {
                    // Single click snaps to hovered window if available!
                    if (!Blank(hoverWin)) sel = hoverWin;
                    else hasSel = false;
                }
            }
            if (d == DragMode.Draw && cur != null)
            {
                if (cur.Kind == Tool.Pen || cur.Kind == Tool.Marker || cur.A != cur.B)
                {
                    shapes.Add(cur);
                    redoStack.Clear();
                }
                cur = null;
            }
            if (d != DragMode.None) { UpdateChrome(); Invalidate(); }
            Cursor = CursorFor(e.Location);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            penWidth = Math.Max(1f, Math.Min(22f, penWidth + (e.Delta > 0 ? 1f : -1f)));
            if (editing != null) { editing.FontSize = TextSize(); InvalidateSel(); }
            ShowToast(tool == Tool.Text ? "Text: " + (int)Math.Round(TextSize()) + " px" : "Size: " + (int)Math.Round(penWidth) + " px");
        }

        // ------------------------------------------------ keyboard
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                if (paletteOpen) { paletteOpen = false; UpdateChrome(); }
                else if (editing != null) CommitText();
                else Close();
                e.Handled = true;
                return;
            }
            if (e.Control)
            {
                switch (e.KeyCode)
                {
                    case Keys.C: DoCopy(); break;
                    case Keys.S: if (e.Shift) DoQuickSave(); else DoSave(); break;
                    case Keys.P: DoPrint(); break;
                    case Keys.T: DoPin(); break;
                    case Keys.Z: if (e.Shift) Redo(); else Undo(); break;
                    case Keys.Y: Redo(); break;
                    case Keys.A: SelectFullScreen(); break;
                    default: base.OnKeyDown(e); return;
                }
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }
            if (editing == null)
            {
                if (!hasSel && e.KeyCode == Keys.C) { CopyHexUnderCursor(); e.Handled = true; return; }
                if (hasSel)
                {
                    switch (e.KeyCode)
                    {
                        case Keys.Enter: DoCopy(); e.Handled = true; return;
                        case Keys.P: SelectTool(Tool.Pen); e.Handled = true; return;
                        case Keys.L: SelectTool(Tool.Line); e.Handled = true; return;
                        case Keys.A: SelectTool(Tool.Arrow); e.Handled = true; return;
                        case Keys.R: SelectTool(Tool.Rect); e.Handled = true; return;
                        case Keys.E: SelectTool(Tool.Ellipse); e.Handled = true; return;
                        case Keys.H: SelectTool(Tool.Marker); e.Handled = true; return;
                        case Keys.B: SelectTool(Tool.Blur); e.Handled = true; return;
                        case Keys.N: SelectTool(Tool.Step); e.Handled = true; return;
                        case Keys.T: SelectTool(Tool.Text); e.Handled = true; return;
                    }
                }
            }
            base.OnKeyDown(e);
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            if (editing == null) return;
            char c = e.KeyChar;
            if (c == '\b') { if (editing.Text.Length > 0) editing.Text = editing.Text.Substring(0, editing.Text.Length - 1); }
            else if (c == '\r') editing.Text += "\n";
            else if (!char.IsControl(c)) editing.Text += c;
            else return;
            e.Handled = true;
            InvalidateSel();
        }

        // ------------------------------------------------ actions
        void OnButton(string id)
        {
            switch (id)
            {
                case "pen": SelectTool(Tool.Pen); break;
                case "line": SelectTool(Tool.Line); break;
                case "arrow": SelectTool(Tool.Arrow); break;
                case "rect": SelectTool(Tool.Rect); break;
                case "ellipse": SelectTool(Tool.Ellipse); break;
                case "marker": SelectTool(Tool.Marker); break;
                case "blur": SelectTool(Tool.Blur); break;
                case "step": SelectTool(Tool.Step); break;
                case "text": SelectTool(Tool.Text); break;
                case "color": paletteOpen = !paletteOpen; UpdateChrome(); break;
                case "undo": Undo(); break;
                case "redo": Redo(); break;
                case "pin": DoPin(); break;
                case "quicksave": DoQuickSave(); break;
                case "save": DoSave(); break;
                case "print": DoPrint(); break;
                case "copy": DoCopy(); break;
                case "close": Close(); break;
            }
        }

        void SelectFullScreen()
        {
            hasSel = true; drag = DragMode.None; sel = ClientRectangle;
            UpdateChrome(); Invalidate();
        }

        void CopyHexUnderCursor()
        {
            int px = Clamp(mousePos.X, 0, shot.Width - 1);
            int py = Clamp(mousePos.Y, 0, shot.Height - 1);
            Color c = shot.GetPixel(px, py);
            string hex = string.Format("#{0:X2}{1:X2}{2:X2}", c.R, c.G, c.B);
            if (UI.TrySetClipboardText(hex) && app != null)
                app.Notify("Color copied", hex + " · RGB(" + c.R + ", " + c.G + ", " + c.B + ")", null);
            Close();
        }

        void SelectTool(Tool t)
        {
            CommitText();
            tool = tool == t ? Tool.None : t;
            UpdateChrome();
        }

        void SetColor(Color c)
        {
            color = c;
            if (editing != null) { editing.Color = c; InvalidateSel(); }
        }

        void PickCustomColor()
        {
            using (var cd = new ColorDialog { Color = color, FullOpen = true, AnyColor = true })
                if (cd.ShowDialog(this) == DialogResult.OK) SetColor(cd.Color);
            UpdateChrome();
        }

        void CommitText()
        {
            if (editing == null) return;
            if (editing.Text.Trim().Length > 0) { shapes.Add(editing); redoStack.Clear(); }
            editing = null;
            InvalidateSel();
        }

        void RecalcStepCounter()
        {
            int maxStep = 0;
            foreach (Shape sh in shapes)
                if (sh.Kind == Tool.Step && sh.StepNumber > maxStep) maxStep = sh.StepNumber;
            nextStep = maxStep + 1;
        }

        void Undo()
        {
            if (editing != null) editing = null;
            else if (shapes.Count > 0)
            {
                Shape last = shapes[shapes.Count - 1];
                shapes.RemoveAt(shapes.Count - 1);
                redoStack.Add(last);
                RecalcStepCounter();
            }
            InvalidateSel();
        }

        void Redo()
        {
            if (redoStack.Count == 0) return;
            Shape sPop = redoStack[redoStack.Count - 1];
            redoStack.RemoveAt(redoStack.Count - 1);
            shapes.Add(sPop);
            RecalcStepCounter();
            InvalidateSel();
        }

        Bitmap Render()
        {
            CommitText();
            var bmp = new Bitmap(sel.Width, sel.Height, PixelFormat.Format24bppRgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.DrawImage(shot, new Rectangle(0, 0, sel.Width, sel.Height), sel, GraphicsUnit.Pixel);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAlias;
                g.TranslateTransform(-sel.X, -sel.Y);
                foreach (Shape sh in shapes) sh.Draw(g, shot, false);
            }
            return bmp;
        }

        bool Ready() { return hasSel && drag == DragMode.None && sel.Width > 0 && sel.Height > 0; }

        void DoCopy()
        {
            if (!Ready()) return;
            using (Bitmap b = Render())
            {
                if (!UI.TrySetClipboardImage(b))
                {
                    MessageBox.Show(this, "Windows clipboard is busy. Please try again.", "QuickShot");
                    return;
                }
            }
            if (app != null) app.Notify("Copied to clipboard", sel.Width + " × " + sel.Height + " px ready to paste", null);
            Close();
        }

        void DoQuickSave()
        {
            if (!Ready()) return;
            using (Bitmap b = Render())
            {
                string path = TrayApp.UniqueFilePath(TrayApp.Folder, "Screenshot");
                try { b.Save(path, ImageFormat.Png); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not save"); return; }
                if (app != null) app.NotifySaved(path);
            }
            Close();
        }

        void DoSave()
        {
            if (!Ready()) return;
            using (Bitmap b = Render())
            using (var dlg = new SaveFileDialog())
            {
                dlg.Title = "Save screenshot";
                dlg.Filter = "PNG image (*.png)|*.png|JPEG image (*.jpg)|*.jpg|Bitmap (*.bmp)|*.bmp";
                dlg.FileName = Path.GetFileName(TrayApp.UniqueFilePath(lastDir ?? TrayApp.Folder, "Screenshot"));
                dlg.InitialDirectory = lastDir ?? TrayApp.Folder;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                lastDir = Path.GetDirectoryName(dlg.FileName);
                string ext = Path.GetExtension(dlg.FileName).ToLowerInvariant();
                ImageFormat f = ext == ".jpg" || ext == ".jpeg" ? ImageFormat.Jpeg : ext == ".bmp" ? ImageFormat.Bmp : ImageFormat.Png;
                try { b.Save(dlg.FileName, f); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not save"); return; }
                if (app != null) app.NotifySaved(dlg.FileName);
            }
            Close();
        }

        void DoPin()
        {
            if (!Ready()) return;
            Bitmap pinned = Render();
            Point screenPos = new Point(captureBounds.Left + sel.Left, captureBounds.Top + sel.Top);
            Close();
            var pin = new PinForm(pinned, screenPos);
            pin.Show();
        }

        void DoPrint()
        {
            if (!Ready()) return;
            bool printed = false;
            using (Bitmap b = Render())
            using (var doc = new PrintDocument())
            using (var pd = new PrintDialog())
            {
                doc.DocumentName = "Screenshot";
                doc.PrintPage += delegate(object o, PrintPageEventArgs pe)
                {
                    Rectangle m = pe.MarginBounds;
                    float k = Math.Min(1f, Math.Min((float)m.Width / b.Width, (float)m.Height / b.Height));
                    pe.Graphics.DrawImage(b, m.Left, m.Top, b.Width * k, b.Height * k);
                };
                pd.Document = doc;
                pd.UseEXDialog = true;
                if (pd.ShowDialog(this) == DialogResult.OK)
                {
                    try { doc.Print(); printed = true; }
                    catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not print"); }
                }
            }
            if (printed) Close();
        }

        // ------------------------------------------------ painting
        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Rectangle clip = e.ClipRectangle;
            g.CompositingMode = CompositingMode.SourceCopy;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.DrawImage(dim, clip, clip, GraphicsUnit.Pixel);

            if (!hasSel || sel.Width < 1 || sel.Height < 1)
            {
                // Smart Window Auto-Snap Highlight!
                if (!Blank(hoverWin))
                {
                    Rectangle hw = Rectangle.Intersect(hoverWin, clip);
                    if (!Blank(hw)) g.DrawImage(shot, hw, hw, GraphicsUnit.Pixel);
                    g.CompositingMode = CompositingMode.SourceOver;
                    using (var p = new Pen(UI.Accent, 2.5f))
                        g.DrawRectangle(p, hoverWin.X + 1, hoverWin.Y + 1, hoverWin.Width - 2, hoverWin.Height - 2);
                }
                else g.CompositingMode = CompositingMode.SourceOver;

                g.SmoothingMode = SmoothingMode.AntiAlias;
                DrawHintBanner(g);
                if (mouseInside) DrawMagnifier(g, mousePos);
                return;
            }

            Rectangle inner = Rectangle.Intersect(sel, clip);
            if (!Blank(inner)) g.DrawImage(shot, inner, inner, GraphicsUnit.Pixel);
            g.CompositingMode = CompositingMode.SourceOver;

            // annotations
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            GraphicsState st = g.Save();
            g.SetClip(sel, CombineMode.Intersect);
            foreach (Shape sh in shapes) sh.Draw(g, shot, false);
            if (cur != null)
            {
                cur.Draw(g, shot, false);
                if (cur.Kind == Tool.Blur)
                {
                    Rectangle br = Shape.Norm(cur.A, cur.B);
                    using (var dp = new Pen(UI.AccentGlow, 1.4f)) { dp.DashStyle = DashStyle.Dash; g.DrawRectangle(dp, br); }
                }
            }
            if (editing != null) editing.Draw(g, shot, true);
            g.Restore(st);

            // high-contrast dual-stroke selection frame
            g.SmoothingMode = SmoothingMode.None;
            using (var outer = new Pen(UI.Accent, 2f)) g.DrawRectangle(outer, sel.X, sel.Y, sel.Width - 1, sel.Height - 1);
            using (var innerP = new Pen(Color.FromArgb(180, 255, 255, 255), 1f))
                if (sel.Width > 3 && sel.Height > 3) g.DrawRectangle(innerP, sel.X + 1, sel.Y + 1, sel.Width - 3, sel.Height - 3);

            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (drag != DragMode.Create && drag != DragMode.Draw)
            {
                int hs = S(9);
                using (var sh = new SolidBrush(Color.FromArgb(80, 0, 0, 0)))
                using (var hb = new SolidBrush(Color.White))
                using (var hp = new Pen(UI.Accent, 1.8f))
                    foreach (Point hpt in HandlePts())
                    {
                        var r = new Rectangle(hpt.X - hs / 2, hpt.Y - hs / 2, hs, hs);
                        g.FillEllipse(sh, r.X + 1, r.Y + 1, r.Width, r.Height);
                        g.FillEllipse(hb, r);
                        g.DrawEllipse(hp, r);
                    }
            }

            // size pill
            Rectangle lr = LabelRect();
            UI.DrawGlassPanel(g, lr, S(6), true);
            TextRenderer.DrawText(g, LabelText(), labelFont, lr, UI.InkLight, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            if (drag == DragMode.Create && mouseInside)
                DrawMagnifier(g, mousePos);

            if (sizeToast != null) DrawSizeToast(g);

            if (btns.Count == 0) return;
            UI.DrawGlassPanel(g, vBar, S(9), true);
            UI.DrawGlassPanel(g, hBar, S(9), true);
            foreach (Btn b in btns) DrawButton(g, b);
            DrawPalette(g);
            DrawTip(g);
        }

        void DrawHintBanner(Graphics g)
        {
            string hint = !Blank(hoverWin)
                ? "Click to snap window (" + hoverWin.Width + " × " + hoverWin.Height + ")  ·  Drag for region  ·  C copy HEX  ·  Esc cancel"
                : "Drag to select region  ·  Ctrl+A full screen  ·  C copy HEX color  ·  Esc cancel";
            Size sz = TextRenderer.MeasureText(hint, tipFont);
            int w = sz.Width + S(28), h = sz.Height + S(14);
            Rectangle r = new Rectangle((ClientSize.Width - w) / 2, S(20), w, h);
            UI.DrawGlassPanel(g, r, S(9), true);
            TextRenderer.DrawText(g, hint, tipFont, r, UI.InkLight, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        void DrawMagnifier(Graphics g, Point p)
        {
            int px = Clamp(p.X, 0, shot.Width - 1);
            int py = Clamp(p.Y, 0, shot.Height - 1);
            int box = S(124), infoH = S(42), gap = S(22);
            int x = p.X + gap, y = p.Y + gap;
            if (x + box > ClientSize.Width - S(8)) x = p.X - gap - box;
            if (y + box + infoH > ClientSize.Height - S(8)) y = p.Y - gap - box - infoH;
            x = Clamp(x, S(8), Math.Max(S(8), ClientSize.Width - box - S(8)));
            y = Clamp(y, S(8), Math.Max(S(8), ClientSize.Height - box - infoH - S(8)));

            Rectangle outer = new Rectangle(x, y, box, box + infoH);
            UI.DrawGlassPanel(g, outer, S(10), true);

            int sample = 11, half = sample / 2;
            Rectangle srcR = new Rectangle(Clamp(px - half, 0, Math.Max(0, shot.Width - sample)), Clamp(py - half, 0, Math.Max(0, shot.Height - sample)), Math.Min(sample, shot.Width), Math.Min(sample, shot.Height));
            Rectangle dstR = new Rectangle(x + S(4), y + S(4), box - S(8), box - S(8));

            InterpolationMode prevI = g.InterpolationMode;
            PixelOffsetMode prevP = g.PixelOffsetMode;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(shot, dstR, srcR, GraphicsUnit.Pixel);
            g.InterpolationMode = prevI;
            g.PixelOffsetMode = prevP;

            // Subtle pixel grid + target center pixel box
            float cellW = (float)dstR.Width / sample, cellH = (float)dstR.Height / sample;
            using (var gp = new Pen(Color.FromArgb(40, 255, 255, 255), 1f))
            {
                for (int i = 1; i < sample; i++)
                {
                    g.DrawLine(gp, dstR.Left + i * cellW, dstR.Top, dstR.Left + i * cellW, dstR.Bottom);
                    g.DrawLine(gp, dstR.Left, dstR.Top + i * cellH, dstR.Right, dstR.Top + i * cellH);
                }
            }
            RectangleF centerCell = new RectangleF(dstR.Left + half * cellW, dstR.Top + half * cellH, cellW, cellH);
            using (var cp = new Pen(UI.Accent, 2f)) g.DrawRectangle(cp, centerCell.X, centerCell.Y, centerCell.Width, centerCell.Height);

            Color col = shot.GetPixel(px, py);
            string hex = string.Format("#{0:X2}{1:X2}{2:X2}", col.R, col.G, col.B);
            Rectangle sw = new Rectangle(x + S(9), y + box + S(9), S(18), S(18));
            using (GraphicsPath sp = UI.RoundRect(sw, S(4)))
            using (var sb = new SolidBrush(col))
            using (var sr = new Pen(Color.White, 1.2f))
            {
                g.FillPath(sb, sp);
                g.DrawPath(sr, sp);
            }
            Rectangle tr = new Rectangle(sw.Right + S(7), y + box + S(3), box - S(36), S(17));
            TextRenderer.DrawText(g, hex, monoFont, tr, UI.InkLight, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            Rectangle cr = new Rectangle(sw.Right + S(7), y + box + S(19), box - S(36), S(16));
            TextRenderer.DrawText(g, px + ", " + py, monoFont, cr, UI.InkMuted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }

        void DrawSizeToast(Graphics g)
        {
            Size sz = TextRenderer.MeasureText(sizeToast, labelFont);
            int dot = Math.Max(S(6), Math.Min(S(16), (int)Math.Round(penWidth * s)));
            int w = sz.Width + dot + S(30), h = sz.Height + S(12);
            Rectangle r = new Rectangle(sel.X + (sel.Width - w) / 2, sel.Y + S(12), w, h);
            r.X = Clamp(r.X, S(8), Math.Max(S(8), ClientSize.Width - w - S(8)));
            r.Y = Clamp(r.Y, S(8), Math.Max(S(8), ClientSize.Height - h - S(8)));
            UI.DrawGlassPanel(g, r, S(7), true);
            using (var br = new SolidBrush(color))
                g.FillEllipse(br, r.X + S(10), r.Y + (r.Height - dot) / 2, dot, dot);
            Rectangle tr = new Rectangle(r.X + dot + S(14), r.Y, r.Width - dot - S(18), r.Height);
            TextRenderer.DrawText(g, sizeToast, labelFont, tr, UI.InkLight, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }

        bool IsActive(string id)
        {
            switch (id)
            {
                case "pen": return tool == Tool.Pen;
                case "line": return tool == Tool.Line;
                case "arrow": return tool == Tool.Arrow;
                case "rect": return tool == Tool.Rect;
                case "ellipse": return tool == Tool.Ellipse;
                case "marker": return tool == Tool.Marker;
                case "blur": return tool == Tool.Blur;
                case "step": return tool == Tool.Step;
                case "text": return tool == Tool.Text;
                case "color": return paletteOpen;
            }
            return false;
        }

        void DrawButton(Graphics g, Btn b)
        {
            Rectangle r = b.R;
            // Subtle divider line before grouped sections
            if (b.SepBefore)
            {
                using (var dp = new Pen(UI.Divider, 1f))
                {
                    if (b.Vertical) g.DrawLine(dp, r.Left + S(5), r.Top - S(4), r.Right - S(5), r.Top - S(4));
                    else g.DrawLine(dp, r.Left - S(4), r.Top + S(5), r.Left - S(4), r.Bottom - S(5));
                }
            }

            Rectangle bgR = Rectangle.Inflate(r, -S(2), -S(2));
            bool active = IsActive(b.Id);
            if (b.Primary)
            {
                Color c1 = hover == b ? UI.AccentGlow : UI.Accent;
                Color c2 = hover == b ? UI.Accent : UI.AccentDeep;
                using (GraphicsPath gp = UI.RoundRect(bgR, S(6)))
                using (var br = new LinearGradientBrush(bgR, c1, c2, 90f))
                    g.FillPath(br, gp);
                TextRenderer.DrawText(g, "Copy", labelFont, bgR, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }
            if (active)
            {
                using (GraphicsPath gp = UI.RoundRect(bgR, S(6)))
                using (var br = new LinearGradientBrush(bgR, UI.Accent, UI.AccentDeep, 90f))
                    g.FillPath(br, gp);
            }
            else if (hover == b)
            {
                using (GraphicsPath gp = UI.RoundRect(bgR, S(6)))
                using (var br = new SolidBrush(UI.BtnHover)) g.FillPath(br, gp);
            }

            Color ink = b.Id == "close" ? UI.Danger : UI.InkLight;
            if ((b.Id == "undo" && shapes.Count == 0 && editing == null) || (b.Id == "redo" && redoStack.Count == 0))
                ink = Color.FromArgb(90, 96, 114);

            Rectangle ir = Rectangle.Inflate(r, -S(8), -S(8));
            float pw = 1.9f * s;
            using (var p = new Pen(ink, pw))
            {
                p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; p.LineJoin = LineJoin.Round;
                switch (b.Id)
                {
                    case "pen":
                    {
                        // Distinct angled pencil with crisp triangular tip & collar
                        PointF[] body = new[]
                        {
                            new PointF(ir.Left + S(2), ir.Bottom - S(5)),
                            new PointF(ir.Right - S(5), ir.Top + S(2)),
                            new PointF(ir.Right - S(1), ir.Top + S(6)),
                            new PointF(ir.Left + S(6), ir.Bottom - S(1))
                        };
                        g.DrawPolygon(p, body);
                        g.DrawLine(p, ir.Left, ir.Bottom, ir.Left + S(2), ir.Bottom - S(5));
                        g.DrawLine(p, ir.Left, ir.Bottom, ir.Left + S(6), ir.Bottom - S(1));
                        break;
                    }
                    case "line":
                        g.DrawLine(p, ir.Left + S(1), ir.Bottom - S(1), ir.Right - S(1), ir.Top + S(1));
                        break;
                    case "arrow":
                        g.DrawLine(p, ir.Left, ir.Bottom, ir.Right, ir.Top);
                        g.DrawLine(p, ir.Right, ir.Top, ir.Right - S(6), ir.Top);
                        g.DrawLine(p, ir.Right, ir.Top, ir.Right, ir.Top + S(6));
                        break;
                    case "rect":
                        g.DrawRectangle(p, ir.X, ir.Y + S(1), ir.Width, ir.Height - S(2));
                        break;
                    case "ellipse":
                        g.DrawEllipse(p, ir.X, ir.Y + S(1), ir.Width, ir.Height - S(2));
                        break;
                    case "marker":
                        using (var hp = new Pen(Color.FromArgb(230, 255, 218, 56), 4.0f * s))
                        {
                            hp.StartCap = LineCap.Square; hp.EndCap = LineCap.Square;
                            g.DrawLine(hp, ir.Left + S(3), ir.Bottom - S(4), ir.Right - S(2), ir.Top + S(2));
                        }
                        using (var ul = new Pen(Color.FromArgb(145, 255, 218, 56), 2.2f * s))
                        {
                            ul.StartCap = LineCap.Round; ul.EndCap = LineCap.Round;
                            g.DrawLine(ul, ir.Left, ir.Bottom, ir.Right, ir.Bottom);
                        }
                        break;
                    case "blur":
                    {
                        int cell = Math.Max(3, ir.Width / 3);
                        for (int gy = 0; gy < 3; gy++)
                            for (int gx = 0; gx < 3; gx++)
                            {
                                int alpha = ((gx + gy) % 2 == 0) ? 240 : 95;
                                using (var br = new SolidBrush(Color.FromArgb(alpha, ink)))
                                    g.FillRectangle(br, ir.X + gx * cell, ir.Y + gy * cell, cell - 1, cell - 1);
                            }
                        break;
                    }
                    case "step":
                        using (var br = new SolidBrush(active ? Color.White : UI.Accent))
                            g.FillEllipse(br, Rectangle.Inflate(ir, S(1), S(1)));
                        using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                        using (var tb = new SolidBrush(active ? UI.AccentDeep : Color.White))
                        using (var nf = new Font("Segoe UI", 11 * s, FontStyle.Bold, GraphicsUnit.Pixel))
                            g.DrawString("1", nf, tb, r, sf);
                        break;
                    case "text":
                        using (var br = new SolidBrush(ink))
                        using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                            g.DrawString("T", letterFont, br, r, sf);
                        break;
                    case "color":
                    {
                        Rectangle cr = Rectangle.Inflate(r, -S(8), -S(8));
                        using (var br = new SolidBrush(color)) g.FillEllipse(br, cr);
                        using (var cp = new Pen(Color.White, 1.7f * s)) g.DrawEllipse(cp, cr);
                        break;
                    }
                    case "undo":
                        g.DrawArc(p, ir.X, ir.Y + S(2), ir.Width, ir.Height - S(2), 200, 175);
                        g.DrawLine(p, ir.X + S(1), ir.Y + S(6), ir.X + S(5), ir.Y + S(2));
                        g.DrawLine(p, ir.X + S(1), ir.Y + S(6), ir.X + S(6), ir.Y + S(7));
                        break;
                    case "redo":
                        g.DrawArc(p, ir.X, ir.Y + S(2), ir.Width, ir.Height - S(2), -15, -175);
                        g.DrawLine(p, ir.Right - S(1), ir.Y + S(6), ir.Right - S(5), ir.Y + S(2));
                        g.DrawLine(p, ir.Right - S(1), ir.Y + S(6), ir.Right - S(6), ir.Y + S(7));
                        break;
                    case "pin":
                        g.DrawEllipse(p, ir.X + S(3), ir.Y, ir.Width - S(6), ir.Height / 2);
                        g.DrawLine(p, ir.X + ir.Width / 2, ir.Y + ir.Height / 2, ir.X + ir.Width / 2, ir.Bottom);
                        break;
                    case "quicksave":
                        g.DrawLine(p, ir.X + ir.Width / 2, ir.Top, ir.X + ir.Width / 2, ir.Bottom - S(3));
                        g.DrawLine(p, ir.X + S(3), ir.Bottom - S(6), ir.X + ir.Width / 2, ir.Bottom - S(2));
                        g.DrawLine(p, ir.Right - S(3), ir.Bottom - S(6), ir.X + ir.Width / 2, ir.Bottom - S(2));
                        g.DrawLine(p, ir.Left, ir.Bottom, ir.Right, ir.Bottom);
                        break;
                    case "save":
                        g.DrawRectangle(p, ir.X, ir.Y, ir.Width, ir.Height);
                        g.DrawRectangle(p, ir.X + S(3), ir.Y, ir.Width - S(6), S(5));
                        g.DrawRectangle(p, ir.X + S(3), ir.Bottom - S(5), ir.Width - S(6), S(5));
                        break;
                    case "print":
                        g.DrawRectangle(p, ir.X, ir.Y + S(4), ir.Width, ir.Height - S(7));
                        g.DrawRectangle(p, ir.X + S(3), ir.Y, ir.Width - S(6), S(4));
                        g.DrawRectangle(p, ir.X + S(3), ir.Bottom - S(4), ir.Width - S(6), S(4));
                        break;
                    case "close":
                        g.DrawLine(p, ir.Left + S(1), ir.Top + S(1), ir.Right - S(1), ir.Bottom - S(1));
                        g.DrawLine(p, ir.Right - S(1), ir.Top + S(1), ir.Left + S(1), ir.Bottom - S(1));
                        break;
                }
            }
        }

        void DrawPalette(Graphics g)
        {
            Rectangle pr = PaletteRect();
            if (Blank(pr)) return;
            UI.DrawGlassPanel(g, pr, S(9), true);
            for (int i = 0; i < Palette.Length; i++)
            {
                Rectangle sr = SwatchRect(pr, i);
                using (GraphicsPath sp = UI.RoundRect(sr, S(5)))
                using (var br = new SolidBrush(Palette[i])) g.FillPath(br, sp);
                bool chosen = Palette[i].ToArgb() == color.ToArgb();
                using (GraphicsPath sp = UI.RoundRect(sr, S(5)))
                using (var p = new Pen(chosen ? Color.White : Color.FromArgb(75, 255, 255, 255), chosen ? 2.2f * s : 1f))
                    g.DrawPath(p, sp);
            }
            // Inline Stroke Size Preset Dots (S / M / L / XL)
            for (int i = 0; i < SizePresets.Length; i++)
            {
                Rectangle sr = SizePresetRect(pr, i);
                bool active = Math.Abs(penWidth - SizePresets[i]) < 1.1f;
                if (active)
                {
                    using (GraphicsPath gp = UI.RoundRect(Rectangle.Inflate(sr, -1, -1), S(5)))
                    using (var br = new SolidBrush(UI.BtnHover)) g.FillPath(br, gp);
                }
                int d = S(5 + i * 3);
                using (var br = new SolidBrush(active ? UI.AccentGlow : UI.InkLight))
                    g.FillEllipse(br, sr.X + (sr.Width - d) / 2, sr.Y + (sr.Height - d) / 2, d, d);
            }
            Rectangle mr = MoreRect(pr);
            using (var sep = new Pen(UI.Divider)) g.DrawLine(sep, mr.Left, mr.Top - S(2), mr.Right, mr.Top - S(2));
            TextRenderer.DrawText(g, "Custom color...", tipFont, mr, UI.InkMuted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        void DrawTip(Graphics g)
        {
            Rectangle tr = TipRect();
            if (Blank(tr)) return;
            UI.DrawGlassPanel(g, tr, S(6), true);
            Size s1 = TextRenderer.MeasureText(hover.Tip, tipFont);
            Rectangle r1 = new Rectangle(tr.X + S(8), tr.Y, s1.Width + S(2), tr.Height);
            TextRenderer.DrawText(g, hover.Tip, tipFont, r1, UI.InkLight, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            Rectangle r2 = new Rectangle(r1.Right + S(4), tr.Y + S(3), tr.Right - r1.Right - S(10), tr.Height - S(6));
            using (GraphicsPath kp = UI.RoundRect(r2, S(4)))
            using (var kb = new SolidBrush(Color.FromArgb(50, 138, 99, 255)))
                g.FillPath(kb, kp);
            TextRenderer.DrawText(g, hover.KeyHint, keyFont, r2, UI.AccentGlow, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            if (toastTimer != null) { toastTimer.Stop(); toastTimer.Dispose(); toastTimer = null; }
            if (shot != null) shot.Dispose();
            if (dim != null) dim.Dispose();
            if (tipFont != null) tipFont.Dispose();
            if (keyFont != null) keyFont.Dispose();
            if (labelFont != null) labelFont.Dispose();
            if (letterFont != null) letterFont.Dispose();
            if (monoFont != null) monoFont.Dispose();
        }
    }
}
