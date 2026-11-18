using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ScreenCoverControl
{
    // No keystrokes, text or coordinates are stored. An unmarked input trips
    // once; subsequent input is suppressed until the cover has been restored.
    public sealed class InputGuard : IDisposable
    {
        delegate IntPtr Hook(int code, IntPtr message, IntPtr data);
        readonly Hook mouseCallback, keyboardCallback;
        readonly Action trip;
        IntPtr mouse, keyboard;
        bool armed, tripped;
        public InputGuard(Action trip)
        {
            this.trip = trip;
            mouseCallback = Mouse;
            keyboardCallback = Keyboard;
        }
        public static bool IsInjectedMouse(uint flags) { return (flags & 3) != 0; }
        public static bool IsInjectedKeyboard(uint flags) { return (flags & 0x12) != 0; }
        public void Arm()
        {
            if (armed) throw new InvalidOperationException();
            tripped = false;
            mouse = SetWindowsHookEx(14, mouseCallback, GetModuleHandle(null), 0);
            if (mouse == IntPtr.Zero) throw new Win32Exception();
            keyboard = SetWindowsHookEx(13, keyboardCallback, GetModuleHandle(null), 0);
            if (keyboard == IntPtr.Zero) { Dispose(); throw new Win32Exception(); }
            armed = true;
        }
        IntPtr Mouse(int code, IntPtr message, IntPtr data)
        {
            if (code >= 0 && armed && StopInput(IsInjectedMouse(unchecked((uint)Marshal.ReadInt32(data, 12))))) return new IntPtr(1);
            return CallNextHookEx(mouse, code, message, data);
        }
        IntPtr Keyboard(int code, IntPtr message, IntPtr data)
        {
            if (code >= 0 && armed && StopInput(IsInjectedKeyboard(unchecked((uint)Marshal.ReadInt32(data, 8))))) return new IntPtr(1);
            return CallNextHookEx(keyboard, code, message, data);
        }
        bool StopInput(bool injected)
        {
            if (tripped) return true;
            if (injected) return false;
            tripped = true;
            // Callback only queues restoration: never do window work in a hook.
            try { trip(); } catch { }
            return true;
        }
        public void Dispose()
        {
            armed = false;
            if (mouse != IntPtr.Zero && UnhookWindowsHookEx(mouse)) mouse = IntPtr.Zero;
            if (keyboard != IntPtr.Zero && UnhookWindowsHookEx(keyboard)) keyboard = IntPtr.Zero;
        }
        [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int id, Hook callback, IntPtr module, uint thread);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto)] static extern IntPtr GetModuleHandle(string name);
    }
}
