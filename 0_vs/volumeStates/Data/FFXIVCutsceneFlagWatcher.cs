using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace VolumeStates
{
    class FFXIVCutsceneFlagWatcher : IDisposable
    {
        private static class NativeMethods
        {
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

            [DllImport("kernel32.dll")]
            public static extern SafeHandle OpenProcess(int dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, int dwProcessId);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool ReadProcessMemory(SafeHandle hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, IntPtr dwSize, ref int lpNumberOfBytesRead);

            [DllImport("kernel32.dll")]
            public static extern void GetSystemInfo(out SYSTEM_INFO lpSystemInfo);

            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern UIntPtr VirtualQueryEx(SafeHandle hProcess, IntPtr lpAddress, out MEMORY_BASIC_INFORMATION lpBuffer, UIntPtr dwLength);
        }

        static long SearchBytes(byte[] haystack, byte[] needle, long startOffset, int alignment)
        {
            var len = needle.Length;
            var limit = haystack.Length - len;
            for (long i = startOffset; i <= limit; i++)
            {
                var k = 0;
                for (; k < len; k++)
                {
                    if (needle[k] != haystack[i + k])
                    {
                        i += ((i % alignment) == 0 ? alignment : (alignment - (i % alignment))) - 1;

                        break;
                    }
                }
                if (k == len) return i;
            }
            return -1;
        }

        private List<NativeMethods.MEMORY_BASIC_INFORMATION> FetchMemoryPages()
        {
            currentStatus.Report(new StatusUpdate { CurrentProcess = StatusUpdate.ProcessType.ReadingMemory, ProcessPercentage = 0f });

            List<NativeMethods.MEMORY_BASIC_INFORMATION> areas = new List<NativeMethods.MEMORY_BASIC_INFORMATION>();

            NativeMethods.SYSTEM_INFO sys_info = new NativeMethods.SYSTEM_INFO();
            NativeMethods.GetSystemInfo(out sys_info);

            IntPtr proc_min_address = sys_info.minimumApplicationAddress;
            IntPtr proc_max_address = sys_info.maximumApplicationAddress;

            long proc_min_address_l = proc_min_address.ToInt64();
            long proc_max_address_l = proc_max_address.ToInt64();

            // this will store any information we get from VirtualQueryEx()
            NativeMethods.MEMORY_BASIC_INFORMATION mem_basic_info = new NativeMethods.MEMORY_BASIC_INFORMATION();

            long startAt = proc_min_address_l;
            while (proc_min_address_l < proc_max_address_l)
            {
                currentStatus.Report(new StatusUpdate { CurrentProcess = StatusUpdate.ProcessType.ReadingMemory, ProcessPercentage = 1 - (proc_max_address_l - proc_min_address_l) / (float)(proc_max_address_l - startAt) });
                // 28 = sizeof(NativeMethods.MEMORY_BASIC_INFORMATION)
                NativeMethods.VirtualQueryEx(processHandle, proc_min_address, out mem_basic_info, new UIntPtr(48));
                int errorCode = Marshal.GetLastWin32Error();
                if (errorCode != 0)
                {
                    throw new Win32Exception(errorCode, "Unhandled win32 API error calling VirtualQueryEx()");
                }

                // if this memory chunk is accessible
                long regionSize = mem_basic_info.RegionSize.ToInt64();
                if (mem_basic_info.Protect == NativeMethods.AllocationProtectEnum.PAGE_READWRITE && mem_basic_info.State == NativeMethods.StateEnum.MEM_COMMIT)
                {
                    // memory area containing our byte usually are more than 100kb big
                    if (mem_basic_info.RegionSize.ToInt64() >= 0x10000)
                    {
                        areas.Add(mem_basic_info);
                    }
                }

                // move to the next memory chunk
                proc_min_address_l += regionSize;
                proc_min_address = new IntPtr(proc_min_address_l);
            }

            currentStatus.Report(new StatusUpdate { CurrentProcess = StatusUpdate.ProcessType.ReadingMemory, ProcessPercentage = 1.0f });

            return areas;
        }

        private bool FindNeedleInMemory(List<NativeMethods.MEMORY_BASIC_INFORMATION> areas, int alignmentHint, byte[] needle, out long relativeByteOffset, out NativeMethods.MEMORY_BASIC_INFORMATION memoryPage)
        {
            currentStatus.Report(new StatusUpdate { CurrentProcess = StatusUpdate.ProcessType.ParsingMemory, ProcessPercentage = 0.0f });

            for (int i = 0; i < areas.Count; i++)
            {
                currentStatus.Report(new StatusUpdate { CurrentProcess = StatusUpdate.ProcessType.ParsingMemory, ProcessPercentage = i / (float)areas.Count });
                var area = areas[i];
                byte[] buffer = new byte[area.RegionSize.ToInt64()];
                int bytesRead = 0;
                if (NativeMethods.ReadProcessMemory(processHandle, area.BaseAddress, buffer, area.RegionSize, ref bytesRead) == false)
                {
                    int errorCode = Marshal.GetLastWin32Error();
                    if (errorCode == 299)
                    {
                        Trace.WriteLine("Couldn't read entire memory; retrying...");
                        if (NativeMethods.ReadProcessMemory(processHandle, area.BaseAddress, buffer, area.RegionSize, ref bytesRead) == false)
                        {
                            int errorCodeSecondTry = Marshal.GetLastWin32Error();
                            if (errorCodeSecondTry == 299)
                            {
                                Trace.WriteLine("Couldn't read entire memory again... Continuing...");
                            }
                            else
                            {
                                throw new Win32Exception(errorCode, "Unhandled win32 API error calling ReadProcessMemory()");
                            }
                        }
                    }
                    else
                    {
                        throw new Win32Exception(errorCode, "Unhandled win32 API error calling ReadProcessMemory()");
                    }
                }

                long startOffset = 0;
                while (startOffset != -1)
                {
                    startOffset = SearchBytes(buffer, needle, startOffset, alignmentHint);
                    if (startOffset != -1)
                    {
                        if (buffer[startOffset - 24] == 0x02)
                        {
                            relativeByteOffset = startOffset;
                            memoryPage = area;
                            currentStatus.Report(new StatusUpdate { CurrentProcess = StatusUpdate.ProcessType.Done, ProcessPercentage = 1.0f, ReadyToWatch = true });
                            return true;
                        }
                        ++startOffset;
                    }
                }
            }
            relativeByteOffset = -1;
            memoryPage = new NativeMethods.MEMORY_BASIC_INFORMATION();

            currentStatus.Report(new StatusUpdate { CurrentProcess = StatusUpdate.ProcessType.Done, ProcessPercentage = 1.0f });
            return false;
        }

        SafeHandle processHandle = null;
        long activeRelativeByteOffset = -1;
        NativeMethods.MEMORY_BASIC_INFORMATION activeMemoryPage = new NativeMethods.MEMORY_BASIC_INFORMATION();

        public class StatusUpdate
        {
            public enum ProcessType
            {
                ConnectingToProcess = 0,
                ReadingMemory,
                ParsingMemory,
                Done,
                COUNT
            };

            public float ProcessPercentage { get; set; }
            public ProcessType CurrentProcess { get; set; }
            public bool ReadyToWatch { get; set; } = false;
        }

        IProgress<StatusUpdate> currentStatus;
        public FFXIVCutsceneFlagWatcher(IProgress<StatusUpdate> cutsceneStatus)
        {
            currentStatus = cutsceneStatus;
            currentStatus.Report(new StatusUpdate { CurrentProcess = StatusUpdate.ProcessType.ConnectingToProcess, ProcessPercentage = 0.0f });

            Process process = Process.GetProcessesByName("ffxiv_dx11")[0];
            processHandle = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_INFORMATION | NativeMethods.PROCESS_WM_READ, false, process.Id);

            // getting minimum & maximum address
            FindNeedleInMemory(FetchMemoryPages(), 8, new byte[] { 0x00, 0x00, 0x00, 0x00, 0xCC, 0xCC, 0xCC, 0x3D, 0x00 }, out activeRelativeByteOffset, out activeMemoryPage);
        }

        #region watcher
        private bool CanWatch()
        {
            return !processHandle.IsInvalid && activeRelativeByteOffset != -1;
        }

        bool isInCutscene = false;
        CancellationTokenSource tokenSource = new CancellationTokenSource();
        CancellationToken token;

        private void watchProcess(Action<bool> callbackFunction)
        {
            int bytesRead = 0;
            byte[] cutsceneEnabledMemory = { 0xff };
            if (NativeMethods.ReadProcessMemory(processHandle, new IntPtr(activeMemoryPage.BaseAddress.ToInt64() + activeRelativeByteOffset), cutsceneEnabledMemory, new IntPtr(1), ref bytesRead) == false)
            {
                Debug.Assert(false, "Reading process memory failed");
                StopWatcher();
            }
            Debug.Assert(cutsceneEnabledMemory[0] == 0x00 || cutsceneEnabledMemory[0] == 0x01, "Cutscene flag has an invariant value");
            Debug.Assert(bytesRead == 0, "Unable to read byte from designated memory");
            bool newValue = cutsceneEnabledMemory[0] == 0x01;
            if (isInCutscene != newValue)
            {
                callbackFunction.Invoke(newValue);
                isInCutscene = newValue;
            }
        }
        public bool StartWatcher(Action<bool> callbackFunction)
        {
            token = tokenSource.Token;
            if (!CanWatch())
            {
                Debug.Assert(CanWatch(), "Watcher cannot start, as no qualifying memory has been found");
                return false;
            }

            Task.Factory.StartNew(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    watchProcess(callbackFunction);
                    await Task.Delay(500);
                }
            });

            return true;
        }

        public void StopWatcher()
        {
            tokenSource.Cancel();
        }

        public void Dispose()
        {
            tokenSource.Dispose();
            GC.SuppressFinalize(this);
        }

        ~FFXIVCutsceneFlagWatcher()
        {
            Dispose();
        }
        #endregion
    }
}
