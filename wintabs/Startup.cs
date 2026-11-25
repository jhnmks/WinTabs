using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows;

using wintabs.Models;
using wintabs.Utilities;
using wintabs.DllImports;

using Microsoft.Extensions.DependencyInjection;

using Serilog;
using Serilog.Events;
using System.Threading;
using System.Globalization;

namespace wintabs
{
    public static class Startup
    {
        class Arguments
        {
            public LogEventLevel LogLevel { get; init; }
            public bool HasConsole { get; init; }
        }

        private static event Action? CrashCleanup;

        private static event Action? ProgramExit;

        private static int s_restartRequested;

        private const string LogFile = "wintabs.log";

        [STAThread]
        public static int Main(string[] args)
        {
            if (Environment.ProcessPath is { } executable)
            {
                if (args is ["--stop-installed-copy"])
                    return InstanceShutdown.StopInstalledCopy(executable);
                if (args is ["--restart-after-exit", var processId, var started] &&
                    int.TryParse(processId, out int pid) && long.TryParse(started, out long startTime))
                    return InstanceShutdown.RestartAfterExit(executable, pid, startTime);
            }

            // A GUI app has no console; when started from a terminal with options, print to that terminal.
            if (args.Length != 0)
            {
                PInvoke.AttachConsole(unchecked((uint)-1)); // ATTACH_PARENT_PROCESS
            }

            if (args.Contains("--help") || args.Contains("-h"))
            {
                Console.WriteLine($@"
WinTabs - Tabbed Window Manager
=======================================

USAGE: wintabs.exe [OPTIONS]

OPTIONS:
    -h, --help                Show this help
    -v, -vv, -vvv             Verbose logging (repeat for more)
    --version                 Show version info

SHORTCUTS:
    Ctrl + Arrow              Switch to the previous / next tab
    Ctrl + Shift + Arrow      Move the tab one position
    Ctrl + Shift + 1-9        Move the tab to the end of that numbered group
    Ctrl + Win + G           Put the active tab in a new group
    Ctrl + Win + U           Unpack all groups on the current desktop
    Ctrl + Win + Arrow       Switch to the previous / next group
    Ctrl + Win + Shift + Arrow  Move the group one position
    Ctrl + Alt + Arrow        Tile the tab beside its group
    Ctrl + Alt + /            Show the shortcuts cheat sheet
");
                return 0;
            }

            if (args.Contains("--version"))
            {
                Console.WriteLine(GetFullVersionString());
                return 0;
            }

            return AppMain(args);
        }

        private static int AppMain(string[] args)
        {
            using var instance = InstanceShutdown.TryAcquireInstance();
            if (instance == null)
            {
                MessageBox.Show("Another instance of WinTabs is already running!", "WinTabs", MessageBoxButton.OK, MessageBoxImage.Error);
                return 1;
            }

            // Set the working path
            string roamingPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string fullPath = $"{roamingPath}\\wintabs";
            if (!Directory.Exists(fullPath))
            {
                Directory.CreateDirectory(fullPath);
            }
            Directory.SetCurrentDirectory(fullPath);

            // Parse command line
            var logLevel = args.Contains("-vv")
                ? LogEventLevel.Verbose
                : args.Contains("-v")
                ? LogEventLevel.Debug
                : LogEventLevel.Information;
#if DEBUG
            if (logLevel > LogEventLevel.Debug)
            {
                logLevel = LogEventLevel.Debug;
            }
#endif

            // Create logger configuration
            var logConfig = new LoggerConfiguration();

            IServiceCollection serviceCollection = new ServiceCollection();
            ConfigureServices(serviceCollection, new Arguments
            {
                LogLevel = logLevel,
                HasConsole = PInvoke.GetConsoleWindow() != IntPtr.Zero,
            });

            var provider = serviceCollection.BuildServiceProvider();
            ProgramExit += OnProgramExit;

            // Create logger
            var logger = provider.GetRequiredService<ILogger>();
            logger.Warning("Starting {Version}; PID={ProcessId}; executable={Executable}; settings directory={Directory}",
                GetFullVersionString(), Environment.ProcessId, Environment.ProcessPath, fullPath);

            ConfigureThreadPriority(logger);

            bool exitNormally = false;
            try
            {
                // Create the app
                var app = new App(provider);
                // We want this running after the one attached in App()
                AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                {
                    logger.Fatal("{Exception}", e.ExceptionObject);
                    CrashCleanup?.Invoke();
                    if (e.IsTerminating)
                    {
                        ProgramExit?.Invoke();
                    }
                };
                // Run the app
                app.InitializeComponent();
                using var shutdownSignal = InstanceShutdown.CreateSignal(Environment.ProcessPath!);
                var registration = ThreadPool.RegisterWaitForSingleObject(shutdownSignal, (_, _) =>
                    app.Dispatcher.BeginInvoke(() =>
                    {
                        logger.Information("Shutting down for uninstall");
                        app.Terminate();
                    }), null, Timeout.Infinite, executeOnlyOnce: true);
                int exitCode;
                try { exitCode = app.Run(); }
                finally { registration.Unregister(null); }
                exitNormally = true;
                logger.Information("Exiting normally with exit code={ExitCode}", exitCode);
                ProgramExit?.Invoke();
                return exitCode;
            }
            finally
            {
                // Write crash log if needed
                if (!exitNormally)
                {
                    CrashCleanup?.Invoke();
                    ProgramExit?.Invoke();
                }
            }
        }

        private static void ConfigureThreadPriority(ILogger logger)
        {
            try
            {
                Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High;
            }
            catch (Exception e)
            {
                logger.Error(e, "Failed to update process priority");
            }

            try
            {
                Process.GetCurrentProcess().PriorityBoostEnabled = true;
            }
            catch (Exception e)
            {
                logger.Error(e, "Failed to enable process priority boost");
            }

            try
            {
                Thread.CurrentThread.Priority = ThreadPriority.Highest;
            }
            catch (Exception e)
            {
                logger.Error(e, "Failed to increase main thread priority");
            }
        }

        private static void OnProgramExit()
        {
            if (Application.Current is App app && app.RestartOnClose)
            {
                if (Environment.ProcessPath is not { } executable || Interlocked.Exchange(ref s_restartRequested, 1) != 0)
                    return;
                try
                {
                    using var current = Process.GetCurrentProcess();
                    var start = new ProcessStartInfo(executable) { UseShellExecute = false };
                    start.ArgumentList.Add("--restart-after-exit");
                    start.ArgumentList.Add(current.Id.ToString(CultureInfo.InvariantCulture));
                    start.ArgumentList.Add(current.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture));
                    using var helper = Process.Start(start);
                }
                catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    Trace.TraceError("Could not restart WinTabs: {0}", e);
                }
            }
        }

