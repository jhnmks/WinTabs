using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace wintabs.Utilities
{
    internal static class InstanceShutdown
    {
        public static Mutex? TryAcquireInstance(string name = @"Local\WinTabs.Instance")
        {
            try
            {
                var mutex = new Mutex(initiallyOwned: false, name, out bool created);
                if (created)
                    return mutex;
                mutex.Dispose();
            }
            catch (UnauthorizedAccessException)
            {
                // An elevated copy can hold the mutex without granting this process access.
            }
            return null;
        }
        public static EventWaitHandle CreateSignal(string executable) => new(false, EventResetMode.AutoReset,
            @"Local\WinTabs.Stop." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                Path.GetFullPath(executable).ToUpperInvariant()))));

        public static int StopInstalledCopy(string executable)
        {
            try
            {
                using var signal = CreateSignal(executable);
                signal.Set();
                var deadline = Environment.TickCount64 + 5000;
                var processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable));
                try
                {
                    foreach (var process in processes)
                    {
                        if (process.Id == Environment.ProcessId || process.HasExited)
                            continue;
                        if (!string.Equals(process.MainModule?.FileName, executable, StringComparison.OrdinalIgnoreCase))
                            continue;
                        if (!process.WaitForExit((int)Math.Max(0, deadline - Environment.TickCount64)))
                            return 1;
                    }
                    return 0;
                }
                finally
                {
                    foreach (var process in processes)
                        process.Dispose();
                }
            }
            catch (Exception e) when (e is UnauthorizedAccessException or IOException or WaitHandleCannotBeOpenedException or Win32Exception or InvalidOperationException)
            {
                return 1;
            }
        }

        public static int RestartAfterExit(string executable, int pid, long startTime)
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                if (process.StartTime.ToUniversalTime().Ticks == startTime && !process.WaitForExit(10000))
                    return 1;
            }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            catch (Win32Exception) { return 1; }

            try
            {
                using var restarted = Process.Start(new ProcessStartInfo(executable) { UseShellExecute = false });
                return restarted != null ? 0 : 1;
            }
            catch (Win32Exception) { return 1; }
        }
    }
}
