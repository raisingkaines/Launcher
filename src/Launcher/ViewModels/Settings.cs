using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Xml.Serialization;

using Avalonia.Collections;
using Avalonia.Platform.Storage;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Launcher.Helpers;
using Launcher.Models;
using Launcher.Services;

using NLog;

namespace Launcher.ViewModels;

public partial class Settings : ObservableObject
{
    private static readonly string _savePath = Path.Combine(Constants.SavePath, Constants.SettingsFile);
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

    private static readonly Lazy<Settings> _instance = new(Create());

    [ObservableProperty]
    private bool discordActivity = true;

    [ObservableProperty]
    private bool parallelDownload = true;

    [ObservableProperty]
    private int downloadThreads = Math.Max(1, MaxDownloadThreads / 2);
    public static int MaxDownloadThreads => Environment.ProcessorCount;

    [ObservableProperty]
    private LocaleType locale = LocaleType.en_US;

    /// <summary>
    /// Where the player chose to keep the game files. Empty means the default location.
    /// </summary>
    [ObservableProperty]
    private string? installPath;

    [ObservableProperty]
    private AvaloniaList<ServerInfo> serverInfoList = [];

    [ObservableProperty]
    [property: XmlIgnore]
    private string installPathStatus = string.Empty;

    [ObservableProperty]
    [property: XmlIgnore]
    private bool moveExistingFiles = true;

    [ObservableProperty]
    [property: XmlIgnore]
    private bool isChangingInstallPath;

    public event EventHandler? LocaleChanged;
    public event EventHandler? DiscordActivityChanged;

    private Settings() { }

    private static Settings Create()
    {
        if (!File.Exists(_savePath))
            return new Settings();

        if (!XmlHelper.TryDeserialize(_savePath, out Settings? settings))
        {
            _logger.Error("Failed to deserialize settings from '{Path}'.", _savePath);

            return new Settings();
        }

        return settings;
    }

    [XmlIgnore]
    public static Settings Instance => _instance.Value;

    /// <summary>
    /// The install folder as it should be shown to the player - never blank.
    /// </summary>
    [XmlIgnore]
    public string DisplayInstallPath => InstallPaths.Root;

    public void Save()
    {
        if (!XmlHelper.TrySerialize(Instance, _savePath))
        {
            _logger.Error("Failed to serialize and save settings to '{Path}'.", _savePath);
        }
    }

    /// <summary>
    /// Describes the install folder: how much is installed there and how much room is left.
    /// </summary>
    public void RefreshInstallPathStatus()
    {
        OnPropertyChanged(nameof(DisplayInstallPath));

        var root = InstallPaths.Root;
        var free = InstallPaths.GetAvailableFreeSpace(root);
        var installed = InstallPaths.GetDirectorySize(root);

        InstallPathStatus = free.HasValue
            ? $"{InstallPaths.FormatSize(installed)} installed · {InstallPaths.FormatSize(free.Value)} free"
            : $"{InstallPaths.FormatSize(installed)} installed";
    }

    /// <summary>
    /// Points the launcher at a new install folder, optionally bringing already
    /// downloaded clients along.
    /// </summary>
    public async Task SetInstallPathAsync(string? pickedPath)
    {
        if (IsChangingInstallPath)
            return;

        if (!InstallPaths.TryValidate(pickedPath, out var newRoot, out var error))
        {
            App.AddNotification(error, true);

            return;
        }

        var oldRoot = InstallPaths.Root;

        if (InstallPaths.PathsEqual(oldRoot, newRoot))
            return;

        if (IsClientRunning())
        {
            App.AddNotification("Close the game before changing the install location.", true);

            return;
        }

        IsChangingInstallPath = true;

        try
        {
            if (MoveExistingFiles)
            {
                var installed = InstallPaths.GetDirectorySize(oldRoot);
                var free = InstallPaths.GetAvailableFreeSpace(newRoot);

                if (free.HasValue && installed > free.Value)
                {
                    App.AddNotification($"""
                                         Not enough room on that drive.
                                         {InstallPaths.FormatSize(installed)} to move, {InstallPaths.FormatSize(free.Value)} free.
                                         """, true);

                    return;
                }

                var progress = new Progress<string>(message => InstallPathStatus = message);

                var moved = await InstallPaths.MoveInstalledServersAsync(
                    oldRoot, newRoot, [.. ServerInfoList], progress);

                if (moved > 0)
                    App.AddNotification($"Moved {moved} server(s) to {newRoot}.");
            }

            InstallPath = newRoot;

            Save();

            _logger.Info("Install path changed from '{Old}' to '{New}'.", oldRoot, newRoot);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to change the install path to '{Path}'.", newRoot);

            App.AddNotification($"""
                                 Could not move the game files.
                                 {ex.Message}
                                 """, true);
        }
        finally
        {
            IsChangingInstallPath = false;

            RefreshInstallPathStatus();
        }
    }

    [RelayCommand]
    private async Task OpenInstallPathAsync()
    {
        bool result;

        try
        {
            var window = App.GetWindow();

            var directoryInfo = new DirectoryInfo(InstallPaths.Root);

            directoryInfo.Create();

            result = await window.Launcher.LaunchDirectoryInfoAsync(directoryInfo);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error opening the install directory.");

            result = false;
        }

        if (!result)
            App.AddNotification("Unable to open the install folder.", true);
    }

    private static bool IsClientRunning()
    {
        try
        {
            var name = Path.GetFileNameWithoutExtension(Constants.ClientExecutableName);

            return Process.GetProcessesByName(name).Length > 0;
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Could not check for a running client.");

            return false;
        }
    }

    partial void OnLocaleChanged(LocaleType value)
        => LocaleChanged?.Invoke(this, EventArgs.Empty);

    partial void OnDiscordActivityChanged(bool value)
    {
        if (value)
            DiscordService.Start();
        else
            DiscordService.Stop();

        DiscordActivityChanged?.Invoke(this, EventArgs.Empty);
    }
}
