using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace VolumeStates.AudioWrapper
{
    public sealed class AudioSession : IDisposable, INotifyPropertyChanged
    {
        private static class Utilities
        {
            public static class NativeMethods
            {
                public const uint AUDCLNT_S_NO_SINGLE_PROCESS = 0x0889000d;
                public const uint ERROR_NO_SUCH_DEVINST = 0xe000020b;

                [Flags]
                public enum ProcessAccessFlags : uint
                {
                    All = 0x001F0FFF,
                    Terminate = 0x00000001,
                    CreateThread = 0x00000002,
                    VirtualMemoryOperation = 0x00000008,
                    VirtualMemoryRead = 0x00000010,
                    VirtualMemoryWrite = 0x00000020,
                    DuplicateHandle = 0x00000040,
                    CreateProcess = 0x000000080,
                    SetQuota = 0x00000100,
                    SetInformation = 0x00000200,
                    QueryInformation = 0x00000400,
                    QueryLimitedInformation = 0x00001000,
                    Synchronize = 0x00100000
                }

                [DllImport("kernel32.dll", SetLastError = true)]
                public static extern IntPtr OpenProcess(
                     ProcessAccessFlags processAccess,
                     [MarshalAs(UnmanagedType.Bool)]
                     bool inheritHandle,
                     int processId
                );

                [DllImport("ole32.dll")]
                public static extern int PropVariantClear(ref PROPVARIANT pvar);

                [ComImport]
                [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
                public class MMDeviceEnumerator
                {
                }

                [Flags]
                public enum CLSCTX
                {
                    INPROC_SERVER = 0x1,
                    INPROC_HANDLER = 0x2,
                    LOCAL_SERVER = 0x4,
                    REMOTE_SERVER = 0x10,
                    ALL = INPROC_SERVER | INPROC_HANDLER | LOCAL_SERVER | REMOTE_SERVER
                }

                public enum STGM
                {
                    READ = 0x00000000,
                }

                public enum EDataFlow
                {
                    eRender,
                    eCapture,
                    eAll,
                }

                public enum ERole
                {
                    eConsole,
                    eMultimedia,
                    eCommunications,
                }

                public enum DEVICE_STATE
                {
                    ACTIVE = 0x00000001,
                    DISABLED = 0x00000002,
                    NOTPRESENT = 0x00000004,
                    UNPLUGGED = 0x00000008,
                    MASK_ALL = 0x0000000F
                }

                [StructLayout(LayoutKind.Sequential)]
                public struct PROPERTYKEY
                {
                    public Guid fmtid;
                    public int pid;

                    public override string ToString()
                    {
                        return fmtid.ToString("B", CultureInfo.InvariantCulture) + " " + pid;
                    }
                }

                // NOTE: we only define what we handle
                [Flags]
                public enum VARTYPE : short
                {
                    VT_I4 = 3,
                    VT_BOOL = 11,
                    VT_UI4 = 19,
                    VT_UI8 = 21,
                    VT_LPWSTR = 31,
                    VT_BLOB = 65,
                    VT_CLSID = 72,
                }

                [StructLayout(LayoutKind.Sequential)]
                public struct PROPVARIANT
                {
                    public VARTYPE vt;
                    public ushort wReserved1;
                    public ushort wReserved2;
                    public ushort wReserved3;
                    public PROPVARIANTunion union;

                    public object GetValue()
                    {
                        switch (vt)
                        {
                            case VARTYPE.VT_UI4:
                                return union.ulVal;

                            case VARTYPE.VT_BOOL:
                                return union.boolVal != 0;

                            case VARTYPE.VT_LPWSTR:
                                return Marshal.PtrToStringUni(union.pwszVal);

                            case VARTYPE.VT_UI8:
                                return union.uhVal;

                            case VARTYPE.VT_CLSID:
                                return (Guid)Marshal.PtrToStructure(union.puuid, typeof(Guid));

                            case VARTYPE.VT_I4:
                                return union.lVal;

                            default:
                                return vt.ToString() + ":?";
                        }
                    }
                }

#pragma warning disable CA1823
                [StructLayout(LayoutKind.Explicit)]
                public struct PROPVARIANTunion
                {
                    [FieldOffset(0)]
                    public int lVal;
                    [FieldOffset(0)]
                    public uint ulVal;
                    [FieldOffset(0)]
                    public ulong uhVal;
                    [FieldOffset(0)]
                    public short boolVal;
                    [FieldOffset(0)]
                    public IntPtr pwszVal;
                    [FieldOffset(0)]
                    public IntPtr puuid;
                }
#pragma warning restore CA1823

                [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
                public interface IMMDeviceEnumerator
                {
                    [PreserveSig]
                    int EnumAudioEndpoints(EDataFlow dataFlow, DEVICE_STATE dwStateMask, out IMMDeviceCollection ppDevices);

                    [PreserveSig]
                    int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice ppEndpoint);

                    [PreserveSig]
                    int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string pwstrId, out IMMDevice ppDevice);

                    [PreserveSig]
                    int RegisterEndpointNotificationCallback(IMMNotificationClient pClient);

                    [PreserveSig]
                    int UnregisterEndpointNotificationCazllback(IMMNotificationClient pClient);
                }

                [Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
                public interface IMMNotificationClient
                {
                    void OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId, DEVICE_STATE dwNewState);
                    void OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId);
                    void OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
                    void OnDefaultDeviceChanged(EDataFlow flow, ERole role, string pwstrDefaultDeviceId);
                    void OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId, PROPERTYKEY key);
                }

                [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
                public interface IMMDeviceCollection
                {
                    [PreserveSig]
                    int GetCount(out int pcDevices);

                    [PreserveSig]
                    int Item(int nDevice, out IMMDevice ppDevice);
                }

                [Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
                public interface IMMDevice
                {
                    [PreserveSig]
                    int Activate([MarshalAs(UnmanagedType.LPStruct)] Guid riid, CLSCTX dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);

                    [PreserveSig]
                    int OpenPropertyStore(STGM stgmAccess, out IPropertyStore ppProperties);

                    [PreserveSig]
                    int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);

                    [PreserveSig]
                    int GetState(out DEVICE_STATE pdwState);
                }

                [Guid("6f79d558-3e96-4549-a1d1-7d75d2288814"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
                private interface IPropertyDescription
                {
                    [PreserveSig]
                    int GetPropertyKey(out PROPERTYKEY pkey);

                    [PreserveSig]
                    int GetCanonicalName(out IntPtr ppszName);

                    [PreserveSig]
                    int GetPropertyType(out short pvartype);

                    [PreserveSig]
                    int GetDisplayName(out IntPtr ppszName);

                    // WARNING: the rest is undefined. you *can't* implement it, only use it.
                }

                [Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
                public interface IPropertyStore
                {
                    [PreserveSig]
                    int GetCount(out int cProps);

                    [PreserveSig]
                    int GetAt(int iProp, out PROPERTYKEY pkey);

                    [PreserveSig]
                    int GetValue(ref PROPERTYKEY key, ref PROPVARIANT pv);

                    [PreserveSig]
                    int SetValue(ref PROPERTYKEY key, ref PROPVARIANT propvar);

                    [PreserveSig]
                    int Commit();
                }

                [Guid("BFA971F1-4D5E-40BB-935E-967039BFBEE4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
                private interface IAudioSessionManager
                {
                    [PreserveSig]
                    int GetAudioSessionControl([MarshalAs(UnmanagedType.LPStruct)] Guid AudioSessionGuid, int StreamFlags, out IAudioSessionControl SessionControl);

                    [PreserveSig]
                    int GetSimpleAudioVolume([MarshalAs(UnmanagedType.LPStruct)] Guid AudioSessionGuid, int StreamFlags, out ISimpleAudioVolume AudioVolume);
                }

                [Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
                public interface IAudioSessionManager2
                {
                    [PreserveSig]
                    int GetAudioSessionControl([MarshalAs(UnmanagedType.LPStruct)] Guid AudioSessionGuid, int StreamFlags, out IAudioSessionControl SessionControl);

                    [PreserveSig]
                    int GetSimpleAudioVolume([MarshalAs(UnmanagedType.LPStruct)] Guid AudioSessionGuid, bool StreamFlags, out ISimpleAudioVolume AudioVolume);

                    [PreserveSig]
                    int GetSessionEnumerator(out IAudioSessionEnumerator SessionEnum);

                    [PreserveSig]
                    int RegisterSessionNotification(IAudioSessionNotification SessionNotification);

                    [PreserveSig]
                    int UnregisterSessionNotification(IAudioSessionNotification SessionNotification);

                    int RegisterDuckNotificationNotImpl();
                    int UnregisterDuckNotificationNotImpl();
                }

                [Guid("641DD20B-4D41-49CC-ABA3-174B9477BB08"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
                public interface IAudioSessionNotification
                {
                    void OnSessionCreated(IAudioSessionControl NewSession);
                }

                [Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
                public interface IAudioSessionEnumerator
                {
                    [PreserveSig]
                    int GetCount(out int SessionCount);

                    [PreserveSig]
                    int GetSession(int SessionCount, out IAudioSessionControl Session);
                }

                [Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
                internal interface IAudioSessionControl2
                {
                    // IAudioSessionControl
                    [PreserveSig]
                    int GetState(out AudioSessionState pRetVal);

                    [PreserveSig]
                    int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);

                    [PreserveSig]
                    int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)]string Value, [MarshalAs(UnmanagedType.LPStruct)] Guid EventContext);

                    [PreserveSig]
                    int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);

                    [PreserveSig]
                    int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string Value, [MarshalAs(UnmanagedType.LPStruct)] Guid EventContext);

                    [PreserveSig]
                    int GetGroupingParam(out Guid pRetVal);

                    [PreserveSig]
                    int SetGroupingParam([MarshalAs(UnmanagedType.LPStruct)] Guid Override, [MarshalAs(UnmanagedType.LPStruct)] Guid EventContext);

                    [PreserveSig]
                    int RegisterAudioSessionNotification(IAudioSessionEvents NewNotifications);

                    [PreserveSig]
                    int UnregisterAudioSessionNotification(IAudioSessionEvents NewNotifications);

                    // IAudioSessionControl2
                    [PreserveSig]
                    int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);

                    [PreserveSig]
                    int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);

                    [PreserveSig]
                    int GetProcessId(out int pRetVal);

                    [PreserveSig]
                    int IsSystemSoundsSession();

                    [PreserveSig]
                    int SetDuckingPreference(bool optOut);
                }

                [Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
                public interface IAudioSessionControl
                {
                    [PreserveSig]
                    int GetState(out AudioSessionState pRetVal);

                    [PreserveSig]
                    int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);

                    [PreserveSig]
                    int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)]string Value, [MarshalAs(UnmanagedType.LPStruct)] Guid EventContext);

                    [PreserveSig]
                    int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);

                    [PreserveSig]
                    int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string Value, [MarshalAs(UnmanagedType.LPStruct)] Guid EventContext);

                    [PreserveSig]
                    int GetGroupingParam(out Guid pRetVal);

                    [PreserveSig]
                    int SetGroupingParam([MarshalAs(UnmanagedType.LPStruct)] Guid Override, [MarshalAs(UnmanagedType.LPStruct)] Guid EventContext);

                    [PreserveSig]
                    int RegisterAudioSessionNotification(IAudioSessionEvents NewNotifications);

                    [PreserveSig]
                    int UnregisterAudioSessionNotification(IAudioSessionEvents NewNotifications);
                }

                [Guid("24918ACC-64B3-37C1-8CA9-74A66E9957A8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
                public interface IAudioSessionEvents
                {
                    void OnDisplayNameChanged([MarshalAs(UnmanagedType.LPWStr)] string NewDisplayName, [MarshalAs(UnmanagedType.LPStruct)] Guid EventContext);
                    void OnIconPathChanged([MarshalAs(UnmanagedType.LPWStr)] string NewIconPath, [MarshalAs(UnmanagedType.LPStruct)] Guid EventContext);
                    void OnSimpleVolumeChanged(float NewVolume, bool NewMute, [MarshalAs(UnmanagedType.LPStruct)] Guid EventContext);
                    void OnChannelVolumeChanged(int ChannelCount, IntPtr NewChannelVolumeArray, int ChangedChannel, [MarshalAs(UnmanagedType.LPStruct)] Guid EventContext);
                    void OnGroupingParamChanged([MarshalAs(UnmanagedType.LPStruct)] Guid NewGroupingParam, [MarshalAs(UnmanagedType.LPStruct)] Guid EventContext);
                    void OnStateChanged(AudioSessionState NewState);
                    void OnSessionDisconnected(AudioSessionDisconnectReason DisconnectReason);
                }

                [Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
                public interface ISimpleAudioVolume
                {
                    [PreserveSig]
                    int SetMasterVolume(float fLevel, [MarshalAs(UnmanagedType.LPStruct)] Guid EventContext);

                    [PreserveSig]
                    int GetMasterVolume(out float pfLevel);

                    [PreserveSig]
                    int SetMute(bool bMute, [MarshalAs(UnmanagedType.LPStruct)] Guid EventContext);

                    [PreserveSig]
                    int GetMute(out bool pbMute);
                }

                [DllImport("Kernel32.dll", CharSet = CharSet.Unicode)]
                [return: MarshalAs(UnmanagedType.Bool)]
                public static extern bool QueryFullProcessImageName([In] IntPtr hProcess, [In] uint dwFlags, [Out] StringBuilder lpExeName, [In, Out] ref int lpdwSize);
            }

            private static NativeMethods.IAudioSessionManager2 GetAudioSessionManager(NativeMethods.IMMDevice device)
            {
                if (device == null)
                    return null;

                // win7+ only
                object o;
                if (device.Activate(typeof(NativeMethods.IAudioSessionManager2).GUID, 0, IntPtr.Zero, out o) != 0 || o == null)
                    return null;

                return o as NativeMethods.IAudioSessionManager2;
            }

            [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1804", Justification = "Members 'not used' are used to assert their values, but only in Debug-builds for performance benefits")]
            private static AudioDevice CreateDevice(NativeMethods.IMMDevice dev)
            {
                if (dev == null)
                    return null;

                string id;
                Marshal.ThrowExceptionForHR(dev.GetId(out id));
                Marshal.ThrowExceptionForHR(dev.GetState(out var state));
                Dictionary<string, object> properties = new Dictionary<string, object>();
                Marshal.ThrowExceptionForHR(dev.OpenPropertyStore(NativeMethods.STGM.READ, out var store));
                if (store != null)
                {
                    Marshal.ThrowExceptionForHR(store.GetCount(out var propCount));
                    for (int j = 0; j < propCount; j++)
                    {
                        NativeMethods.PROPERTYKEY pk;
                        if (store.GetAt(j, out pk) == 0)
                        {
                            NativeMethods.PROPVARIANT value = new NativeMethods.PROPVARIANT();
                            int errorCode = store.GetValue(ref pk, ref value);
                            if ((uint)errorCode == NativeMethods.ERROR_NO_SUCH_DEVINST)
                            {
                                continue;
                            }
                            Debug.Assert(errorCode == 0, "Error obtaining information from windows API - error code: " + errorCode);
                            object v = value.GetValue();
                            if (value.vt != NativeMethods.VARTYPE.VT_BLOB) // for some reason, this fails?
                            {
                                errorCode = NativeMethods.PropVariantClear(ref value);
                                Debug.Assert(errorCode == 0, "Error obtaining information from windows API - error code: " + errorCode);
                            }
                            string name = pk.ToString();
                            properties[name] = v;
                        }
                    }
                }
                return new AudioDevice(id, (AudioDeviceStates)state, properties);
            }

            [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1804", Justification = "Members 'not used' are used to assert their values, but only in Debug-builds for performance benefits")]
            private static NativeMethods.IMMDevice GetDefaultSpeakers()
            {
                // get the speakers (1st render + multimedia) device
                NativeMethods.IMMDeviceEnumerator deviceEnumerator = (NativeMethods.IMMDeviceEnumerator)(new NativeMethods.MMDeviceEnumerator());
                NativeMethods.IMMDevice speakers;
                int errorCode = deviceEnumerator.GetDefaultAudioEndpoint(NativeMethods.EDataFlow.eRender, NativeMethods.ERole.eMultimedia, out speakers);
                Debug.Assert(errorCode == 0, "Error obtaining information from windows API - error code: " + errorCode);
                return speakers;
            }

            public static IList<AudioDevice> RequestAllDevices()
            {
                List<AudioDevice> list = new List<AudioDevice>();
                NativeMethods.IMMDeviceEnumerator deviceEnumerator = null;
                deviceEnumerator = (NativeMethods.IMMDeviceEnumerator)(new NativeMethods.MMDeviceEnumerator());
                if (deviceEnumerator == null)
                    return list;

                NativeMethods.IMMDeviceCollection collection;
                Marshal.ThrowExceptionForHR(deviceEnumerator.EnumAudioEndpoints(NativeMethods.EDataFlow.eRender, NativeMethods.DEVICE_STATE.MASK_ALL, out collection));
                if (collection == null)
                    return list;

                int count;
                Marshal.ThrowExceptionForHR(collection.GetCount(out count));
                for (int i = 0; i < count; i++)
                {
                    NativeMethods.IMMDevice dev;
                    Marshal.ThrowExceptionForHR(collection.Item(i, out dev));
                    if (dev != null)
                    {
                        list.Add(CreateDevice(dev));
                    }
                }
                return list;
            }

            public static AudioDevice RequestDefaultSpeakers()
            {
                return CreateDevice(GetDefaultSpeakers());
            }

            public static IList<AudioSession> RequestAllSessions(AudioDevice device)
            {
                if (device == null)
                {
                    throw new ArgumentNullException(nameof(device));
                }

                List<AudioSession> list = new List<AudioSession>();
                NativeMethods.IMMDeviceEnumerator deviceEnumerator = (NativeMethods.IMMDeviceEnumerator)(new NativeMethods.MMDeviceEnumerator());
                NativeMethods.IMMDevice immDevice;
                Marshal.ThrowExceptionForHR(deviceEnumerator.GetDevice(device.Id, out immDevice));
                Debug.Assert(immDevice != null, "Unable to retrieve IMMDevice with EnumeratorName of AudioDevice");
                NativeMethods.IAudioSessionManager2 mgr = GetAudioSessionManager(immDevice);
                if (mgr == null)
                    return list;

                NativeMethods.IAudioSessionEnumerator sessionEnumerator;
                Marshal.ThrowExceptionForHR(mgr.GetSessionEnumerator(out sessionEnumerator));
                int count;
                Marshal.ThrowExceptionForHR(sessionEnumerator.GetCount(out count));

                for (int i = 0; i < count; i++)
                {
                    NativeMethods.IAudioSessionControl ctl;
                    Marshal.ThrowExceptionForHR(sessionEnumerator.GetSession(i, out ctl));
                    if (ctl == null)
                        continue;

                    NativeMethods.IAudioSessionControl2 ctl2 = ctl as NativeMethods.IAudioSessionControl2;
                    NativeMethods.ISimpleAudioVolume sav = ctl as NativeMethods.ISimpleAudioVolume;
                    if (ctl2 != null && sav != null)
                    {
                        list.Add(new AudioSession(ctl2, sav));
                    }
                }
                Marshal.ReleaseComObject(sessionEnumerator);
                Marshal.ReleaseComObject(mgr);
                return list;
            }
        }

        public static IList<AudioSession> RequestAllSessions(AudioDevice device)
        {
            return Utilities.RequestAllSessions(device);
        }

        public static IList<AudioDevice> RequestAllDevices()
        {
            return Utilities.RequestAllDevices();
        }

        public static AudioDevice RequestDefaultSpeakers()
        {
            return Utilities.RequestDefaultSpeakers();
        }

        private Utilities.NativeMethods.IAudioSessionControl2 _ctl;
        private Utilities.NativeMethods.ISimpleAudioVolume _sav;
        private Process _process;
        UpdateClass classInstance;

        public event PropertyChangedEventHandler PropertyChanged;

        private AudioSession(Utilities.NativeMethods.IAudioSessionControl2 ctl, Utilities.NativeMethods.ISimpleAudioVolume sav)
        {
            _ctl = ctl;
            _sav = sav;
            classInstance = new UpdateClass(this);
            Marshal.ThrowExceptionForHR(_ctl.RegisterAudioSessionNotification(classInstance));
        }

        ~AudioSession()
        {
            if (classInstance != null)
            {
                Marshal.ThrowExceptionForHR(_ctl.UnregisterAudioSessionNotification(classInstance));
            }
        }

        private class UpdateClass : Utilities.NativeMethods.IAudioSessionEvents
        {
            AudioSession sessionReference;
            public UpdateClass(AudioSession session)
            {
                sessionReference = session;
            }

            void Utilities.NativeMethods.IAudioSessionEvents.OnChannelVolumeChanged(int ChannelCount, IntPtr NewChannelVolumeArray, int ChangedChannel, Guid EventContext) { }
            void Utilities.NativeMethods.IAudioSessionEvents.OnDisplayNameChanged(string NewDisplayName, Guid EventContext) { }
            void Utilities.NativeMethods.IAudioSessionEvents.OnGroupingParamChanged(Guid NewGroupingParam, Guid EventContext) { }
            void Utilities.NativeMethods.IAudioSessionEvents.OnIconPathChanged(string NewIconPath, Guid EventContext) { }
            void Utilities.NativeMethods.IAudioSessionEvents.OnSessionDisconnected(AudioSessionDisconnectReason DisconnectReason) { }
            void Utilities.NativeMethods.IAudioSessionEvents.OnSimpleVolumeChanged(float NewVolume, bool NewMute, Guid EventContext)
            {
                sessionReference.PropertyChanged?.Invoke(sessionReference, new PropertyChangedEventArgs("Volume"));
            }
            void Utilities.NativeMethods.IAudioSessionEvents.OnStateChanged(AudioSessionState NewState) { }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1804", Justification = "Members 'not used' are used to assert their values, but only in Debug-builds for performance benefits")]
        public Process Process
        {
            get
            {
                if (_process == null && ProcessId != 0)
                {
                    _process = Process.GetProcessById(ProcessId);
                }
                return _process;
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1804", Justification = "Members 'not used' are used to assert their values, but only in Debug-builds for performance benefits")]
        public string ProcessPath
        {
            get
            {
                int capacity = 2000;
                StringBuilder builder = new StringBuilder(capacity);
                IntPtr ptr = Utilities.NativeMethods.OpenProcess(Utilities.NativeMethods.ProcessAccessFlags.QueryLimitedInformation, false, ProcessId);
                if (!Utilities.NativeMethods.QueryFullProcessImageName(ptr, 0, builder, ref capacity))
                {
                    return String.Empty;
                }

                return builder.ToString();
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1804", Justification = "Members 'not used' are used to assert their values, but only in Debug-builds for performance benefits")]
        public int ProcessId
        {
            get
            {
                CheckDisposed();
                int i;
                int errorCode = _ctl.GetProcessId(out i);
                Debug.Assert(errorCode == 0 || (uint)errorCode == Utilities.NativeMethods.AUDCLNT_S_NO_SINGLE_PROCESS, "Error obtaining information from windows API - error code: " + errorCode);
                return i;
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1804", Justification = "Members 'not used' are used to assert their values, but only in Debug-builds for performance benefits")]
        public string Identifier
        {
            get
            {
                CheckDisposed();
                string s;
                int errorCode = _ctl.GetSessionIdentifier(out s);
                Debug.Assert(errorCode == 0, "Error obtaining information from windows API - error code: " + errorCode);
                return s;
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1804", Justification = "Members 'not used' are used to assert their values, but only in Debug-builds for performance benefits")]
        public string InstanceIdentifier
        {
            get
            {
                CheckDisposed();
                string s;
                int errorCode = _ctl.GetSessionInstanceIdentifier(out s);
                Debug.Assert(errorCode == 0, "Error obtaining information from windows API - error code: " + errorCode);
                return s;
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1804", Justification = "Members 'not used' are used to assert their values, but only in Debug-builds for performance benefits")]
        public AudioSessionState State
        {
            get
            {
                CheckDisposed();
                AudioSessionState s;
                int errorCode = _ctl.GetState(out s);
                Debug.Assert(errorCode == 0, "Error obtaining information from windows API - error code: " + errorCode);
                return s;
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1804", Justification = "Members 'not used' are used to assert their values, but only in Debug-builds for performance benefits")]
        public Guid GroupingParameter
        {
            get
            {
                CheckDisposed();
                Guid g;
                int errorCode = _ctl.GetGroupingParam(out g);
                Debug.Assert(errorCode == 0, "Error obtaining information from windows API - error code: " + errorCode);
                return g;
            }
            set
            {
                CheckDisposed();
                int errorCode = _ctl.SetGroupingParam(value, Guid.Empty);
                Debug.Assert(errorCode == 0, "Error setting information using windows API - error code: " + errorCode);
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1804", Justification = "Members 'not used' are used to assert their values, but only in Debug-builds for performance benefits")]
        public string DisplayName
        {
            get
            {
                CheckDisposed();
                Marshal.ThrowExceptionForHR(_ctl.GetDisplayName(out string s));
                return s;
            }
            set
            {
                CheckDisposed();
                Marshal.ThrowExceptionForHR(_ctl.GetDisplayName(out string s));
                if (s != value)
                {
                    Marshal.ThrowExceptionForHR(_ctl.SetDisplayName(value, Guid.Empty));
                }
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1804", Justification = "Members 'not used' are used to assert their values, but only in Debug-builds for performance benefits")]
        public float Volume
        {
            get
            {
                CheckDisposed();
                float level;
                int errorCode = _sav.GetMasterVolume(out level);
                Debug.Assert(errorCode == 0, "Error obtaining information from windows API - error code: " + errorCode);
                return level;
            }
            set
            {
                CheckDisposed();
                int errorCode = _sav.SetMasterVolume(value, Guid.Empty);
                Debug.Assert(errorCode == 0, "Error setting information using windows API - error code: " + errorCode);
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Volume)));
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1804", Justification = "Members 'not used' are used to assert their values, but only in Debug-builds for performance benefits")]
        public string IconPath
        {
            get
            {
                CheckDisposed();
                string s;
                int errorCode = _ctl.GetIconPath(out s);
                Debug.Assert(errorCode == 0, "Error obtaining information from windows API - error code: " + errorCode);
                return s;
            }
            set
            {
                CheckDisposed();
                string s;
                int errorCode = _ctl.GetIconPath(out s);
                Debug.Assert(errorCode == 0, "Error obtaining information from windows API - error code: " + errorCode);
                if (s != value)
                {
                    errorCode = _ctl.SetIconPath(value, Guid.Empty);
                    Debug.Assert(errorCode == 0, "Error setting information using windows API - error code: " + errorCode);
                }
            }
        }

        private void CheckDisposed()
        {
            if (_ctl == null)
                throw new ObjectDisposedException("Control");
            if (_sav == null)
                throw new ObjectDisposedException("SimpleAudioVolume");
        }

        public override string ToString()
        {
            string s = DisplayName;
            if (!string.IsNullOrEmpty(s))
                return "DisplayName: " + s;

            if (Process != null)
                return "Process: " + Process.ProcessName;

            return "Pid: " + ProcessId;
        }

        public void Dispose()
        {
            if (_ctl != null)
            {
                Marshal.ReleaseComObject(_ctl);
                _ctl = null;
            }

            _process?.Dispose();

            GC.SuppressFinalize(this);
        }
    }

    public sealed class AudioDevice
    {
        internal AudioDevice(string id, AudioDeviceStates state, IDictionary<string, object> properties)
        {
            Id = id;
            State = state;
            Properties = properties;
        }

        public string Id { get; private set; }
        public AudioDeviceStates State { get; private set; }
        public IDictionary<string, object> Properties { get; private set; }

        public string Description
        {
            get
            {
                const string PKEY_Device_DeviceDesc = "{a45c254e-df1c-4efd-8020-67d146a850e0} 2";
                object value;
                Properties.TryGetValue(PKEY_Device_DeviceDesc, out value);
                return string.Format(CultureInfo.InvariantCulture, "{0}", value);
            }
        }

        public string ContainerId
        {
            get
            {
                const string PKEY_Devices_ContainerId = "{8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c} 2";
                object value;
                Properties.TryGetValue(PKEY_Devices_ContainerId, out value);
                return string.Format(CultureInfo.InvariantCulture, "{0}", value);
            }
        }

        public string EnumeratorName
        {
            get
            {
                const string PKEY_Device_EnumeratorName = "{a45c254e-df1c-4efd-8020-67d146a850e0} 24";
                object value;
                Properties.TryGetValue(PKEY_Device_EnumeratorName, out value);
                return string.Format(CultureInfo.InvariantCulture, "{0}", value);
            }
        }

        public string InterfaceFriendlyName
        {
            get
            {
                const string DEVPKEY_DeviceInterface_FriendlyName = "{026e516e-b814-414b-83cd-856d6fef4822} 2";
                object value;
                Properties.TryGetValue(DEVPKEY_DeviceInterface_FriendlyName, out value);
                return string.Format(CultureInfo.InvariantCulture, "{0}", value);
            }
        }

        public string FriendlyName
        {
            get
            {
                const string DEVPKEY_Device_FriendlyName = "{a45c254e-df1c-4efd-8020-67d146a850e0} 14";
                object value;
                Properties.TryGetValue(DEVPKEY_Device_FriendlyName, out value);
                return string.Format(CultureInfo.InvariantCulture, "{0}", value);
            }
        }

        public override string ToString()
        {
            return FriendlyName;
        }
    }

    public enum AudioSessionState
    {
        Inactive = 0,
        Active = 1,
        Expired = 2
    }

    [Flags]
    public enum AudioDeviceStates
    {
        None = 0x0,
        Active = 0x1,
        Disabled = 0x2,
        NotPresent = 0x4,
        Unplugged = 0x8,
    }

    public enum AudioSessionDisconnectReason
    {
        DisconnectReasonDeviceRemoval = 0,
        DisconnectReasonServerShutdown = 1,
        DisconnectReasonFormatChanged = 2,
        DisconnectReasonSessionLogOff = 3,
        DisconnectReasonSessionDisconnected = 4,
        DisconnectReasonExclusiveModeOverride = 5
    }
}
