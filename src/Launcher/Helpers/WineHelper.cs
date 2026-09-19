using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

using NLog;

namespace Launcher.Helpers;

/// <summary>
/// Finding Wine, so the Windows game client can run on macOS, Linux and ChromeOS.
///
/// Wine is the free, open source compatibility layer. CrossOver is a commercial packaging
/// of the same thing, and its copy is found here too for anyone who already has it.
///
/// The client is a 32-bit Windows binary, which is what actually catches people out: a
/// Wine that only handles 64-bit installs cleanly and runs everything else, then fails on
/// this one executable. Linux needs its 32-bit packages; macOS needs a Wine built to run
/// 32-bit code at all, since macOS itself dropped it.
/// </summary>
public static class WineHelper
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Where Wine lives beyond whatever is on PATH, in the order to prefer it.
    /// </summary>
    private static IEnumerable<string> CandidatePaths()
    {
        if (OperatingSystem.IsMacOS())
        {
            yield return "/usr/local/bin/wine";
            yield return "/opt/homebrew/bin/wine";

            // Wine's own app bundles.
            yield return "/Applications/Wine Stable.app/Contents/Resources/wine/bin/wine";
            yield return "/Applications/Wine Devel.app/Contents/Resources/wine/bin/wine";
            yield return "/Applications/Wine Staging.app/Contents/Resources/wine/bin/wine";

            // CrossOver ships a Wine that can still run 32-bit code on modern macOS.
            yield return "/Applications/CrossOver.app/Contents/SharedSupport/CrossOver/bin/wine";

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            if (!string.IsNullOrEmpty(home))
            {
                // Whisky keeps its own build per user.
                yield return Path.Combine(
                    home, "Library/Application Support/com.isaacmarovitz.Whisky/Libraries/Wine/bin/wine");
            }
        }
        else
        {
            yield return "/usr/bin/wine";
            yield return "/usr/local/bin/wine";
            yield return "/opt/wine-stable/bin/wine";
            yield return "/opt/wine-staging/bin/wine";
        }
    }

    /// <summary>The Wine to launch with, or empty when there is none to be found.</summary>
    public static string GetPath()
    {
        var onPath = WhichWine();

        if (!string.IsNullOrEmpty(onPath))
            return onPath;

        foreach (var candidate in CandidatePaths())
        {
            try
            {
                if (File.Exists(candidate))
                {
                    _logger.Info("Found Wine at '{Path}'.", candidate);

                    return candidate;
                }
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Could not check '{Path}'.", candidate);
            }
        }

        return string.Empty;
    }

    public static bool IsInstalled() => !string.IsNullOrEmpty(GetPath());

    /// <summary>What Wine reports itself as, for the logs. Empty if it will not even run.</summary>
    public static string GetVersion(string winePath)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = winePath,
                Arguments = "--version",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });

            if (process is null)
                return string.Empty;

            var version = process.StandardOutput.ReadLine() ?? string.Empty;

            process.WaitForExit(5000);

            return version.Trim();
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Could not ask '{Path}' for its version.", winePath);

            return string.Empty;
        }
    }

    /// <summary>
    /// What to tell someone who has no Wine, in terms of their own system. "Wine is not
    /// installed" on its own leaves them nowhere.
    /// </summary>
    public static string DescribeHowToInstall()
    {
        if (OperatingSystem.IsMacOS())
        {
            return "The game needs Wine to run on macOS. The client is 32-bit and macOS dropped "
                 + "32-bit support, so it needs a Wine built to handle that: Whisky is free, and "
                 + "CrossOver also works. Plain Wine from Homebrew is usually not enough on its own.";
        }

        if (OperatingSystem.IsLinux())
        {
            return "The game needs Wine to run on Linux, including its 32-bit support. On Debian "
                 + "and Ubuntu that is 'sudo dpkg --add-architecture i386', then "
                 + "'sudo apt install wine wine32'. On a Chromebook, run the same inside the Linux "
                 + "container.";
        }

        return "The game needs Wine to run on this system.";
    }

    private static string WhichWine()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "which",
                Arguments = "wine",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });

            if (process is null)
                return string.Empty;

            var path = process.StandardOutput.ReadLine();

            process.WaitForExit(5000);

            return string.IsNullOrWhiteSpace(path) ? string.Empty : path.Trim();
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Could not run 'which wine'.");

            return string.Empty;
        }
    }
}
