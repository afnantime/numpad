using System;
using System.Collections.Generic;
using System.Threading;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.IO;

namespace NumpadDesktop
{
    [DataContract]
    public sealed class KeyBinding
    {
        [DataMember] public int VirtualKey;
        [DataMember] public int ScanCode;
        [DataMember] public bool Extended;
        [DataMember] public int Modifiers; // Shift=1, Ctrl=2, Alt=4, Win=8
        public bool Same(KeyBinding other)
        {
            return other != null && VirtualKey == other.VirtualKey && ScanCode == other.ScanCode && Extended == other.Extended && Modifiers == other.Modifiers;
        }
        public override string ToString()
        {
            return String.Format("VK={0:X2}  SC={1:X2}  {2}  modifiers={3}", VirtualKey, ScanCode, Extended ? "extended" : "normal", Modifiers);
        }
    }

    [DataContract]
    public sealed class Settings
    {
        [DataMember] public KeyBinding Mode1;
        [DataMember] public KeyBinding Mode2;
        [DataMember] public KeyBinding TopLeft;
        [DataMember] public KeyBinding TopRight;
        public bool Valid()
        {
            return Mode1 != null && Mode2 != null && TopLeft != null && TopRight != null &&
                !Mode1.Same(Mode2) && !Mode1.Same(TopLeft) && !Mode1.Same(TopRight) &&
                !Mode2.Same(TopLeft) && !Mode2.Same(TopRight) &&
                !KeyboardRules.IsModifier(Mode1.VirtualKey) && !KeyboardRules.IsModifier(Mode2.VirtualKey) &&
                Mode1.ScanCode > 0 && Mode2.ScanCode > 0;
        }
        public static string Folder { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NumpadDesktop"); } }
        public static Settings Load()
        {
            string file = Path.Combine(Folder, "settings.json");
            if (!File.Exists(file)) return new Settings();
            using (FileStream stream = File.OpenRead(file))
                return (Settings)new DataContractJsonSerializer(typeof(Settings)).ReadObject(stream);
        }
        public void Save()
        {
            if (!Valid()) throw new InvalidOperationException("ปุ่มเปลี่ยนโหมดซ้ำกันหรือซ้ำกับวงเล็บแถวบน");
            Directory.CreateDirectory(Folder);
            string file = Path.Combine(Folder, "settings.json");
            string temp = file + ".tmp";
            using (FileStream stream = File.Create(temp))
                new DataContractJsonSerializer(typeof(Settings)).WriteObject(stream, this);
            if (File.Exists(file)) File.Replace(temp, file, null);
            else File.Move(temp, file);
        }
    }

    public static class KeyboardRules
    {
        public static bool IsModifier(int vk)
        {
            return vk == 16 || vk == 17 || vk == 18 || (vk >= 160 && vk <= 165) || vk == 91 || vk == 92;
        }
        public static int DesktopNumber(KeyBinding key)
        {
            // Non-extended navigation scan codes identify the physical keypad,
            // independent of Num Lock. Extended Home/arrow keys are untouched.
            if (key.Extended || (key.Modifiers & 14) != 0) return 0;
            switch (key.ScanCode)
            {
                case 0x4F: return 1; case 0x50: return 2; case 0x51: return 3;
                case 0x4B: return 4; case 0x4C: return 5; case 0x4D: return 6;
                case 0x47: return 7; case 0x48: return 8; case 0x49: return 9;
                default: return 0;
            }
        }
    }

    public interface IDesktopService
    {
        int Count { get; }
        void Create();
        void Switch(int zeroBasedIndex);
    }
    public sealed class WindowsDesktopService : IDesktopService
    {
        public int Count { get { return VirtualDesktop.Desktop.Count; } }
        public void Create() { VirtualDesktop.Desktop.Create(); }
        public void Switch(int index) { VirtualDesktop.Desktop.FromIndex(index).MakeVisible(); }
    }
    public static class DesktopCommands
    {
        public static void Go(IDesktopService service, int number, Func<bool> cancelled)
        {
            if (number < 1 || number > 9) throw new ArgumentOutOfRangeException("number");
            // Re-enumerate each operation: Task View can change the list externally.
            int attempts = 0;
            while (service.Count < number)
            {
                if (cancelled()) return;
                if (++attempts > 9) throw new InvalidOperationException("ไม่สามารถสร้าง Desktop ได้ครบ");
                service.Create();
            }
            if (!cancelled()) service.Switch(number - 1);
        }
    }

    public sealed class DesktopWorker : IDisposable
    {
        readonly Queue<Tuple<int, int>> queue = new Queue<Tuple<int, int>>();
        readonly AutoResetEvent wake = new AutoResetEvent(false);
        readonly Thread thread;
        readonly Action<Exception> failed;
        readonly Func<IDesktopService> createService;
        bool stopped;
        int generation;
        public DesktopWorker(Action<Exception> failed) : this(failed, delegate { return new WindowsDesktopService(); }) { }
        public DesktopWorker(Action<Exception> failed, Func<IDesktopService> createService)
        {
            this.failed = failed;
            this.createService = createService;
            thread = new Thread(Run); thread.IsBackground = true;
            thread.SetApartmentState(ApartmentState.STA); thread.Start();
        }
        public void Enqueue(int number)
        {
            lock (queue) { if (stopped) return; queue.Enqueue(Tuple.Create(number, generation)); }
            wake.Set();
        }
        public void Cancel()
        {
            lock (queue) { generation++; queue.Clear(); }
        }
        bool Cancelled(int ticket)
        {
            lock (queue) return stopped || ticket != generation;
        }
        void Run()
        {
            IDesktopService service;
            try { service = createService(); }
            catch (Exception ex) { failed(ex); return; }
            while (true)
            {
                wake.WaitOne();
                while (true)
                {
                    Tuple<int, int> command;
                    lock (queue)
                    {
                        if (stopped) return;
                        if (queue.Count == 0) break;
                        command = queue.Dequeue();
                    }
                    try { DesktopCommands.Go(service, command.Item1, delegate { return Cancelled(command.Item2); }); }
                    catch (Exception ex)
                    {
                        if (!Cancelled(command.Item2)) { Cancel(); failed(ex); }
                    }
                }
            }
        }
        public void Dispose()
        {
            lock (queue) { stopped = true; generation++; queue.Clear(); }
            wake.Set();
            // Do not freeze the UI if Explorer has a COM call in flight.
            if (thread.Join(1000)) wake.Dispose();
        }
    }
}
