using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace NumpadDesktop
{
    public sealed class KeyboardHook : IDisposable
    {
        delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
        [StructLayout(LayoutKind.Sequential)]
        struct KeyboardData { public uint vk, scan, flags, time; public UIntPtr extra; }
        [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int id, HookProc callback, IntPtr module, uint thread);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto)] static extern IntPtr GetModuleHandle(string name);
        readonly HookProc callback;
        readonly KeyboardState state = new KeyboardState();
        IntPtr handle;
        public Func<KeyBinding, bool> Pressed;
        public Action<Exception> Failed;
        public KeyboardHook()
        {
            callback = Process;
            handle = SetWindowsHookEx(13, callback, GetModuleHandle(null), 0);
            if (handle == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        static bool Held(int vk) { return (GetAsyncKeyState(vk) & 0x8000) != 0; }
        static int Modifiers()
        {
            return (Held(16) ? 1 : 0) | (Held(17) ? 2 : 0) | (Held(18) ? 4 : 0) | ((Held(91) || Held(92)) ? 8 : 0);
        }
        IntPtr Process(int code, IntPtr message, IntPtr data)
        {
            if (code < 0) return CallNextHookEx(handle, code, message, data);
            try
            {
                KeyboardData value = (KeyboardData)Marshal.PtrToStructure(data, typeof(KeyboardData));
                // Ignore SendInput events; never intercept our own or another app's automation.
                if ((value.flags & 0x10) != 0) return CallNextHookEx(handle, code, message, data);
                bool up = (value.flags & 0x80) != 0;
                KeyBinding key = new KeyBinding { VirtualKey = (int)value.vk, ScanCode = (int)value.scan, Extended = (value.flags & 1) != 0, Modifiers = Modifiers() };
                if (state.Process(key, up, Pressed)) return new IntPtr(1);
            }
            catch (Exception ex) { if (Failed != null) Failed(ex); }
            return CallNextHookEx(handle, code, message, data);
        }
        public void Dispose()
        {
            if (handle != IntPtr.Zero) { UnhookWindowsHookEx(handle); handle = IntPtr.Zero; }
            GC.KeepAlive(callback);
        }
    }
    public sealed class KeyboardState
    {
        readonly HashSet<int> down = new HashSet<int>();
        readonly HashSet<int> swallowed = new HashSet<int>();
        public bool Process(KeyBinding key, bool up, Func<KeyBinding, bool> pressed)
        {
            int identity = key.ScanCode | (key.Extended ? 0x10000 : 0);
            if (up) { down.Remove(identity); return swallowed.Remove(identity); }
            if (!down.Add(identity)) return swallowed.Contains(identity);
            if (pressed != null && pressed(key)) { swallowed.Add(identity); return true; }
            return false;
        }
    }
}
