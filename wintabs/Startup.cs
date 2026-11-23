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

        private const string LogFile = "wintabs.log";

        [STAThread]
        public static int Main(string[] args)
        {
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
            // Set the working path
            string roamingPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string fullPath = $"{roamingPath}\\wintabs";
            if (!Directory.Exists(fullPath))
            {
                Directory.CreateDirectory(fullPath);
            }
            Directory.SetCurrentDirectory(fullPath);

            // Check if other instances are running
            var exists = Process.GetProcessesByName(Process.GetCurrentProcess().ProcessName)
                .Where(x => x.MainWindowHandle != IntPtr.Zero)
                .Any();
            if (exists)
            {
                MessageBox.Show("Another instance of WinTabs is already running!", "WinTabs", MessageBoxButton.OK, MessageBoxImage.Error);
                return 1;
            }

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

            ProgramExit += OnProgramExit;

            var provider = serviceCollection.BuildServiceProvider();

            // Create logger
            var logger = provider.GetRequiredService<ILogger>();
            logger.Warning(GetFullVersionString());

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
                int exitCode = app.Run();
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
                var startupCommand = Environment.ProcessPath;
                if (startupCommand == null)
                {
                    return;
                }
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c timeout 1 && taskkill /f /im wintabs.exe && timeout 1 && {startupCommand}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
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
                File.WriteAllText(logFileName, "");
            }
            catch
            {
                // Fall back to using a temporary file
                isUsingTempLogFile = true;
                logFileName = Path.GetTempFileName() + ".log";
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
                buffered: true,
                flushToDiskInterval: TimeSpan.FromSeconds(5));

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
                    // Create the file with a placeholder error message indicating more severe failure
                    var crashFileName = $"wintabs-crash-{DateTimeOffset.Now:yyyyMMddTHHmmss}.log";
                    File.Copy(logFileName, crashFileName, true);
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
