using System;
using System.IO;
using System.Threading.Tasks;

using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Launcher.Helpers;
using Launcher.Models;

using NLog;

namespace Launcher.ViewModels;

/// <summary>
/// Shown when the game closes with an error, or closes so quickly that it clearly never
/// finished loading.
/// </summary>
public partial class CrashReport : Popup
{
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    private readonly ServerInfo _info;

    [ObservableProperty]
    private string headline = string.Empty;

    [ObservableProperty]
    private string details = string.Empty;

    public CrashReport(ServerInfo info, int? exitCode, TimeSpan ranFor, bool endedEarly)
    {
        _info = info;

        Headline = endedEarly
            ? App.GetText("Text.Crash.EndedEarly")
            : App.GetText("Text.Crash.Crashed");

        Details = CrashReportHelper.Build(info, exitCode, ranFor);

        View = new Views.CrashReport
        {
            DataContext = this
        };
    }

    [RelayCommand]
    private async Task OpenClientLogsAsync()
    {
        bool result;

        try
        {
            var window = App.GetWindow();

            var directoryInfo = new DirectoryInfo(CrashReportHelper.GetClientLogsPath(_info));

            // Fall back to the client folder when the game never got far enough to log.
            if (!directoryInfo.Exists)
                directoryInfo = new DirectoryInfo(InstallPaths.GetClientPath(_info));

            result = directoryInfo.Exists && await window.Launcher.LaunchDirectoryInfoAsync(directoryInfo);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error opening the client log directory.");

            result = false;
        }

        if (!result)
            App.AddNotification("Unable to open the client log folder.", true);
    }

    [RelayCommand]
    private async Task CopyDetailsAsync()
    {
        try
        {
            var clipboard = App.GetWindow().Clipboard;

            if (clipboard is null)
            {
                App.AddNotification("Clipboard is not available.", true);

                return;
            }

            await clipboard.SetTextAsync(Details);

            App.AddNotification("Crash details copied.");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Could not copy the crash details to the clipboard.");

            App.AddNotification("Could not copy the crash details.", true);
        }
    }

    public override Task<bool> ProcessAsync() => Task.FromResult(true);
}
