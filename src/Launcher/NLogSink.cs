using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

using Avalonia.Logging;

namespace Launcher;

public class NLogSink : ILogSink
{
    private readonly LogEventLevel _level;
    private readonly HashSet<string>? _areas;
    private ConcurrentDictionary<string, NLog.Logger> _loggerCache = new();

    public NLogSink(LogEventLevel minimumLevel, IList<string>? areas = null)
    {
        _level = minimumLevel;
        _areas = areas?.Count > 0 ? [.. areas] : null;
    }

    public bool IsEnabled(LogEventLevel level, string area)
    {
        if (level < _level)
            return false;

        if (_areas == null)
            return true;

        return _areas.Contains(area);
    }

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate)
    {
        if (!IsEnabled(level, area))
            return;

        var logger = Resolve(source?.GetType(), area);
        logger.Log(LogLevelToNLogLevel(level), messageTemplate);
    }

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues)
    {
        if (!IsEnabled(level, area))
            return;

        var logger = Resolve(source?.GetType(), area);
        logger.Log(LogLevelToNLogLevel(level), messageTemplate, propertyValues);
    }


    public NLog.ILogger Resolve(Type? source, string? area)
    {
        var loggerName = source?.FullName ?? area ?? typeof(NLogSink).FullName;

        if (string.IsNullOrEmpty(loggerName))
            loggerName = typeof(NLogSink).FullName!;

        return _loggerCache.GetOrAdd(loggerName, name => NLog.LogManager.GetLogger(name));
    }

    private static NLog.LogLevel LogLevelToNLogLevel(LogEventLevel level)
    {
        return level switch
        {
            LogEventLevel.Verbose => NLog.LogLevel.Trace,
            LogEventLevel.Debug => NLog.LogLevel.Debug,
            LogEventLevel.Information => NLog.LogLevel.Info,
            LogEventLevel.Warning => NLog.LogLevel.Warn,
            LogEventLevel.Error => NLog.LogLevel.Error,
            LogEventLevel.Fatal => NLog.LogLevel.Fatal,
            _ => NLog.LogLevel.Off,
        };
    }
}
