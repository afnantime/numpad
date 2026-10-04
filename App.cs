using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Reflection;

[assembly: AssemblyTitle("Numpad Desktop")]
[assembly: AssemblyDescription("Numpad mode switching for Windows 10 virtual desktops")]
[assembly: AssemblyProduct("Numpad Desktop")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace NumpadDesktop
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            bool created;
            using (Mutex mutex = new Mutex(true, "Local\\NumpadDesktop.Application", out created))
            {
                if (!created) { MessageBox.Show("โปรแกรมเปิดอยู่แล้ว ดูไอคอนข้างนาฬิกา", "Numpad Desktop"); return; }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                try { using (TrayApplication app = new TrayApplication(Array.IndexOf(args, "--smoke") >= 0)) Application.Run(app); }
                catch (Exception ex) { Log.Write(ex); MessageBox.Show("ไม่สามารถเปิดโปรแกรมได้: " + ex.Message, "Numpad Desktop", MessageBoxButtons.OK, MessageBoxIcon.Error); }
                finally { mutex.ReleaseMutex(); }
            }
        }
    }
    static class Log
    {
        public static void Write(Exception ex)
        {
            try { Directory.CreateDirectory(Settings.Folder); File.AppendAllText(Path.Combine(Settings.Folder, "errors.log"), DateTime.Now.ToString("s") + " " + ex + Environment.NewLine); }
            catch { }
        }
    }
    sealed class TrayApplication : ApplicationContext
    {
        readonly Control dispatcher = new Control();
        readonly NotifyIcon tray;
        readonly ToolStripMenuItem mode1, mode2;
        readonly Icon icon1, icon2;
        readonly DesktopWorker worker;
        readonly KeyboardHook hook;
        readonly bool supported;
        Settings settings;
        SettingsForm form;
        bool desktopMode, closing;
        System.Windows.Forms.Timer smoke;
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
        public TrayApplication(bool smokeTest)
        {
            IntPtr window = dispatcher.Handle;
            Version os = Environment.OSVersion.Version;
            supported = os.Major == 10 && os.Build >= 17763 && os.Build < 22000;
            try { settings = Settings.Load(); }
            catch (Exception ex) { settings = new Settings(); Log.Write(ex); }
            icon1 = MakeIcon("1", Color.RoyalBlue); icon2 = MakeIcon("2", Color.SeaGreen);
            ContextMenuStrip menu = new ContextMenuStrip();
            mode1 = new ToolStripMenuItem("โหมด 1 — สลับ Desktop", null, delegate { SetMode(true); });
            mode1.Enabled = supported;
            mode2 = new ToolStripMenuItem("โหมด 2 — Numpad ปกติ", null, delegate { SetMode(false); });
            menu.Items.Add(mode1); menu.Items.Add(mode2); menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("ตั้งค่าปุ่ม ( และ )…", null, delegate { ShowSettings(); });
            ToolStripMenuItem startup = new ToolStripMenuItem("เปิดพร้อม Windows");
            try { using (RegistryKey run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) startup.Checked = run != null && run.GetValue("NumpadDesktop") != null; }
            catch (Exception ex) { Log.Write(ex); }
            startup.Click += delegate
            {
                try
                {
                    using (RegistryKey run = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                    {
                        if (startup.Checked) run.DeleteValue("NumpadDesktop", false);
                        else run.SetValue("NumpadDesktop", "\"" + Application.ExecutablePath + "\"");
                    }
                    startup.Checked = !startup.Checked;
                }
                catch (Exception ex) { Log.Write(ex); MessageBox.Show("บันทึกการเปิดพร้อม Windows ไม่สำเร็จ: " + ex.Message); }
            };
            menu.Items.Add(startup); menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("ออกจากโปรแกรม", null, delegate { ExitThread(); });
            tray = new NotifyIcon { Icon = icon2, Text = "Numpad Desktop: โหมด 2 — พิมพ์ปกติ", ContextMenuStrip = menu, Visible = true };
            tray.DoubleClick += delegate { ShowSettings(); };
            worker = new DesktopWorker(delegate(Exception ex)
            {
                Post(delegate { Log.Write(ex); SetMode(false, false); Notify("สลับ Desktop ไม่สำเร็จ — กลับโหมด 2", ex.Message, ToolTipIcon.Error); });
            });
            hook = new KeyboardHook(); hook.Pressed = OnKey;
            hook.Failed = delegate(Exception ex) { Post(delegate { Log.Write(ex); SetMode(false, false); Notify("ตรวจจับปุ่มไม่สำเร็จ", ex.Message, ToolTipIcon.Error); }); };
            UpdateMode();
            if (smokeTest)
            {
                smoke = new System.Windows.Forms.Timer { Interval = 1500 };
                smoke.Tick += delegate { ExitThread(); }; smoke.Start();
            }
            else if (!supported) Notify("Windows รุ่นนี้ยังไม่รองรับ", "รุ่นแรกใช้ Windows 10 รุ่น 1809–22H2", ToolTipIcon.Warning);
            else if (!settings.Valid()) ShowSettings();
        }
        void Post(Action action)
        {
            if (!closing && !dispatcher.IsDisposed)
                try { dispatcher.BeginInvoke(action); } catch (InvalidOperationException) { }
        }
        bool OnKey(KeyBinding key)
        {
            if (form != null && !form.IsDisposed && form.Capturing)
            {
                if (KeyboardRules.IsModifier(key.VirtualKey)) return false;
                form.AcceptKey(key); return true;
            }
            if (settings.Valid())
            {
                if (settings.Mode1.Same(key)) { SetMode(true); return true; }
                if (settings.Mode2.Same(key)) { SetMode(false); return true; }
            }
            int number = desktopMode ? KeyboardRules.DesktopNumber(key) : 0;
            if (number != 0) { worker.Enqueue(number); return true; }
            return false;
        }
        void SetMode(bool value) { SetMode(value, true); }
        void SetMode(bool value, bool announce)
        {
            if (value && !supported) return;
            bool changed = desktopMode != value;
            desktopMode = value;
            if (!value) worker.Cancel();
            UpdateMode();
            if (changed && announce) Notify(value ? "โหมด 1 — สลับ Desktop" : "โหมด 2 — Numpad ปกติ", value ? "กด Numpad 1–9 เพื่อไป Desktop ตามหมายเลข" : "ใช้ปุ่มตามปกติ โดยคงสถานะ Num Lock", ToolTipIcon.Info);
        }
        void UpdateMode()
        {
            mode1.Checked = desktopMode; mode2.Checked = !desktopMode;
            tray.Icon = desktopMode ? icon1 : icon2;
            tray.Text = desktopMode ? "Numpad Desktop: โหมด 1 — สลับ Desktop" : "Numpad Desktop: โหมด 2 — พิมพ์ปกติ";
        }
        void Notify(string title, string text, ToolTipIcon type) { tray.ShowBalloonTip(2500, title, text, type); }
        void ShowSettings()
        {
            SetMode(false, false);
            if (form != null && !form.IsDisposed) { form.Show(); form.Activate(); return; }
            form = new SettingsForm(settings, delegate(Settings value) { settings = value; });
            form.Show();
        }
        static Icon MakeIcon(string text, Color color)
        {
            using (Bitmap bitmap = new Bitmap(32, 32))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            using (SolidBrush brush = new SolidBrush(color))
            using (Font font = new Font("Segoe UI", 21, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                graphics.FillEllipse(brush, 0, 0, 31, 31);
                graphics.DrawString(text, font, Brushes.White, 8, 2);
                IntPtr native = bitmap.GetHicon();
                try { using (Icon temporary = Icon.FromHandle(native)) return (Icon)temporary.Clone(); }
                finally { DestroyIcon(native); }
            }
        }
        protected override void ExitThreadCore()
        {
            if (closing) return;
            closing = true;
            hook.Dispose(); worker.Dispose();
            if (smoke != null) smoke.Dispose();
            if (form != null) form.Dispose();
            tray.Visible = false; tray.ContextMenuStrip.Dispose(); tray.Dispose();
            icon1.Dispose(); icon2.Dispose(); dispatcher.Dispose();
            base.ExitThreadCore();
        }
    }

    sealed class SettingsForm : Form
    {
        readonly KeyBinding[] keys = new KeyBinding[4];
        readonly Label[] values = new Label[4];
        readonly Label status;
        readonly Action<Settings> saved;
        int capturing = -1;
        public bool Capturing { get { return capturing >= 0 && ContainsFocus; } }
        public SettingsForm(Settings current, Action<Settings> saved)
        {
            this.saved = saved;
            Text = "Numpad Desktop — ตั้งค่าปุ่ม";
            Font = new Font("Tahoma", 10);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(700, 450); FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; StartPosition = FormStartPosition.CenterScreen;
            keys[0] = current.Mode1; keys[1] = current.Mode2; keys[2] = current.TopLeft; keys[3] = current.TopRight;
            Label help = new Label { Left = 20, Top = 15, Width = 660, Height = 84, Text = "กดปุ่ม ‘ตรวจจับ’ แล้วกดปุ่มที่ต้องการหนึ่งครั้ง\nตั้งค่าทั้ง 4 ช่อง เพื่อให้วงเล็บแถวบนยังพิมพ์ได้ตามปกติ\nถ้ารหัสซ้ำ ให้เลือกปุ่มอื่นบน Numpad เช่น / และ * แทนวงเล็บ\nระหว่างตั้งค่า โปรแกรมจะอยู่โหมด 2 และไม่เปลี่ยน Num Lock" };
            Controls.Add(help);
            string[] names = { "1. ปุ่ม ( บน Numpad → โหมด 1", "2. ปุ่ม ) บน Numpad → โหมด 2", "3. พิมพ์ ( จากแถวบน", "4. พิมพ์ ) จากแถวบน" };
            for (int i = 0; i < 4; i++)
            {
                int row = i;
                Controls.Add(new Label { Left = 20, Top = 110 + i * 58, Width = 300, Height = 22, Text = names[i] });
                values[i] = new Label { Left = 20, Top = 134 + i * 58, Width = 540, Height = 22, Text = keys[i] == null ? "ยังไม่ได้ตั้งค่า" : keys[i].ToString() };
                Controls.Add(values[i]);
                Button capture = new Button { Left = 570, Top = 112 + i * 58, Width = 110, Height = 35, Text = "ตรวจจับ" };
                capture.Click += delegate { capturing = row; status.Text = "รอปุ่ม: " + names[row] + " (Esc เพื่อยกเลิก)"; };
                Controls.Add(capture);
            }
            status = new Label { Left = 20, Top = 350, Width = 660, Height = 48, ForeColor = Color.DarkSlateBlue, Text = "สำหรับแถวบน ให้ใช้ภาษาอังกฤษแล้วกด Shift+9 และ Shift+0 ตามคีย์บอร์ดของคุณ" };
            Controls.Add(status);
            Button save = new Button { Left = 440, Top = 405, Width = 115, Height = 32, Text = "บันทึก" };
            save.Click += delegate
            {
                capturing = -1;
                Settings value = new Settings { Mode1 = keys[0], Mode2 = keys[1], TopLeft = keys[2], TopRight = keys[3] };
                if (!value.Valid()) { status.Text = "ยังบันทึกไม่ได้: ตั้งค่าให้ครบ และเลือกปุ่มโหมดที่ไม่ซ้ำกันหรือซ้ำกับวงเล็บแถวบน"; status.ForeColor = Color.Firebrick; return; }
                try { value.Save(); saved(value); Close(); }
                catch (Exception ex) { Log.Write(ex); status.Text = "บันทึกไม่สำเร็จ: " + ex.Message; }
            };
            Controls.Add(save);
            Button close = new Button { Left = 570, Top = 405, Width = 110, Height = 32, Text = "ปิด" };
            close.Click += delegate { Close(); }; Controls.Add(close);
            Deactivate += delegate { capturing = -1; status.Text = "หยุดตรวจจับแล้ว กด ‘ตรวจจับ’ เพื่อเริ่มใหม่"; };
        }
        public void AcceptKey(KeyBinding key)
        {
            if (key.VirtualKey == 27) { capturing = -1; status.Text = "ยกเลิกการตรวจจับแล้ว"; return; }
            int row = capturing; capturing = -1;
            if (row < 0) return;
            keys[row] = key; values[row].Text = key.ToString();
            status.ForeColor = Color.DarkSlateBlue;
            status.Text = "รับปุ่มแล้ว ตั้งค่าช่องถัดไป หรือกดบันทึกเมื่อครบ";
        }
    }
}
