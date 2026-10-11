using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using Launcher.Models;

using NLog;

namespace Launcher.Helpers;

/// <summary>
/// Gathers what is worth reading after the game closes badly: how it exited, and the
/// tail of the client's own logs.
/// </summary>
public static class CrashReportHelper
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// A session shorter than this that ends cleanly never really got going - the player
    /// was dropped at load or login rather than choosing to quit.
    /// </summary>
    public static readonly TimeSpan ShortSessionThreshold = TimeSpan.FromSeconds(20);

    /// <summary>
    /// The client keeps its logs beside itself, one file per subsystem.
    /// </summary>
    public const string ClientLogsDirectory = "Logs";
    public const string ClientLogFile = "FreeRealms.log";

    // FreeRealms.log is tab separated, with a severity column; 2 means error.
    private const int SeverityColumn = 5;
    private const string ErrorSeverity = "2";

    public static string GetClientLogsPath(ServerInfo info)
        => Path.Combine(InstallPaths.GetClientPath(info), ClientLogsDirectory);

    public static string Build(ServerInfo info, int? exitCode, TimeSpan ranFor, string? wineReport = null)
    {
        var report = new StringBuilder();

        report.AppendLine($"Server:    {info.Name}");
        report.AppendLine($"Exit code: {DescribeExitCode(exitCode)}");
        report.AppendLine($"Ran for:   {DescribeDuration(ranFor)}");
        report.AppendLine($"Client:    {InstallPaths.GetClientPath(info)}");
        report.AppendLine();

        // Include Wine failures even when the native client never created a log.
        if (!string.IsNullOrWhiteSpace(wineReport))
        {
            report.AppendLine(wineReport);
            report.AppendLine();
        }

        var logPath = Path.Combine(GetClientLogsPath(info), ClientLogFile);

        if (!File.Exists(logPath))
        {
            report.AppendLine("No client log was found, so the game may have failed before it started.");

            return report.ToString();
        }

        var lines = ReadTail(logPath, 400);

        var errors = lines
            .Where(IsError)
            .TakeLast(12)
            .Select(Tidy)
            .ToArray();

        if (errors.Length > 0)
        {
            report.AppendLine("Last errors reported by the game:");
            report.AppendLine();

            foreach (var error in errors)
                report.AppendLine($"  {error}");

            report.AppendLine();
        }

        report.AppendLine("End of the client log:");
        report.AppendLine();

        foreach (var line in lines.TakeLast(25).Select(Tidy))
            report.AppendLine($"  {line}");

        return report.ToString();
    }

    private static bool IsError(string line)
    {
        var columns = line.Split('\t');

        return columns.Length > SeverityColumn && columns[SeverityColumn] == ErrorSeverity;
    }

    /// <summary>
    /// Drops the bookkeeping columns so the message itself is readable.
    /// </summary>
    private static string Tidy(string line)
    {
        var columns = line.Split('\t');

        if (columns.Length <= SeverityColumn + 2)
            return line.Trim();

        var time = $"{columns[0]} {columns[1]}".Trim();
        var message = string.Join(' ', columns.Skip(SeverityColumn + 2)).Trim();

        return $"{time}  {message}";
    }

    private static List<string> ReadTail(string path, int count)
    {
        try
        {
            // The game holds the log open, so share everything.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);

            var tail = new Queue<string>(count);

            while (reader.ReadLine() is { } line)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                if (tail.Count == count)
                    tail.Dequeue();

                tail.Enqueue(line);
            }

            return [.. tail];
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Could not read the client log at '{Path}'.", path);

            return [];
        }
    }

    private static string DescribeExitCode(int? exitCode)
    {
        if (exitCode is null)
            return "unknown";

        // Windows reports crashes as negative / 0xC0000005-style status codes.
        return exitCode < 0
            ? $"{exitCode} (0x{exitCode:X8})"
            : exitCode.ToString()!;
    }

    private static string DescribeDuration(TimeSpan ranFor)
    {
        if (ranFor < TimeSpan.Zero)
            return "unknown";

        if (ranFor.TotalMinutes < 1)
            return $"{ranFor.TotalSeconds:0.#} seconds";

        if (ranFor.TotalHours < 1)
            return $"{(int)ranFor.TotalMinutes}m {ranFor.Seconds}s";

        return $"{(int)ranFor.TotalHours}h {ranFor.Minutes}m";
    }
}
