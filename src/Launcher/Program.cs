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
        // Must be the first thing that runs. Velopack handles the install, update and
        // uninstall hooks here and then exits, so nothing else should touch disk or
        // start services before it has had its turn.
        VelopackApp.Build().Run();

        SetupNLog();

        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

        if (!Settings.Instance.MusicMuted)
            MusicService.Start();

        if (Settings.Instance.DiscordActivity)
        {
            DiscordService.Start();
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

        LogManager.Shutdown();
    }

    internal static AppBuilder BuildAvaloniaApp()
    {
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

        // Keep this in step with Constants.LogsDirectory, which is what "Open Logs" opens.
        var logsDir = Constants.LogsDirectory;
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
