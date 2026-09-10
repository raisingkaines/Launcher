using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Launcher.Helpers;

public static partial class Dx9Helper
{
    private static readonly string[] RequiredDlls = ["d3d9.dll", "d3dx9_31.dll"];

    public static bool IsInstalled()
    {
        try
        {
            string? systemPath = null;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                systemPath = Path.Combine(winDir, "System32");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ||
                     RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                if (string.IsNullOrEmpty(WineHelper.GetPath()))
                    return false;

                var winePrefix = Environment.GetEnvironmentVariable("WINEPREFIX") ??
                                 Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".wine");

                systemPath = Path.Combine(winePrefix, "drive_c", "windows", "system32");
            }
            else
            {
                return false;
            }

            if (!Directory.Exists(systemPath))
                return false;

            foreach (var requiredDll in RequiredDlls)
            {
                if (!File.Exists(Path.Combine(systemPath, requiredDll)))
                    return false;
            }
        }
        catch
        {
            return false;
        }

        return true;
    }
}