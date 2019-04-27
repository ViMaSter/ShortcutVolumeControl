using VolumeControl.States;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using volumeStates;

namespace VolumeControl.AudioWrapper
{
    public class HotkeyCollection
    {
        public class AudioState
        {
            private class WindowsHotkey
            {
                #region windows API helper
                [DllImport("User32.dll")]
                private static extern uint RegisterHotKey(
                [In] IntPtr hWnd,
                [In] int id,
                [In] uint fsModifiers,
                [In] uint vk);

                [DllImport("User32.dll")]
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

                private int hotkeyID = HOTKEYINDEX;

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

                private void RegisterHotKey(uint key, uint modifier)
                {
                    var helper = new WindowInteropHelper(Application.Current.MainWindow);
                    uint a = RegisterHotKey(helper.Handle, hotkeyID, modifier, key);
                    Debug.Assert(a != 0, "Couldn't register hotkey");
                }

                private void UnregisterHotKey()
                {
                    var helper = new WindowInteropHelper(Application.Current.MainWindow);
                    uint a = UnregisterHotKey(helper.Handle, hotkeyID);
                    Debug.Assert(a != 0, "Couldn't unregister hotkey");
                }
                #endregion

                private Action onHotKeyPressed;
                private ModifierKeys modifierKeys;
                private Key keyToUse;

                public WindowsHotkey(ModifierKeys modifier, Key key, Action pressedEvent)
                {
                    modifierKeys = modifier;
                    keyToUse = key;

                    onHotKeyPressed = pressedEvent;
                }

                public void Map()
                {
                    var helper = new WindowInteropHelper(Application.Current.MainWindow);
                    _source = HwndSource.FromHwnd(helper.Handle);
                    _source.AddHook(HwndHook);
                    uint keyCode = (uint)KeyInterop.VirtualKeyFromKey(keyToUse);
                    RegisterHotKey(keyCode, (uint)modifierKeys);
                }

                public void Unmap()
                {
                    _source.RemoveHook(HwndHook);
                    _source = null;
                    UnregisterHotKey();
                }
            }

            WindowsHotkey windowsHotkey;
            public AppStatus AppStatusReference
            {
                get => appStatusReference;
            }
            AppStatus appStatusReference;

            public AudioState(ModifierKeys modifier, Key key, AppStatus appStatus, Action hotkeyReaction)
            {
                windowsHotkey = new WindowsHotkey(modifier, key, hotkeyReaction);
                appStatusReference = appStatus;
            }

            public void Map()
            {
                windowsHotkey.Map();
            }

            public void Unmap()
            {
                windowsHotkey.Unmap();
            }
        }

        private Func<AppReflection> GetReflection;
        private Dictionary<Tuple<ModifierKeys, Key>, AudioState> hotkeysByState = new Dictionary<Tuple<ModifierKeys, Key>, AudioState>();

        public HotkeyCollection(Func<AppReflection> getReflection)
        {
            GetReflection = getReflection;
        }

        public void SetKeyPerState(ModifierKeys modifier, Key key, AppStatus appStatus)
        {
            Tuple<ModifierKeys, Key> mapping = new Tuple<ModifierKeys, Key>(modifier, key);

            hotkeysByState[mapping] = new AudioState
            (
                modifier, key, appStatus, () => GetReflection().ApplyState(hotkeysByState[mapping].AppStatusReference)
            );
        }

        public void EnableAllHotkeys()
        {
            foreach (var mapping in hotkeysByState)
            {
                mapping.Value.Map();
            }
        }

        public void DisableAllHotkeys()
        {
            foreach (var mapping in hotkeysByState)
            {
                mapping.Value.Unmap();
            }
        }
    }
}
