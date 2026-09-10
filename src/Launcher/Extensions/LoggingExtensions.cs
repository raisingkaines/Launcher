using Avalonia;
using Avalonia.Logging;

namespace Launcher.Extensions;

public static class LoggingExtensions
{
    public static AppBuilder LogToNLog(this AppBuilder builder, LogEventLevel level = LogEventLevel.Warning, params string[] areas)
    {
        Logger.Sink = new NLogSink(level, areas);

        return builder;
    }
}