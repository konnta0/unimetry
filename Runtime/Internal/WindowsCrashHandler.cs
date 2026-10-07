using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace Unimetry.Internal
{
    /// <summary>
    /// Installs a Windows unhandled-exception filter for standalone players.
    /// The filter writes a crash artifact and chains to the previous filter. Editor builds do not install it.
    /// </summary>
    internal static class WindowsCrashHandler
    {
        public static void Install(
            CrashArtifactStore store,
            BreadcrumbLog breadcrumbs,
            CrashSession session,
            bool captureMinidumps,
            int maxMinidumpBytes)
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            Native.Install(store, breadcrumbs, session, captureMinidumps, maxMinidumpBytes);
#else
            _ = store;
            _ = breadcrumbs;
            _ = session;
            _ = captureMinidumps;
            _ = maxMinidumpBytes;
#endif
        }

        public static void EnsureInstalled()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            Native.EnsureInstalled();
#endif
        }

        public static void Uninstall()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            Native.Uninstall();
#endif
        }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        private static class Native
        {
            private const int ExceptionContinueSearch = 0;
            private const uint GenericWrite = 0x40000000;
            private const uint CreateAlways = 2;
            private const uint FileAttributeNormal = 0x80;
            private const uint MiniDumpNormal = 0;
            private static readonly IntPtr InvalidHandle = new IntPtr(-1);

            private static readonly ExceptionFilter FilterDelegate = OnFilter;
            private static CrashArtifactStore store;
            private static BreadcrumbLog breadcrumbs;
            private static CrashSession session;
            private static bool captureMinidumps;
            private static int maxMinidumpBytes;
            private static bool configured;
            private static int handling;
            private static IntPtr filterPtr;
            private static IntPtr previousPtr;
            private static ExceptionFilter previousFilter;
            private static IntPtr exceptionInfoBuffer;

            public static void Install(
                CrashArtifactStore crashStore,
                BreadcrumbLog crashBreadcrumbs,
                CrashSession crashSession,
                bool writeMinidumps,
                int minidumpByteLimit)
            {
                store = crashStore;
                breadcrumbs = crashBreadcrumbs;
                session = crashSession;
                captureMinidumps = writeMinidumps;
                maxMinidumpBytes = minidumpByteLimit;
                configured = crashStore != null;
                if (exceptionInfoBuffer == IntPtr.Zero)
                {
                    exceptionInfoBuffer = Marshal.AllocHGlobal(Marshal.SizeOf<MinidumpExceptionInformation>());
                }

                EnsureInstalled();
            }

            public static void EnsureInstalled()
            {
                if (!configured)
                {
                    return;
                }

                if (filterPtr == IntPtr.Zero)
                {
                    filterPtr = Marshal.GetFunctionPointerForDelegate(FilterDelegate);
                }

                var previous = SetUnhandledExceptionFilter(filterPtr);
                if (previous == filterPtr)
                {
                    return;
                }

                previousPtr = previous;
                previousFilter = previous == IntPtr.Zero
                    ? null
                    : Marshal.GetDelegateForFunctionPointer<ExceptionFilter>(previous);
            }

            public static void Uninstall()
            {
                if (filterPtr == IntPtr.Zero)
                {
                    return;
                }

                SetUnhandledExceptionFilter(previousPtr);
                filterPtr = IntPtr.Zero;
                previousPtr = IntPtr.Zero;
                previousFilter = null;
                configured = false;
                if (exceptionInfoBuffer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(exceptionInfoBuffer);
                    exceptionInfoBuffer = IntPtr.Zero;
                }
            }

            [AOT.MonoPInvokeCallback(typeof(ExceptionFilter))]
            private static int OnFilter(IntPtr exceptionPointers)
            {
                if (Interlocked.Exchange(ref handling, 1) != 0)
                {
                    return ContinueSearch(exceptionPointers);
                }

                try
                {
                    WriteArtifact(exceptionPointers);
                }
                catch (Exception)
                {
                    // The process is already dying. Keep going so the previous filter can report the crash.
                }

                return ContinueSearch(exceptionPointers);
            }

            private static int ContinueSearch(IntPtr exceptionPointers)
            {
                var chained = previousFilter;
                if (chained == null)
                {
                    return ExceptionContinueSearch;
                }

                try
                {
                    return chained(exceptionPointers);
                }
                catch (Exception)
                {
                    return ExceptionContinueSearch;
                }
            }

            private static void WriteArtifact(IntPtr exceptionPointers)
            {
                var activeStore = store;
                if (activeStore == null)
                {
                    return;
                }

                activeStore.EnsureDirectory();

                var id = CreateId();
                if (!CrashArtifactStore.IsArtifactId(id))
                {
                    return;
                }

                var info = new NativeExceptionInfo();
                var hasInfo = TryReadException(exceptionPointers, out info);
                var code = hasInfo ? info.Code : 0u;
                var message = hasInfo
                    ? CrashSignal.Describe(code, info.Address, info.ParameterCount, info.Information0, info.Information1)
                    : "Native crash";
                var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var crumbText = string.Empty;
                var activeBreadcrumbs = breadcrumbs;
                if (activeBreadcrumbs != null)
                {
                    try
                    {
                        crumbText = activeBreadcrumbs.CaptureText(nowMs);
                    }
                    catch (Exception)
                    {
                        crumbText = string.Empty;
                    }
                }

                var activeSession = session;
                var minidumpBytes = 0;
                if (captureMinidumps)
                {
                    minidumpBytes = TryWriteMinidump(activeStore.MinidumpPath(id), exceptionPointers);
                }

                var artifact = new CrashArtifact
                {
                    schema = CrashArtifactStore.SchemaVersion,
                    id = id,
                    capturedAtUnixMs = nowMs,
                    signal = CrashSignal.Hex(code),
                    exceptionType = CrashSignal.Name(code),
                    message = message,
                    threadName = GetCurrentThreadId().ToString(CultureInfo.InvariantCulture),
                    registers = hasInfo ? TryRegisters(info.Context) : string.Empty,
                    managedStack = TryManagedStack(),
                    breadcrumbs = crumbText,
                    deviceModel = activeSession == null ? string.Empty : activeSession.DeviceModel,
                    osType = activeSession == null ? string.Empty : activeSession.OsType,
                    buildId = activeSession == null ? string.Empty : activeSession.BuildId,
                    minidumpFile = minidumpBytes > 0 ? id + ".dmp" : string.Empty,
                    minidumpBytes = minidumpBytes,
                };
                activeStore.TryWrite(artifact);
            }

            private static string CreateId()
            {
                try
                {
                    return IdGenerator.CreateTraceId();
                }
                catch (Exception)
                {
                    var ticks = DateTime.UtcNow.Ticks.ToString("x16", CultureInfo.InvariantCulture);
                    var thread = GetCurrentThreadId().ToString("x8", CultureInfo.InvariantCulture);
                    var process = GetCurrentProcessId().ToString("x8", CultureInfo.InvariantCulture);
                    if (ticks.Length > 16)
                    {
                        ticks = ticks.Substring(ticks.Length - 16);
                    }

                    return ticks + thread + process;
                }
            }

            private static string TryManagedStack()
            {
                try
                {
                    return Environment.StackTrace ?? string.Empty;
                }
                catch (Exception)
                {
                    return string.Empty;
                }
            }

            private static string TryRegisters(IntPtr context)
            {
                try
                {
                    if (context == IntPtr.Zero || IntPtr.Size != 8)
                    {
                        return string.Empty;
                    }

                    var values = new long[CrashRegisters.Names.Length];
                    for (var index = 0; index < values.Length; index++)
                    {
                        values[index] = Marshal.ReadInt64(context, CrashRegisters.X64Offsets[index]);
                    }

                    return CrashRegisters.Format(values);
                }
                catch (Exception)
                {
                    return string.Empty;
                }
            }

            private static bool TryReadException(IntPtr exceptionPointers, out NativeExceptionInfo info)
            {
                info = new NativeExceptionInfo();
                if (exceptionPointers == IntPtr.Zero)
                {
                    return false;
                }

                try
                {
                    var record = Marshal.ReadIntPtr(exceptionPointers);
                    info.Context = Marshal.ReadIntPtr(exceptionPointers, IntPtr.Size);
                    if (record == IntPtr.Zero)
                    {
                        return false;
                    }

                    info.Code = (uint)Marshal.ReadInt32(record);
                    if (IntPtr.Size == 8)
                    {
                        info.Address = (ulong)Marshal.ReadInt64(record, 16);
                        info.ParameterCount = Marshal.ReadInt32(record, 24);
                        if (info.ParameterCount >= 1)
                        {
                            info.Information0 = (ulong)Marshal.ReadInt64(record, 32);
                        }

                        if (info.ParameterCount >= 2)
                        {
                            info.Information1 = (ulong)Marshal.ReadInt64(record, 40);
                        }
                    }
                    else
                    {
                        info.Address = (uint)Marshal.ReadInt32(record, 12);
                        info.ParameterCount = Marshal.ReadInt32(record, 16);
                        if (info.ParameterCount >= 1)
                        {
                            info.Information0 = (uint)Marshal.ReadInt32(record, 20);
                        }

                        if (info.ParameterCount >= 2)
                        {
                            info.Information1 = (uint)Marshal.ReadInt32(record, 24);
                        }
                    }

                    if (info.ParameterCount < 0)
                    {
                        info.ParameterCount = 0;
                    }

                    return true;
                }
                catch (Exception)
                {
                    return false;
                }
            }

            private static int TryWriteMinidump(string path, IntPtr exceptionPointers)
            {
                if (!captureMinidumps || maxMinidumpBytes <= 0 || exceptionInfoBuffer == IntPtr.Zero || string.IsNullOrEmpty(path))
                {
                    return 0;
                }

                var file = CreateFileW(
                    path,
                    GenericWrite,
                    0,
                    IntPtr.Zero,
                    CreateAlways,
                    FileAttributeNormal,
                    IntPtr.Zero);
                if (file == IntPtr.Zero || file == InvalidHandle)
                {
                    return 0;
                }

                var wrote = false;
                try
                {
                    var info = new MinidumpExceptionInformation
                    {
                        ThreadId = GetCurrentThreadId(),
                        ExceptionPointers = exceptionPointers,
                        ClientPointers = 1,
                    };
                    Marshal.StructureToPtr(info, exceptionInfoBuffer, false);
                    wrote = MiniDumpWriteDump(
                        GetCurrentProcess(),
                        GetCurrentProcessId(),
                        file,
                        MiniDumpNormal,
                        exceptionInfoBuffer,
                        IntPtr.Zero,
                        IntPtr.Zero);
                }
                catch (Exception)
                {
                    wrote = false;
                }
                finally
                {
                    CloseHandle(file);
                }

                if (!wrote)
                {
                    AtomicFile.TryDelete(path);
                    return 0;
                }

                try
                {
                    var length = new FileInfo(path).Length;
                    if (length <= 0 || length > maxMinidumpBytes)
                    {
                        AtomicFile.TryDelete(path);
                        return 0;
                    }

                    return (int)length;
                }
                catch (Exception)
                {
                    AtomicFile.TryDelete(path);
                    return 0;
                }
            }

            [UnmanagedFunctionPointer(CallingConvention.Winapi)]
            private delegate int ExceptionFilter(IntPtr exceptionPointers);

            [StructLayout(LayoutKind.Sequential)]
            private struct MinidumpExceptionInformation
            {
                public uint ThreadId;
                public IntPtr ExceptionPointers;
                public int ClientPointers;
            }

            private struct NativeExceptionInfo
            {
                public uint Code;
                public ulong Address;
                public int ParameterCount;
                public ulong Information0;
                public ulong Information1;
                public IntPtr Context;
            }

            [DllImport("kernel32.dll")]
            private static extern IntPtr SetUnhandledExceptionFilter(IntPtr topLevelExceptionFilter);

            [DllImport("kernel32.dll")]
            private static extern IntPtr GetCurrentProcess();

            [DllImport("kernel32.dll")]
            private static extern uint GetCurrentProcessId();

            [DllImport("kernel32.dll")]
            private static extern uint GetCurrentThreadId();

            [DllImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, CharSet = CharSet.Unicode)]
            private static extern IntPtr CreateFileW(
                string fileName,
                uint desiredAccess,
                uint shareMode,
                IntPtr securityAttributes,
                uint creationDisposition,
                uint flagsAndAttributes,
                IntPtr templateFile);

            [DllImport("kernel32.dll", SetLastError = true)]
            private static extern bool CloseHandle(IntPtr handle);

            [DllImport("dbghelp.dll", SetLastError = true)]
            private static extern bool MiniDumpWriteDump(
                IntPtr process,
                uint processId,
                IntPtr file,
                uint dumpType,
                IntPtr exceptionParam,
                IntPtr userStreamParam,
                IntPtr callbackParam);
        }
#endif
    }
}
