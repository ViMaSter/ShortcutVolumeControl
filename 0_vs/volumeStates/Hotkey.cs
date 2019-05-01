using VolumeControl.States;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using VolumeStates;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VolumeControl.AudioWrapper
{
    public class HotkeyCollection
    {
        public class AudioState
        {
            private class WindowsHotkey
            {
                #region windows API helper
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
                    uint winAPIResult = RegisterHotKey(helper.Handle, hotkeyID, modifier, key);
                    if (winAPIResult == 0)
                    {
                        int winAPIErrorCode = Marshal.GetLastWin32Error();
                        throw new SystemException("Couldn't register hotkey - error code: " + winAPIErrorCode);
                    }
                }

                private void UnregisterHotKey()
                {
                    var helper = new WindowInteropHelper(Application.Current.MainWindow);
                    uint winAPIResult = UnregisterHotKey(helper.Handle, hotkeyID);
                    if (winAPIResult == 0)
                    {
                        int winAPIErrorCode = Marshal.GetLastWin32Error();
                        throw new SystemException("Couldn't unregister hotkey - error code: " + winAPIErrorCode);
                    }
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

                public void Activate()
                {
                    var helper = new WindowInteropHelper(Application.Current.MainWindow);
                    _source = HwndSource.FromHwnd(helper.Handle);
                    _source.AddHook(HwndHook);
                    uint keyCode = (uint)KeyInterop.VirtualKeyFromKey(keyToUse);
                    RegisterHotKey(keyCode, (uint)modifierKeys);
                }

                public void Deactivate()
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

            public void Activate()
            {
                windowsHotkey.Activate();
            }

            public void Deactivate()
            {
                windowsHotkey.Deactivate();
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

        public void RemoveMapping(ModifierKeys modifier, Key key)
        {
            Tuple<ModifierKeys, Key> mapping = new Tuple<ModifierKeys, Key>(modifier, key);

            if (hotkeysByState.ContainsKey(mapping))
            {
                hotkeysByState.Remove(mapping);
            }
        }

        public void ClearMappings()
        {
            DisableAllHotkeys();
            hotkeysByState.Clear();
        }

        public bool AttemptPress(Tuple<ModifierKeys, Key> keyCombo)
        {
            if (hotkeysByState.ContainsKey(keyCombo))
            {
                GetReflection().ApplyState(hotkeysByState[keyCombo].AppStatusReference);
                return true;
            }

            return false;
        }

        public void EnableAllHotkeys()
        {
            foreach (var mapping in hotkeysByState)
            {
                mapping.Value.Activate();
            }
        }

        public void DisableAllHotkeys()
        {
            foreach (var mapping in hotkeysByState)
            {
                mapping.Value.Deactivate();
            }
        }

        public JArray Serialize()
        {
            JArray stateArray = new JArray();
            foreach(var entry in hotkeysByState)
            {
                JObject entryObject = new JObject();

                JObject hotkeyObject = new JObject();
                hotkeyObject.Add("modifier", (int)entry.Key.Item1);
                hotkeyObject.Add("key", (int)entry.Key.Item2);

                JArray appsObject = new JArray();
                foreach (var pathToVolume in entry.Value.AppStatusReference.ProcessPathToVolume)
                {
                    JObject app = new JObject();
                    app.Add("process", pathToVolume.Key);
                    app.Add("volume", pathToVolume.Value);
                    appsObject.Add(app);
                }

                entryObject.Add("hotkey", hotkeyObject);
                entryObject.Add("apps", appsObject);
                stateArray.Add(entryObject);
            }
            return stateArray;
        }
    }
}
