using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Launcher.Models;

using NLog;

namespace Launcher.Helpers;

/// <summary>
/// Resolves where game clients live on disk.
///
/// The launcher's own data (settings, credentials) always stays in the user profile,
/// but the game itself can sit on any drive the player picks. <see cref="Root"/> is
/// that chosen location; each server gets its own folder underneath it.
/// </summary>
public static class InstallPaths
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// The folder new servers are installed into.
    /// </summary>
    public static string Root
    {
        get
        {
            var configured = ViewModels.Settings.Instance.InstallPath;

            if (string.IsNullOrWhiteSpace(configured))
                return Constants.DefaultInstallPath;

            try
            {
                return Path.GetFullPath(configured);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Configured install path '{Path}' is not usable, falling back to the default.", configured);

                return Constants.DefaultInstallPath;
            }
        }
    }

    /// <summary>
    /// The folder holding one server's files. <see cref="ServerInfo.SavePath"/> is
    /// normally absolute, but older settings files stored it relative to the root.
    /// </summary>
    public static string GetServerRoot(ServerInfo info)
    {
        var savePath = info.SavePath;

        if (string.IsNullOrWhiteSpace(savePath))
            return Path.Combine(Root, "Default");

        try
        {
            if (Path.IsPathRooted(savePath))
                return Path.GetFullPath(savePath);

            return Path.GetFullPath(Path.Combine(Root, savePath));
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Could not resolve save path '{Path}' for server '{Name}'.", savePath, info.Name);

            return Path.Combine(Root, "Default");
        }
    }

    /// <summary>
    /// The folder the game client itself is unpacked into.
    /// </summary>
    public static string GetClientPath(ServerInfo info)
        => Path.Combine(GetServerRoot(info), Constants.ClientDirectory);

    /// <summary>
    /// Checks that a folder the player picked can actually hold the game.
    /// </summary>
    public static bool TryValidate(string? path, out string fullPath, out string error)
    {
        fullPath = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(path))
        {
            error = "Pick a folder for the game files.";
            return false;
        }

        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception ex)
        {
            error = $"That path can't be used: {ex.Message}";
            return false;
        }

        if (!Path.IsPathRooted(fullPath))
        {
            error = @"Pick a full path, such as D:\Games\Free Realms Legacy.";
            return false;
        }

        if (File.Exists(fullPath))
        {
            error = "That is a file, not a folder.";
            return false;
        }

        try
        {
            Directory.CreateDirectory(fullPath);

            // Prove we can actually write there before committing to it.
            var probe = Path.Combine(fullPath, $".write-test-{Guid.NewGuid():N}");

            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
        }
        catch (Exception ex)
        {
            error = $"The launcher can't write to that folder: {ex.Message}";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Free space on the volume holding <paramref name="path"/>, or null when it can't be
    /// read (network shares, for instance).
    /// </summary>
    public static long? GetAvailableFreeSpace(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));

            if (string.IsNullOrEmpty(root))
                return null;

            return new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Could not read free space for '{Path}'.", path);

            return null;
        }
    }

    public static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];

        double size = bytes;
        var unit = 0;

        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return $"{size:0.#} {units[unit]}";
    }

    public static long GetDirectorySize(string path)
    {
        try
        {
            if (!Directory.Exists(path))
                return 0;

            return new DirectoryInfo(path)
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .Sum(file => file.Length);
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Could not measure '{Path}'.", path);

            return 0;
        }
    }

    /// <summary>
    /// Moves every installed server folder to <paramref name="newRoot"/> and points the
    /// settings at the new location. Servers that already sit outside the old root are
    /// left where they are.
    /// </summary>
    public static async Task<int> MoveInstalledServersAsync(
        string oldRoot,
        string newRoot,
        IReadOnlyList<ServerInfo> servers,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var moved = 0;

        foreach (var info in servers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var from = GetServerRoot(info);

            if (!Directory.Exists(from) || !IsUnder(from, oldRoot))
                continue;

            var to = Path.Combine(newRoot, Path.GetFileName(from.TrimEnd(Path.DirectorySeparatorChar)));

            if (PathsEqual(from, to))
                continue;

            if (Directory.Exists(to))
            {
                _logger.Error("Cannot move '{From}' to '{To}': the destination already exists.", from, to);

                throw new IOException($"'{to}' already exists. Move or delete it and try again.");
            }

            progress?.Report($"Moving {info.Name}...");

            await MoveDirectoryAsync(from, to, cancellationToken);

            info.SavePath = to;

            moved++;
        }

        _logger.Info("Moved {Count} server(s) from '{From}' to '{To}'.", moved, oldRoot, newRoot);

        return moved;
    }

    /// <summary>
    /// Moves a folder, falling back to copy-then-delete when the destination is on another volume.
    /// </summary>
    private static Task MoveDirectoryAsync(string from, string to, CancellationToken cancellationToken)
        => Task.Run(() =>
        {
            var parent = Path.GetDirectoryName(to);

            if (!string.IsNullOrEmpty(parent))
                Directory.CreateDirectory(parent);

            try
            {
                Directory.Move(from, to);

                return;
            }
            catch (IOException)
            {
                // Destination is on another volume - Directory.Move can't do that, so copy instead.
            }

            CopyDirectory(from, to, cancellationToken);

            Directory.Delete(from, true);
        }, cancellationToken);

    private static void CopyDirectory(string from, string to, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(to);

        foreach (var directory in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)), true);
        }
    }

    public static bool PathsEqual(string left, string right)
        => string.Equals(Normalize(left), Normalize(right), PathComparison);

    public static bool IsUnder(string path, string root)
    {
        var normalizedPath = Normalize(path);
        var normalizedRoot = Normalize(root);

        return normalizedPath.Equals(normalizedRoot, PathComparison)
            || normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, PathComparison);
    }

    private static StringComparison PathComparison
        => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static string Normalize(string path)
    {
        try
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path;
        }
    }
}
