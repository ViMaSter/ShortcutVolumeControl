using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace volumeStates
{
    public class Hotkey
    {
        [DllImport("kernel32.dll")]
        public static extern uint GetLastError();

        [DllImport("User32.dll", SetLastError = true)]
        private static extern uint RegisterHotKey(
        [In] IntPtr hWnd,
        [In] int id,
        [In] uint fsModifiers,
        [In] uint vk);

        [DllImport("User32.dll", SetLastError = true)]
        private static extern uint UnregisterHotKey(
            [In] IntPtr hWnd,
            [In] int id);

        private HwndSource _source;
        private static int _HOTKEYINDEX = 9000;
        private static int HOTKEYINDEX
        {
            get
            {
                return ++_HOTKEYINDEX;
            }
        }

        public enum Modifier
        {
            NONE = 0x0000,
            MOD_ALT = 0x0001,
            MOD_CTRL = 0x0002,
            MOD_NOREPEAT = 0x4000,
            MOD_SHIFT = 0x0004,
            MOD_WIN = 0x0008
        };

        private int hotkeyID = HOTKEYINDEX;

        private Window windowReference;

        public Hotkey(Window parent, uint key, ModifierKeys modifier)
        {
            windowReference = parent;
            var helper = new WindowInteropHelper(windowReference);
            _source = HwndSource.FromHwnd(helper.Handle);
            _source.AddHook(HwndHook);
            RegisterHotKey(key, (uint)modifier);
        }

        private void RegisterHotKey(uint key, uint modifier)
        {
            var helper = new WindowInteropHelper(windowReference);
            uint a = RegisterHotKey(helper.Handle, hotkeyID, modifier, key);
            Debug.Assert(a != 0, "Couldn't register hotkey");
        }

        private void UnregisterHotKey()
        {
            var helper = new WindowInteropHelper(windowReference);
            uint a = UnregisterHotKey(helper.Handle, hotkeyID);
            Debug.Assert(a != 0, "Couldn't unregister hotkey");
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_HOTKEY = 0x0312;
            switch (msg)
            {
                case WM_HOTKEY:
                    if (wParam.ToInt32() == hotkeyID)
                    {
                        onHotKeyPressed();
                        handled = true;
                    }
                    break;
            }
            return IntPtr.Zero;
        }

        public delegate void OnHotKeyPressed();
        public OnHotKeyPressed onHotKeyPressed;

        public void Unmap()
        {
            _source.RemoveHook(HwndHook);
            _source = null;
            UnregisterHotKey();
        }
    }
}
