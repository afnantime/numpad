using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;
using System.IO;
using System.Runtime.Serialization.Json;

namespace NumpadDesktop
{
    static class TestProgram
    {
        static int checks;
        static void Assert(bool value, string message)
        {
            Interlocked.Increment(ref checks); if (!value) throw new Exception("FAIL: " + message);
        }
        sealed class FakeDesktop : IDesktopService
        {
            public int Total = 1, Selected = -1, Created;
            public bool Fail;
            public int Count { get { return Total; } }
            public void Create() { if (Fail) throw new InvalidOperationException("Explorer unavailable"); Created++; Total++; }
            public void Switch(int index) { if (Fail) throw new InvalidOperationException("Explorer unavailable"); Selected = index; }
        }
        static KeyBinding Key(int vk, int scan, bool extended, int mods)
        {
            return new KeyBinding { VirtualKey = vk, ScanCode = scan, Extended = extended, Modifiers = mods };
        }
        [STAThread]
        static int Main(string[] args)
        {
            try
            {
                Rules();
                WorkerRules();
                if (Array.IndexOf(args, "--desktop-integration") >= 0) DesktopIntegration();
                Console.WriteLine("PASS: " + checks + " assertions"); return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }
        static void Rules()
        {
            int[] scans = { 0x4F, 0x50, 0x51, 0x4B, 0x4C, 0x4D, 0x47, 0x48, 0x49 };
            int[] nav = { 35, 40, 34, 37, 12, 39, 36, 38, 33 };
            for (int i = 0; i < 9; i++)
            {
                Assert(KeyboardRules.DesktopNumber(Key(97 + i, scans[i], false, 0)) == i + 1, "Num Lock on " + (i + 1));
                Assert(KeyboardRules.DesktopNumber(Key(nav[i], scans[i], false, 0)) == i + 1, "Num Lock off " + (i + 1));
                Assert(KeyboardRules.DesktopNumber(Key(nav[i], scans[i], true, 0)) == 0, "extended navigation untouched");
                Assert(KeyboardRules.DesktopNumber(Key(49 + i, 2 + i, false, 0)) == 0, "top numbers untouched");
            }
            foreach (int scan in new int[] { 0x52, 0x53, 0x35, 0x37, 0x4E, 0x4A, 0x1C })
                Assert(KeyboardRules.DesktopNumber(Key(0, scan, false, 0)) == 0, "other keypad untouched");
            foreach (int mod in new int[] { 2, 4, 8 })
                Assert(KeyboardRules.DesktopNumber(Key(97, 0x4F, false, mod)) == 0, "modified shortcuts untouched");
            KeyBinding topLeft = Key(57, 10, false, 1), topRight = Key(48, 11, false, 1);
            Settings settings = new Settings { Mode1 = topLeft, Mode2 = topRight, TopLeft = topLeft, TopRight = topRight };
            Assert(!settings.Valid(), "indistinguishable brackets rejected");
            settings.Mode1 = Key(111, 0x35, true, 0); settings.Mode2 = Key(106, 0x37, false, 0);
            Assert(settings.Valid(), "alternative keypad keys accepted");
            using (MemoryStream stream = new MemoryStream())
            {
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(Settings));
                serializer.WriteObject(stream, settings); stream.Position = 0;
                Settings loaded = (Settings)serializer.ReadObject(stream);
                Assert(loaded.Valid() && loaded.Mode1.Same(settings.Mode1) && loaded.TopRight.Same(topRight), "settings JSON round trip");
            }
            settings.Mode2 = settings.Mode1; Assert(!settings.Valid(), "identical mode keys rejected");
            KeyboardState state = new KeyboardState();
            int calls = 0; bool mode1 = true;
            Func<KeyBinding, bool> handler = delegate(KeyBinding key) { calls++; return mode1 && KeyboardRules.DesktopNumber(key) != 0; };
            KeyBinding digit = Key(97, 0x4F, false, 0);
            Assert(state.Process(digit, false, handler), "mode 1 keydown suppressed");
            Assert(state.Process(digit, false, handler) && calls == 1, "hold suppresses repeat without dispatch");
            mode1 = false;
            Assert(state.Process(digit, false, handler), "mode change while held does not leak repeat");
            Assert(state.Process(Key(35, 0x4F, false, 0), true, handler), "keyup paired despite VK change");
            Assert(!state.Process(digit, false, handler), "mode 2 passes fresh keydown");
            mode1 = true;
            Assert(!state.Process(digit, false, handler), "passed key stays passed through mode change");
            Assert(!state.Process(digit, true, handler), "passed keyup stays passed");
            FakeDesktop fake = new FakeDesktop();
            DesktopCommands.Go(fake, 5, delegate { return false; });
            Assert(fake.Total == 5 && fake.Created == 4 && fake.Selected == 4, "create exactly missing desktops, map to zero based");
            DesktopCommands.Go(fake, 1, delegate { return false; });
            Assert(fake.Created == 4 && fake.Selected == 0, "existing desktop reused");
            fake.Total = 2;
            DesktopCommands.Go(fake, 5, delegate { return false; });
            Assert(fake.Total == 5 && fake.Selected == 4, "re-enumerate after external removal");
            int created = fake.Created;
            DesktopCommands.Go(fake, 9, delegate { return true; });
            Assert(fake.Created == created, "cancel avoids creation");
            fake.Fail = true; bool failed = false;
            try { DesktopCommands.Go(fake, 1, delegate { return false; }); } catch (InvalidOperationException) { failed = true; }
            Assert(failed, "desktop failure propagated to safe mode handler");
            using (KeyboardHook hook = new KeyboardHook())
            {
                Assert(hook != null, "native hook install/uninstall on Windows");
                Application.DoEvents();
            }
        }
        sealed class RecordingDesktop : IDesktopService
        {
            public readonly List<int> Switches = new List<int>();
            public readonly ManualResetEvent Done = new ManualResetEvent(false);
            public bool Fail;
            public int ThreadId;
            public int Count { get { return 9; } }
            public void Create() { throw new Exception("Should not create"); }
            public void Switch(int index)
            {
                if (Fail) throw new InvalidOperationException("Test COM failure");
                Assert(Thread.CurrentThread.GetApartmentState() == ApartmentState.STA, "worker COM apartment");
                ThreadId = Thread.CurrentThread.ManagedThreadId;
                Switches.Add(index);
                if (Switches.Count == 3) Done.Set();
            }
        }
        static void WorkerRules()
        {
            RecordingDesktop service = new RecordingDesktop();
            Exception error = null;
            using (DesktopWorker worker = new DesktopWorker(delegate(Exception ex) { error = ex; service.Done.Set(); }, delegate { return service; }))
            {
                worker.Enqueue(1); worker.Enqueue(5); worker.Enqueue(9);
                Assert(service.Done.WaitOne(3000), "worker completes queued commands");
            }
            Assert(error == null && service.Switches.Count == 3 && service.Switches[0] == 0 && service.Switches[1] == 4 && service.Switches[2] == 8, "queued commands execute in order");
            Assert(service.ThreadId != Thread.CurrentThread.ManagedThreadId, "desktop work outside keyboard/UI thread");
            service.Done.Dispose();
            RecordingDesktop failing = new RecordingDesktop { Fail = true };
            error = null;
            using (DesktopWorker worker = new DesktopWorker(delegate(Exception ex) { error = ex; failing.Done.Set(); }, delegate { return failing; }))
            {
                worker.Enqueue(1);
                Assert(failing.Done.WaitOne(3000), "worker reports desktop failure");
            }
            Assert(error is InvalidOperationException && failing.Switches.Count == 0, "failure reaches app fallback handler");
            failing.Done.Dispose();
        }
        static void DesktopIntegration()
        {
            Version os = Environment.OSVersion.Version;
            Console.WriteLine("Windows " + os);
            if (os.Build < 17763 || os.Build >= 22000) throw new Exception("Windows 10 1809–22H2 required");
            VirtualDesktop.Desktop original = VirtualDesktop.Desktop.Current;
            List<VirtualDesktop.Desktop> owned = new List<VirtualDesktop.Desktop>();
            int initial = VirtualDesktop.Desktop.Count;
            try
            {
                while (VirtualDesktop.Desktop.Count < 9) owned.Add(VirtualDesktop.Desktop.Create());
                WindowsDesktopService service = new WindowsDesktopService();
                foreach (int number in new int[] { 1, 5, 9 })
                {
                    DesktopCommands.Go(service, number, delegate { return false; });
                    Thread.Sleep(200);
                    Assert(VirtualDesktop.Desktop.FromDesktop(VirtualDesktop.Desktop.Current) == number - 1, "real switch to Desktop " + number);
                    Console.WriteLine("Switched to Desktop " + number);
                }
            }
            finally
            {
                original.MakeVisible();
                for (int i = owned.Count - 1; i >= 0; i--) owned[i].Remove(original);
                Assert(VirtualDesktop.Desktop.Count == initial, "only test-created desktops removed");
                Assert(VirtualDesktop.Desktop.FromDesktop(VirtualDesktop.Desktop.Current) == VirtualDesktop.Desktop.FromDesktop(original), "original desktop restored");
                Console.WriteLine("Restored original desktop and count: " + initial);
            }
        }
    }
}
