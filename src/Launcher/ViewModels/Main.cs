using System;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Avalonia.Collections;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Launcher.Helpers;
using Launcher.Models;
using Launcher.Services;

using NLog;

using Velopack;

namespace Launcher.ViewModels;

public partial class Main : ObservableObject
{
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    [ObservableProperty]
    private Popup? popup;

    [ObservableProperty]
    private Server? activeServer;

    [ObservableProperty]
    private string message = string.Empty;

    [ObservableProperty]
    private SemanticVersion version = App.CurrentVersion;

    public string VersionLabel => $"v{Version}";

    private readonly DispatcherTimer _statusTimer = new() { Interval = Constants.ServerStatusRefreshInterval };
    private bool _statusRefreshRunning;

    public AvaloniaList<Server> Servers { get; } = [];
    public AvaloniaList<Notification> Notifications { get; } = [];

    public Main()
    {
#if DEBUG && DESIGNMODE
        if (Avalonia.Controls.Design.IsDesignMode)
        {
            Servers.Clear();

            for (var i = 0; i < 20; i++)
                Servers.Add(new Server());
        }
#endif

        // Subscribe to changes in the server list from settings to keep the UI in sync.
        Settings.Instance.ServerInfoList.CollectionChanged += ServerInfoList_CollectionChanged;
        Settings.Instance.DiscordActivityChanged += (_, _) => UpdateDiscordActivity();
    }

    private void ServerInfoList_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewStartingIndex != -1)
        {
            var serverInfo = Settings.Instance.ServerInfoList[e.NewStartingIndex];

            Servers.Add(new Server(serverInfo, this));
        }
        else if (e.Action == NotifyCollectionChangedAction.Remove && e.OldStartingIndex != -1)
        {
            Servers.RemoveAt(e.OldStartingIndex);
            // If ActiveServer was removed, set to null
            if (ActiveServer != null && !Servers.Contains(ActiveServer))
                ActiveServer = null;
        }
    }

    public void OnLoad()
    {
        foreach (var serverInfo in Settings.Instance.ServerInfoList)
        {
            Servers.Add(new Server(serverInfo, this));
        }

        if (Settings.Instance.ServerInfoList.Count == 0)
        {
            _logger.Info("No servers found in settings. Adding default servers.");
            foreach (var defaultServerUrl in Constants.DefaultServerUrls)
            {
                _ = AddServer.TryAddServerAsync(defaultServerUrl);
            }
        }

        _ = RefreshServerStatusesAsync();

        _statusTimer.Tick += OnStatusTimerTick;
        _statusTimer.Start();

        // Players rarely press the update button, so look on every launch. This is what
        // makes a release pushed to GitHub actually reach anyone.
        _ = App.CheckForUpdatesSilentlyAsync();

        UpdateDiscordActivity();
    }

    /// <summary>
    /// Keeps the player counts in the rail honest without a full refresh: each tick is one
    /// UDP ping per server, so no HTTP calls and no settings writes.
    /// </summary>
    private async void OnStatusTimerTick(object? sender, EventArgs e)
    {
        if (_statusRefreshRunning)
            return;

        _statusRefreshRunning = true;

        try
        {
            foreach (var server in Servers.ToArray())
            {
                await server.RefreshServerStatusAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Auto refresh of server status failed.");
        }
        finally
        {
            _statusRefreshRunning = false;
        }
    }

    /// <summary>
    /// Fills in the status dot beside every server in the rail. One at a time, because a
    /// refresh writes the server details back to the settings file.
    /// </summary>
    private async Task RefreshServerStatusesAsync()
    {
        foreach (var server in Servers.ToArray())
        {
            server.QuietRefresh = true;

            try
            {
                await server.RefreshCommand.ExecuteAsync(null);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Could not refresh the status of server: {Name}.", server.Info.Name);
            }
            finally
            {
                server.QuietRefresh = false;
            }
        }
    }

    partial void OnVersionChanged(SemanticVersion value)
        => OnPropertyChanged(nameof(VersionLabel));

    public void UpdateDiscordActivity()
    {
        if (!Settings.Instance.DiscordActivity)
            return;

        var serversPlaying = Servers.Where(x => x.Process is not null).Select(x => x.Info.Name);
        var playingOn = string.Join(", ", serversPlaying);

        var details = string.IsNullOrEmpty(playingOn)
            ? App.GetText("Text.Discord.Idle")
            : App.GetText("Text.Discord.Playing");

        DiscordService.UpdateActivity(details, playingOn);
    }

    [RelayCommand]
    public Task CheckForUpdates() => App.CheckForUpdatesAsync();

    [RelayCommand]
    public void ShowSettings()
    {
        var window = App.GetWindow();

        var dialog = new Views.Settings();
        dialog.ShowDialog(window);
    }

    [RelayCommand]
    public void ShowAddServer() => App.ShowPopup(new AddServer());

    [RelayCommand]
    public Task OpenDiscordAsync() => OpenUrlAsync(Constants.DiscordInviteUrl);

    [RelayCommand]
    public Task OpenWebsiteAsync() => OpenUrlAsync(Constants.WebsiteUrl);

    private async Task OpenUrlAsync(string url)
    {
        try
        {
            var window = App.GetWindow();

            await window.Launcher.LaunchUriAsync(new Uri(url));
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error opening: {Url}.", url);

            App.AddNotification($"""
                                 Unable to open that link.
                                 {url}
                                 """, true);
        }
    }

    [RelayCommand]
    public async Task OpenLogsAsync()
    {
        bool result;

        try
        {
            var window = App.GetWindow();

            var directoryInfo = new DirectoryInfo(Constants.LogsDirectory);

            result = await window.Launcher.LaunchDirectoryInfoAsync(directoryInfo);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error opening logs directory.");

            result = false;
        }

        if (!result)
            App.AddNotification("Unable to open logs directory.", true);
    }

    [RelayCommand]
    public void DeleteServer()
    {
        if (ActiveServer == null)
            return;

        if (ActiveServer.IsDownloading)
        {
            App.AddNotification("Cannot delete server while download is in progress.", true);
            return;
        }

        // Show a confirmation dialog before deleting
        App.ShowPopup(new DeleteServer(ActiveServer.Info));
    }

    public void OnReceiveNotification(Notification notification)
    {
        // Limit the number of visible notifications
        if (Notifications.Count >= 3)
            Notifications.RemoveAt(0);

        Notifications.Add(notification);

        // Wait for a few seconds before removing the notification
        Task.Run(async () =>
        {
            await Task.Delay(3000);

            Notifications.Remove(notification);
        });
    }
}