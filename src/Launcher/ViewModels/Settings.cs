using System;
using System.IO;
using System.Xml.Serialization;

using Avalonia.Collections;

using CommunityToolkit.Mvvm.ComponentModel;

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

    [ObservableProperty]
    private AvaloniaList<ServerInfo> serverInfoList = [];

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

    public void Save()
    {
        if (!XmlHelper.TrySerialize(Instance, _savePath))
        {
            _logger.Error("Failed to serialize and save settings to '{Path}'.", _savePath);
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