using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("QuickShot Pro Setup")]
[assembly: AssemblyProduct("QuickShot Pro")]
[assembly: AssemblyCompany("Ahmad Akkash")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace QuickShotSetup
{
    static class Native
    {
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    }

    static class Program
    {
        public const string AppName = "QuickShot";
        public const string DisplayName = "QuickShot Pro";
        public const string Version = "1.0.0";
        public const string Publisher = "Ahmad Akkash";
        public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\QuickShot";

        public static string DefaultInstallDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppName); }
        }

        [STAThread]
        static int Main(string[] args)
        {
            bool dpiOk = false;
            try { dpiOk = Native.SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch (EntryPointNotFoundException) { }
            if (!dpiOk) { try { Native.SetProcessDPIAware(); } catch { } }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool silent = HasArg(args, "--silent");
            bool uninstall = HasArg(args, "--uninstall");
            string customDir = GetArgValue(args, "--dir");
            string installDir = string.IsNullOrEmpty(customDir) ? DefaultInstallDir : customDir;

            string demoOut = GetArgValue(args, "--demo-render-setup");
            if (!string.IsNullOrEmpty(demoOut))
                return RenderDemo(demoOut);

            if (HasArg(args, "--self-test"))
                return RunSelfTest();

            if (silent)
            {
                try
                {
                    if (uninstall)
                    {
                        InstallerCore.Uninstall(installDir, false);
                        return 0;
                    }
                    InstallerCore.Install(installDir, true, true, true, false);
                    return 0;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex.ToString());
                    return 1;
                }
            }

            Application.Run(new SetupForm(installDir, uninstall));
            return 0;
        }

        static bool HasArg(string[] args, string name)
        {
            foreach (string a in args)
                if (string.Equals(a, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        static string GetArgValue(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return null;
        }

        static int RunSelfTest()
        {
            string tmp = Path.Combine(Path.GetTempPath(), "QuickShotSetupTest_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                Directory.CreateDirectory(tmp);
                string exeOut = Path.Combine(tmp, "QuickShot.exe");
                string icoOut = Path.Combine(tmp, "QuickShot.ico");
                InstallerCore.ExtractResourcePublic("QuickShot.exe", exeOut);
                InstallerCore.ExtractResourcePublic("QuickShot.ico", icoOut);
                if (!File.Exists(exeOut) || new FileInfo(exeOut).Length < 20000) return 2;
                if (!File.Exists(icoOut) || new FileInfo(icoOut).Length < 1000) return 3;
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.ToString());
                return 1;
            }
            finally
            {
                try { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch { }
            }
        }

        static int RenderDemo(string outPath)
        {
            try
            {
                using (var f = new SetupForm(DefaultInstallDir, false))
                using (var bmp = new Bitmap(f.ClientSize.Width, f.ClientSize.Height, PixelFormat.Format32bppRgb))
                {
                    f.SetDemoModeForTest();
                    f.RenderToBitmapForTest(bmp);
                    bmp.Save(outPath, ImageFormat.Png);
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

    static class InstallerCore
    {
        public static bool IsInstalled(string dir)
        {
            return File.Exists(Path.Combine(dir, "QuickShot.exe"));
        }

        public static void StopRunningQuickShot()
        {
            try
            {
                Process[] procs = Process.GetProcessesByName("QuickShot");
                foreach (Process p in procs)
                {
                    try { p.Kill(); p.WaitForExit(1500); } catch { }
                    finally { p.Dispose(); }
                }
            }
            catch { }
            Thread.Sleep(250);
        }

        public static void ExtractResourcePublic(string resName, string destPath) { ExtractResource(resName, destPath); }

        static void ExtractResource(string resName, string destPath)
        {
            Assembly asm = Assembly.GetExecutingAssembly();
            using (Stream s = asm.GetManifestResourceStream(resName))
            {
                if (s == null) throw new InvalidOperationException("Missing embedded payload: " + resName);
                using (FileStream fs = File.Create(destPath))
                {
                    byte[] buf = new byte[16384];
                    int read;
                    while ((read = s.Read(buf, 0, buf.Length)) > 0) fs.Write(buf, 0, read);
                }
            }
        }

        static void CreateShortcut(string shortcutPath, string targetPath, string iconPath, string description)
        {
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return;
            object shell = Activator.CreateInstance(shellType);
            try
            {
                object sc = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath });
                Type scType = sc.GetType();
                scType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc, new object[] { targetPath });
                scType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc, new object[] { Path.GetDirectoryName(targetPath) });
                scType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, sc, new object[] { iconPath + ",0" });
                scType.InvokeMember("Description", BindingFlags.SetProperty, null, sc, new object[] { description });
                scType.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
                Marshal.FinalReleaseComObject(sc);
            }
            finally
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }

        public static string DesktopShortcutPath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Program.AppName + ".lnk");
        }

        public static string StartMenuShortcutPath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), Program.AppName + ".lnk");
        }

        public static void Install(string installDir, bool startWithWindows, bool desktopShortcut, bool startMenuShortcut, bool launchAfter)
        {
            StopRunningQuickShot();
            Directory.CreateDirectory(installDir);

            string exePath = Path.Combine(installDir, "QuickShot.exe");
            string icoPath = Path.Combine(installDir, "QuickShot.ico");
            string uninstPath = Path.Combine(installDir, "QuickShot-Uninstall.exe");

            ExtractResource("QuickShot.exe", exePath);
            ExtractResource("QuickShot.ico", icoPath);

            // Copy setup executable as the registered uninstaller so Windows Settings -> Apps can cleanly uninstall later
            string selfExe = Assembly.GetExecutingAssembly().Location;
            if (!string.Equals(Path.GetFullPath(selfExe), Path.GetFullPath(uninstPath), StringComparison.OrdinalIgnoreCase))
            {
                try { File.Copy(selfExe, uninstPath, true); } catch { }
            }

            // Startup registration
            using (RegistryKey rk = Registry.CurrentUser.CreateSubKey(Program.RunKey))
            {
                if (rk != null)
                {
                    if (startWithWindows) rk.SetValue(Program.AppName, "\"" + exePath + "\" --silent");
                    else rk.DeleteValue(Program.AppName, false);
                }
            }

            // Shortcuts
            if (desktopShortcut)
                CreateShortcut(DesktopShortcutPath(), exePath, icoPath, "QuickShot Pro — Fast screenshot & annotation studio");
            if (startMenuShortcut)
                CreateShortcut(StartMenuShortcutPath(), exePath, icoPath, "QuickShot Pro — Fast screenshot & annotation studio");

            // Register in Windows Installed Apps (per-user, no UAC needed)
            int sizeKb = 150;
            try
            {
                long bytes = new FileInfo(exePath).Length + new FileInfo(icoPath).Length;
                if (File.Exists(uninstPath)) bytes += new FileInfo(uninstPath).Length;
                sizeKb = Math.Max(100, (int)(bytes / 1024));
            }
            catch { }

            using (RegistryKey uk = Registry.CurrentUser.CreateSubKey(Program.UninstallKey))
            {
                if (uk != null)
                {
                    uk.SetValue("DisplayName", Program.DisplayName);
                    uk.SetValue("DisplayVersion", Program.Version);
                    uk.SetValue("Publisher", Program.Publisher);
                    uk.SetValue("DisplayIcon", icoPath);
                    uk.SetValue("InstallLocation", installDir);
                    uk.SetValue("UninstallString", "\"" + uninstPath + "\" --uninstall");
                    uk.SetValue("QuietUninstallString", "\"" + uninstPath + "\" --uninstall --silent");
                    uk.SetValue("NoModify", 1, RegistryValueKind.DWord);
                    uk.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                    uk.SetValue("EstimatedSize", sizeKb, RegistryValueKind.DWord);
                }
            }

            if (launchAfter)
            {
                Process.Start(new ProcessStartInfo { FileName = exePath, UseShellExecute = true });
            }
        }

        public static void Uninstall(string installDir, bool scheduleSelfCleanup)
        {
            StopRunningQuickShot();

            try { using (RegistryKey rk = Registry.CurrentUser.OpenSubKey(Program.RunKey, true)) if (rk != null) rk.DeleteValue(Program.AppName, false); } catch { }
            try { Registry.CurrentUser.DeleteSubKeyTree(Program.UninstallKey, false); } catch { }
            try { string d = DesktopShortcutPath(); if (File.Exists(d)) File.Delete(d); } catch { }
            try { string s = StartMenuShortcutPath(); if (File.Exists(s)) File.Delete(s); } catch { }

            string exePath = Path.Combine(installDir, "QuickShot.exe");
            string icoPath = Path.Combine(installDir, "QuickShot.ico");
            try { if (File.Exists(exePath)) File.Delete(exePath); } catch { }
            try { if (File.Exists(icoPath)) File.Delete(icoPath); } catch { }

            string selfExe = Path.GetFullPath(Assembly.GetExecutingAssembly().Location);
            string fullDir = Path.GetFullPath(installDir);
            bool runningInsideInstallDir = selfExe.StartsWith(fullDir, StringComparison.OrdinalIgnoreCase);

            if (!runningInsideInstallDir)
            {
                try { if (Directory.Exists(fullDir)) Directory.Delete(fullDir, true); } catch { }
            }
            else if (scheduleSelfCleanup)
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = "/c ping 127.0.0.1 -n 2 >nul & rmdir /s /q \"" + fullDir + "\"",
                        WindowStyle = ProcessWindowStyle.Hidden,
                        CreateNoWindow = true,
                        UseShellExecute = false
                    });
                }
                catch { }
            }
        }
    }

    class SetupForm : Form
    {
        static readonly Color Accent = Color.FromArgb(138, 99, 255);
        static readonly Color AccentDeep = Color.FromArgb(98, 62, 224);
        static readonly Color AccentGlow = Color.FromArgb(170, 140, 255);
        static readonly Color Success = Color.FromArgb(52, 211, 153);
        static readonly Color Danger = Color.FromArgb(248, 113, 113);
        static readonly Color BgTop = Color.FromArgb(26, 29, 40);
        static readonly Color BgBot = Color.FromArgb(17, 19, 27);
        static readonly Color CardBg = Color.FromArgb(34, 38, 52);
        static readonly Color BorderCol = Color.FromArgb(58, 64, 84);
        static readonly Color InkLight = Color.FromArgb(244, 246, 252);
        static readonly Color InkMuted = Color.FromArgb(156, 164, 184);

        enum ViewState { Ready, UninstallConfirm, DoneInstall, DoneUninstall }

        string installDir;
        ViewState state;
        bool alreadyInstalled;

        bool optStartup = true;
        bool optDesktop = true;
        bool optStartMenu = true;
        bool optLaunch = true;

        bool dragging;
        Point dragOffset;
        string hoverId;
        string errorMsg;

        public SetupForm(string dir, bool uninstallMode)
        {
            installDir = dir;
            alreadyInstalled = InstallerCore.IsInstalled(installDir);
            state = uninstallMode ? ViewState.UninstallConfirm : ViewState.Ready;

            Text = "QuickShot Pro Setup";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(500, 456);
            BackColor = BgBot;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            KeyPreview = true;
            KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Close(); };

            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                using (Stream st = asm.GetManifestResourceStream("QuickShot.ico"))
                    if (st != null) Icon = new Icon(st);
            }
            catch { }
        }

        public void SetDemoModeForTest()
        {
            alreadyInstalled = false;
            state = ViewState.Ready;
            hoverId = "primary";
        }

        public void RenderToBitmapForTest(Bitmap bmp)
        {
            using (Graphics g = Graphics.FromImage(bmp))
                OnPaint(new PaintEventArgs(g, new Rectangle(0, 0, bmp.Width, bmp.Height)));
        }

        static GraphicsPath RoundRect(RectangleF r, float rad)
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

        Rectangle CloseBtnRect() { return new Rectangle(ClientSize.Width - 40, 12, 28, 28); }
        Rectangle CheckRect(int idx) { return new Rectangle(28, 206 + idx * 36, ClientSize.Width - 56, 30); }
        Rectangle PrimaryBtnRect()
        {
            if (state == ViewState.Ready && alreadyInstalled)
                return new Rectangle(28, ClientSize.Height - 66, 294, 44);
            return new Rectangle(28, ClientSize.Height - 66, ClientSize.Width - 56, 44);
        }
        Rectangle SecondaryBtnRect()
        {
            if (state == ViewState.Ready && alreadyInstalled)
                return new Rectangle(334, ClientSize.Height - 66, 138, 44);
            return Rectangle.Empty;
        }

        string HitTest(Point p)
        {
            if (CloseBtnRect().Contains(p)) return "close";
            if (PrimaryBtnRect().Contains(p)) return "primary";
            if (!SecondaryBtnRect().IsEmpty && SecondaryBtnRect().Contains(p)) return "secondary";
            if (state == ViewState.Ready)
            {
                for (int i = 0; i < 4; i++)
                    if (CheckRect(i).Contains(p)) return "chk" + i;
            }
            return null;
        }

        void DrawLogo(Graphics g, int x, int y, int sz)
        {
            RectangleF r = new RectangleF(x, y, sz, sz);
            using (GraphicsPath gp = RoundRect(r, sz * 0.24f))
            using (var br = new LinearGradientBrush(r, Color.FromArgb(164, 108, 255), Color.FromArgb(88, 46, 198), 45f))
                g.FillPath(br, gp);
            float u = sz / 32f;
            using (var p = new Pen(Color.White, 2.6f * u))
            {
                p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; p.LineJoin = LineJoin.Round;
                g.DrawLines(p, new[] { new PointF(x + 7 * u, y + 13 * u), new PointF(x + 7 * u, y + 7 * u), new PointF(x + 13 * u, y + 7 * u) });
                g.DrawLines(p, new[] { new PointF(x + 19 * u, y + 25 * u), new PointF(x + 25 * u, y + 25 * u), new PointF(x + 25 * u, y + 19 * u) });
            }
            using (var p = new Pen(Color.White, 2.2f * u))
            {
                p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                g.DrawLine(p, x + 11 * u, y + 21 * u, x + 21 * u, y + 11 * u);
                g.DrawLine(p, x + 21 * u, y + 11 * u, x + 16 * u, y + 11 * u);
                g.DrawLine(p, x + 21 * u, y + 11 * u, x + 21 * u, y + 16 * u);
            }
        }

        void DrawCheckbox(Graphics g, Rectangle r, bool isChecked, string label, bool isHover)
        {
            if (isHover)
            {
                using (GraphicsPath hp = RoundRect(r, 7))
                using (var hb = new SolidBrush(Color.FromArgb(35, 255, 255, 255)))
                    g.FillPath(hb, hp);
            }
            Rectangle box = new Rectangle(r.X + 6, r.Y + 6, 18, 18);
            using (GraphicsPath bp = RoundRect(box, 5))
            {
                if (isChecked)
                {
                    using (var br = new LinearGradientBrush(box, Accent, AccentDeep, 90f)) g.FillPath(br, bp);
                    using (var cp = new Pen(Color.White, 2.1f))
                    {
                        cp.StartCap = LineCap.Round; cp.EndCap = LineCap.Round; cp.LineJoin = LineJoin.Round;
                        g.DrawLines(cp, new[] { new Point(box.X + 4, box.Y + 9), new Point(box.X + 8, box.Y + 13), new Point(box.X + 14, box.Y + 5) });
                    }
                }
                else
                {
                    using (var br = new SolidBrush(CardBg)) g.FillPath(br, bp);
                    using (var pn = new Pen(BorderCol, 1.3f)) g.DrawPath(pn, bp);
                }
            }
            using (var f = new Font("Segoe UI", 13f, FontStyle.Regular, GraphicsUnit.Pixel))
                TextRenderer.DrawText(g, label, f, new Rectangle(box.Right + 10, r.Y, r.Width - 36, r.Height), InkLight, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            Rectangle full = new Rectangle(0, 0, ClientSize.Width, ClientSize.Height);
            using (var bg = new LinearGradientBrush(full, BgTop, BgBot, 90f))
                g.FillRectangle(bg, full);

            // Top luminous purple accent stripe
            using (var ab = new LinearGradientBrush(new Rectangle(0, 0, ClientSize.Width, 4), AccentGlow, AccentDeep, 0f))
                g.FillRectangle(ab, 0, 0, ClientSize.Width, 4);

            // Subtle outer border
            using (var bp = new Pen(BorderCol, 1f))
                g.DrawRectangle(bp, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);

            // Close button
            Rectangle cb = CloseBtnRect();
            if (hoverId == "close")
            {
                using (GraphicsPath gp = RoundRect(cb, 6))
                using (var br = new SolidBrush(Color.FromArgb(80, 248, 113, 113))) g.FillPath(br, gp);
            }
            using (var xf = new Font("Segoe UI", 15f, FontStyle.Bold, GraphicsUnit.Pixel))
                TextRenderer.DrawText(g, "×", xf, cb, hoverId == "close" ? Danger : InkMuted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            // Brand header
            DrawLogo(g, 28, 28, 48);
            using (var tf = new Font("Segoe UI", 22f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var bf = new Font("Segoe UI", 12.5f, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var pf = new Font("Consolas", 11f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var wb = new SolidBrush(InkLight))
            using (var mb = new SolidBrush(InkMuted))
            {
                g.DrawString("QuickShot Pro", tf, wb, 90, 26);
                Rectangle verPill = new Rectangle(248, 30, 56, 22);
                using (GraphicsPath vp = RoundRect(verPill, 6))
                using (var vb = new SolidBrush(Color.FromArgb(55, 138, 99, 255)))
                    g.FillPath(vb, vp);
                TextRenderer.DrawText(g, "v" + Program.Version, pf, verPill, AccentGlow, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                g.DrawString("Screenshot, annotation & desktop pin studio for Windows", bf, mb, 90, 56);
            }

            using (var sep = new Pen(Color.FromArgb(65, 130, 140, 175)))
                g.DrawLine(sep, 28, 94, ClientSize.Width - 28, 94);

            if (state == ViewState.Ready)
            {
                // Install folder info card
                Rectangle card = new Rectangle(28, 112, ClientSize.Width - 56, 76);
                using (GraphicsPath cp = RoundRect(card, 10))
                using (var cb2 = new SolidBrush(CardBg))
                using (var cpen = new Pen(BorderCol, 1f))
                {
                    g.FillPath(cb2, cp);
                    g.DrawPath(cpen, cp);
                }
                using (var hf = new Font("Segoe UI", 11.5f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var mf = new Font("Consolas", 11.5f, FontStyle.Regular, GraphicsUnit.Pixel))
                using (var sf = new Font("Segoe UI", 11.5f, FontStyle.Regular, GraphicsUnit.Pixel))
                using (var ab = new SolidBrush(AccentGlow))
                using (var wb = new SolidBrush(InkLight))
                using (var mb = new SolidBrush(InkMuted))
                {
                    g.DrawString(alreadyInstalled ? "INSTALLED LOCATION (UPDATE / REPAIR)" : "INSTALL LOCATION", hf, ab, card.X + 14, card.Y + 11);
                    g.DrawString(installDir, mf, wb, card.X + 14, card.Y + 32);
                    g.DrawString("Size: ~110 KB  ·  Zero dependencies  ·  No admin required", sf, mb, card.X + 14, card.Y + 52);
                }

                DrawCheckbox(g, CheckRect(0), optStartup, "Launch QuickShot automatically when Windows starts", hoverId == "chk0");
                DrawCheckbox(g, CheckRect(1), optDesktop, "Create a Desktop shortcut", hoverId == "chk1");
                DrawCheckbox(g, CheckRect(2), optStartMenu, "Add to Windows Start Menu", hoverId == "chk2");
                DrawCheckbox(g, CheckRect(3), optLaunch, "Launch QuickShot right after installing", hoverId == "chk3");

                if (!string.IsNullOrEmpty(errorMsg))
                {
                    using (var ef = new Font("Segoe UI", 12f, FontStyle.Regular, GraphicsUnit.Pixel))
                    using (var eb = new SolidBrush(Danger))
                        g.DrawString(errorMsg, ef, eb, 28, 358);
                }

            // Primary CTA button (anti-aliased text — zero GDI fringe on purple gradient)
            using (var csf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            using (var whiteBr = new SolidBrush(Color.White))
            {
                Rectangle pb = PrimaryBtnRect();
                Color c1 = hoverId == "primary" ? AccentGlow : Accent;
                Color c2 = hoverId == "primary" ? Accent : AccentDeep;
                using (GraphicsPath gp = RoundRect(pb, 9))
                using (var br = new LinearGradientBrush(pb, c1, c2, 90f))
                    g.FillPath(br, gp);
                g.TextRenderingHint = TextRenderingHint.AntiAlias;
                using (var bf = new Font("Segoe UI", 14f, FontStyle.Bold, GraphicsUnit.Pixel))
                    g.DrawString(alreadyInstalled ? "Update QuickShot Pro" : "Install QuickShot Pro", bf, whiteBr, pb, csf);
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            }

                Rectangle sb = SecondaryBtnRect();
                if (!sb.IsEmpty)
                {
                    using (GraphicsPath gp = RoundRect(sb, 9))
                    using (var br = new SolidBrush(hoverId == "secondary" ? Color.FromArgb(65, 248, 113, 113) : CardBg))
                    using (var pn = new Pen(hoverId == "secondary" ? Danger : BorderCol, 1.2f))
                    {
                        g.FillPath(br, gp);
                        g.DrawPath(pn, gp);
                    }
                    using (var bf = new Font("Segoe UI", 13.5f, FontStyle.Bold, GraphicsUnit.Pixel))
                        TextRenderer.DrawText(g, "Uninstall", bf, sb, Danger, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
            }
            else if (state == ViewState.UninstallConfirm)
            {
                using (var hf = new Font("Segoe UI", 17f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var bf = new Font("Segoe UI", 13f, FontStyle.Regular, GraphicsUnit.Pixel))
                using (var wb = new SolidBrush(InkLight))
                using (var mb = new SolidBrush(InkMuted))
                {
                    g.DrawString("Uninstall QuickShot Pro?", hf, wb, 28, 128);
                    g.DrawString("This removes QuickShot, its shortcuts, and startup entry.\nYour saved screenshots in Pictures\\QuickShot will NOT be deleted.", bf, mb, new RectangleF(28, 166, ClientSize.Width - 56, 80));
                }
                Rectangle pb = PrimaryBtnRect();
                using (GraphicsPath gp = RoundRect(pb, 9))
                using (var br = new SolidBrush(hoverId == "primary" ? Color.FromArgb(255, 130, 130) : Color.FromArgb(220, 68, 68)))
                    g.FillPath(br, gp);
                using (var bf = new Font("Segoe UI", 14f, FontStyle.Bold, GraphicsUnit.Pixel))
                    TextRenderer.DrawText(g, "Uninstall QuickShot Pro", bf, pb, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            else
            {
                bool installedOk = state == ViewState.DoneInstall;
                int cx = ClientSize.Width / 2;
                using (var cb3 = new SolidBrush(Color.FromArgb(40, 52, 211, 153)))
                    g.FillEllipse(cb3, cx - 34, 124, 68, 68);
                using (var cp = new Pen(Success, 3.2f))
                {
                    cp.StartCap = LineCap.Round; cp.EndCap = LineCap.Round; cp.LineJoin = LineJoin.Round;
                    g.DrawLines(cp, new[] { new Point(cx - 14, 158), new Point(cx - 3, 169), new Point(cx + 16, 147) });
                }
                using (var hf = new Font("Segoe UI", 19f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var bf = new Font("Segoe UI", 13f, FontStyle.Regular, GraphicsUnit.Pixel))
                {
                    TextRenderer.DrawText(g, installedOk ? "QuickShot Pro is ready!" : "QuickShot Pro uninstalled", hf, new Rectangle(28, 206, ClientSize.Width - 56, 30), InkLight, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    string sub = installedOk
                        ? "Press PrtSc anytime to capture, annotate, blur secrets, or pin.\nLook for the purple icon in your Windows system tray."
                        : "QuickShot and its shortcuts were cleanly removed.\nYour saved screenshots in Pictures\\QuickShot are untouched.";
                    TextRenderer.DrawText(g, sub, bf, new Rectangle(28, 246, ClientSize.Width - 56, 60), InkMuted, TextFormatFlags.HorizontalCenter);
                }

                Rectangle pb = PrimaryBtnRect();
                using (GraphicsPath gp = RoundRect(pb, 9))
                using (var br = new LinearGradientBrush(pb, hoverId == "primary" ? AccentGlow : Accent, hoverId == "primary" ? Accent : AccentDeep, 90f))
                    g.FillPath(br, gp);
                using (var bf = new Font("Segoe UI", 14f, FontStyle.Bold, GraphicsUnit.Pixel))
                    TextRenderer.DrawText(g, "Finish", bf, pb, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            string hit = HitTest(e.Location);
            if (hit == null) { dragging = true; dragOffset = e.Location; return; }

            if (hit == "close") { Close(); return; }
            if (hit == "chk0") { optStartup = !optStartup; Invalidate(); return; }
            if (hit == "chk1") { optDesktop = !optDesktop; Invalidate(); return; }
            if (hit == "chk2") { optStartMenu = !optStartMenu; Invalidate(); return; }
            if (hit == "chk3") { optLaunch = !optLaunch; Invalidate(); return; }
            if (hit == "secondary") { state = ViewState.UninstallConfirm; Invalidate(); return; }
            if (hit == "primary")
            {
                if (state == ViewState.Ready)
                {
                    try
                    {
                        errorMsg = null;
                        InstallerCore.Install(installDir, optStartup, optDesktop, optStartMenu, optLaunch);
                        state = ViewState.DoneInstall;
                        Invalidate();
                    }
                    catch (Exception ex) { errorMsg = "Install failed: " + ex.Message; Invalidate(); }
                }
                else if (state == ViewState.UninstallConfirm)
                {
                    try
                    {
                        InstallerCore.Uninstall(installDir, true);
                        state = ViewState.DoneUninstall;
                        Invalidate();
                    }
                    catch (Exception ex) { errorMsg = "Uninstall failed: " + ex.Message; Invalidate(); }
                }
                else Close();
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (dragging) { Location = new Point(Location.X + e.X - dragOffset.X, Location.Y + e.Y - dragOffset.Y); return; }
            string hit = HitTest(e.Location);
            if (hit != hoverId) { hoverId = hit; Invalidate(); }
            Cursor = hit != null ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseUp(MouseEventArgs e) { dragging = false; }
    }
}
