using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace VolumeStates.Windows
{
    class PositionWatcher : IDisposable
    {
        private class SafeProcessHandle : Microsoft.Win32.SafeHandles.SafeHandleZeroOrMinusOneIsInvalid
        {
            private static class NativeMethods
            {
                [DllImport("kernel32.dll", SetLastError = true)]
                [return: MarshalAs(UnmanagedType.Bool)]
                public static extern bool CloseHandle(IntPtr handle);
            }

            private SafeProcessHandle() : base(true) { }

            protected override bool ReleaseHandle()
            {
                if (!this.IsInvalid)
                {
                    if (!NativeMethods.CloseHandle(this.handle))
                        throw new System.ComponentModel.Win32Exception();
                    this.handle = IntPtr.Zero;
                }
                return true;
            }
        }

        private static class NativeMethods
        {
#pragma warning disable CA1823
            public const int PROCESS_QUERY_INFORMATION = 0x0400;
            public const int PROCESS_WM_READ = 0x0010;

            public enum AllocationProtectEnum : uint
            {
                PAGE_EXECUTE = 0x00000010,
                PAGE_EXECUTE_READ = 0x00000020,
                PAGE_EXECUTE_READWRITE = 0x00000040,
                PAGE_EXECUTE_WRITECOPY = 0x00000080,
                PAGE_NOACCESS = 0x00000001,
                PAGE_READONLY = 0x00000002,
                PAGE_READWRITE = 0x00000004,
                PAGE_WRITECOPY = 0x00000008,
                PAGE_GUARD = 0x00000100,
                PAGE_NOCACHE = 0x00000200,
                PAGE_WRITECOMBINE = 0x00000400
            }

            public enum StateEnum : uint
            {
                MEM_COMMIT = 0x1000,
                MEM_FREE = 0x10000,
                MEM_RESERVE = 0x2000
            }

            public enum TypeEnum : uint
            {
                MEM_IMAGE = 0x1000000,
                MEM_MAPPED = 0x40000,
                MEM_PRIVATE = 0x20000
            }

            public struct MEMORY_BASIC_INFORMATION
            {
                public IntPtr BaseAddress;
                public IntPtr AllocationBase;
                public AllocationProtectEnum AllocationProtect;
                public IntPtr RegionSize;
                public StateEnum State;
                public AllocationProtectEnum Protect;
                public TypeEnum Type;
            }

            public struct SYSTEM_INFO
            {
                public ushort processorArchitecture;
                ushort reserved;
                public uint pageSize;
                public IntPtr minimumApplicationAddress;
                public IntPtr maximumApplicationAddress;
                public IntPtr activeProcessorMask;
                public uint numberOfProcessors;
                public uint processorType;
                public uint allocationGranularity;
                public ushort processorLevel;
                public ushort processorRevision;
            }
#pragma warning restore CA1823

            [DllImport("kernel32.dll")]
            public static extern SafeProcessHandle OpenProcess(int dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, int dwProcessId);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool ReadProcessMemory(SafeProcessHandle hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, IntPtr dwSize, ref int lpNumberOfBytesRead);

            [DllImport("kernel32.dll")]
            public static extern void GetSystemInfo(out SYSTEM_INFO lpSystemInfo);

            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern UIntPtr VirtualQueryEx(SafeProcessHandle hProcess, IntPtr lpAddress, out MEMORY_BASIC_INFORMATION lpBuffer, UIntPtr dwLength);
        }

        public PositionWatcher()
        {
            AttachToProcess();
        }

        private Process ffxivProcess;
        private SafeProcessHandle processHandle = null;
        Action<byte[]> currentCallbackFunction = null;

        private void AttachToProcess()
        {
            Process[] ffxivProcesses = Process.GetProcessesByName("ffxiv_dx11");
            if (ffxivProcesses.Length <= 0)
            {
                ffxivProcesses = Process.GetProcessesByName("ffxiv");
            }
            if (ffxivProcesses.Length <= 0)
            {
                return;
            }

            ffxivProcess = ffxivProcesses[0];
            processHandle = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_INFORMATION | NativeMethods.PROCESS_WM_READ, false, ffxivProcess.Id);
        }

        private bool CanWatch()
        {
            return processHandle != null && !processHandle.IsInvalid;
        }

        byte[] currentPositionMemory = {
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00
        };
        CancellationTokenSource tokenSource = new CancellationTokenSource();
        CancellationToken token;

        private void watchProcess()
        {
            int bytesRead = 0;
            byte[] newPositionMemory = {
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00
            };
            if (NativeMethods.ReadProcessMemory(processHandle, new IntPtr(ffxivProcess.MainModule.BaseAddress.ToInt64() + 0x1B2C790), newPositionMemory, new IntPtr(12), ref bytesRead) == false)
            {
                Debug.Assert(false, "Reading process memory failed");
                StopWatcher();
                return;
            }
            Debug.Assert(bytesRead == 12, "Unable to read byte from designated memory");
            if (!currentPositionMemory.SequenceEqual(newPositionMemory))
            {
                currentCallbackFunction.Invoke(newPositionMemory);
                currentPositionMemory = newPositionMemory;
            }
        }
        public bool StartWatcher(Action<byte[]> callbackFunction)
        {
            currentCallbackFunction = callbackFunction;
            token = tokenSource.Token;
            if (!CanWatch())
            {
                MessageBox.Show(Properties.Resources.COULDNT_FIND_MEMORY_LOCATION_CONTENT, Properties.Resources.COULDNT_FIND_MEMORY_LOCATION_TITLE, MessageBoxButton.OK);
                return false;
            }

            Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    watchProcess();
                    await Task.Delay(16).ConfigureAwait(true);
                }
            }, token);

            return true;
        }

        public void StopWatcher()
        {
            tokenSource.Cancel();
        }

        public void Dispose()
        {
            processHandle?.Dispose();
            tokenSource.Dispose();
            GC.SuppressFinalize(this);
        }

        ~PositionWatcher()
        {
            Dispose();
        }
    }
}