using System.Diagnostics;

namespace Launcher.Helpers;

public static class WineHelper
{
    public static string GetPath()
    {
        try
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = "which",
                Arguments = "wine",
                UseShellExecute = false,
                RedirectStandardOutput = true
            };

            var process = new Process
            {
                StartInfo = processStartInfo
            };

            process.Start();

            var path = process.StandardOutput.ReadLine();

            process.WaitForExit();

            if (!string.IsNullOrWhiteSpace(path))
                return path.Trim();
        }
        catch
        {
        }

        return string.Empty;
    }
}