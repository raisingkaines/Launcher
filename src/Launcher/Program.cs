using System;
using System.IO;

using Avalonia;
using Avalonia.Logging;

using Launcher.Extensions;
using Launcher.Helpers;
using Launcher.Services;
using Launcher.ViewModels;

using NLog;
using NLog.Config;
using NLog.Targets;

using Velopack;

namespace Launcher;

internal sealed class Program
{
    [STAThread]
    internal static void Main(string[] args)
    {
        SetupNLog();

        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

        if (Settings.Instance.DiscordActivity)
        {
            DiscordService.Start();
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

        LogManager.Shutdown();
    }

    internal static AppBuilder BuildAvaloniaApp()
    {
        VelopackApp.Build().Run();

        var builder = AppBuilder.Configure<App>()
            .WithInterFont()
            .UsePlatformDetect();

#if DEBUG
        builder.LogToTrace();
#endif

        builder.LogToNLog(LogEventLevel.Error);

        return builder;
    }

    private static void SetupNLog()
    {
        var config = new LoggingConfiguration();

#if DEBUG
        var debuggerTarget = new DebuggerTarget("debugger");
        config.AddRule(LogLevel.Debug, LogLevel.Fatal, debuggerTarget);
#endif

        // Ensure logs directory exists
        var logsDir = Path.Combine(Directory.GetCurrentDirectory(), "logs");
        if (!Directory.Exists(logsDir))
        {
            Directory.CreateDirectory(logsDir);
        }

        var fileTarget = new FileTarget("file")
        {
            DeleteOldFileOnStartup = true,
            FileName = Path.Combine(logsDir, Constants.LogFile)
        };

        config.AddRule(LogLevel.Info, LogLevel.Fatal, fileTarget);

        LogManager.Configuration = config;
    }

    private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var logger = LogManager.GetCurrentClassLogger();

        if (e.ExceptionObject is Exception exception)
        {
            logger.Fatal(exception.ToString());
        }
        else
        {
            logger.Fatal("Unhandled exception of unknown type: {0}", e.ExceptionObject?.ToString() ?? "null");
        }
    }
}