        private static void ConfigureServices(IServiceCollection services, Arguments args)
        {
            ConfigureLogging(services, args);
            services.AddSingleton(_ => new LowLevelMouseHook());
            services.AddSingleton(_ => new LowLevelKeyboardHook());
        }

        private static void ConfigureLogging(IServiceCollection services, Arguments args)
        {
            // Create log file
            bool isUsingTempLogFile = false;
            string logFileName = Path.GetFullPath(LogFile);
            try
            {
                using var probe = File.Open(logFileName, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            }
            catch
            {
                // Fall back to using a temporary file
                isUsingTempLogFile = true;
                logFileName = Path.Combine(Path.GetTempPath(), $"wintabs-{Guid.NewGuid():N}.log");
            }

            // Create logger configuration
            var logConfig = new LoggerConfiguration();
#if DEBUG
            if (Debugger.IsAttached)
            {
                logConfig.WriteTo.Debug();
            }
#endif

            logConfig.WriteTo.File(
                logFileName,
                restrictedToMinimumLevel: args.LogLevel,
                fileSizeLimitBytes: 4 * 1024 * 1024,
                rollOnFileSizeLimit: true,
                retainedFileCountLimit: 5,
                buffered: false);

            var logBuffer = new CircularBuffer<string>(1000);
            var lineWriter = new LineWriter(logBuffer);

            logConfig.WriteTo.TextWriter(lineWriter, restrictedToMinimumLevel: LogEventLevel.Information);
            if (args.HasConsole)
            {
                logConfig.WriteTo.TextWriter(Console.Out);
            }

            var logger = logConfig.CreateLogger();

            services.AddSingleton<ILogger>(logger);
            services.AddSingleton<ILogFileViewer>(new LogFileViewer(logFileName, logBuffer));

            CrashCleanup += () =>
            {
                logger.Dispose();
                if (!isUsingTempLogFile)
                {
                    try
                    {
                        var dir = Path.GetDirectoryName(logFileName)!;
                        var latest = LogFileViewer.FindLatest(logFileName);
                        if (latest != null)
                            File.Copy(latest, Path.Combine(dir, $"wintabs-crash-{DateTimeOffset.Now:yyyyMMddTHHmmss}.log"), true);
                        foreach (var old in Directory.EnumerateFiles(dir, "wintabs-crash-*.log")
                            .OrderByDescending(File.GetLastWriteTimeUtc).Skip(5))
                            File.Delete(old);
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                    {
                        Trace.TraceWarning("Could not archive WinTabs crash log: {0}", e.Message);
                    }
                }
            };

            ProgramExit += () =>
            {
                logger.Dispose();
            };
        }

        private static string GetFullVersionString() => $"WinTabs v{GetVersionString()} on {Environment.OSVersion}";

        internal static string GetVersionString() =>
            Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? Assembly.GetEntryAssembly()?.GetName().Version?.ToString()
            ?? "0.0.0";
    }
}
