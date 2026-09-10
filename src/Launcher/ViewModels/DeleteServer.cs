using System;
using System.IO;
using System.Threading.Tasks;

using CommunityToolkit.Mvvm.ComponentModel;

using Launcher.Helpers;
using Launcher.Models;

using NLog;

namespace Launcher.ViewModels;

public partial class DeleteServer : Popup
{
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    [ObservableProperty]
    private ServerInfo info;

    public DeleteServer(ServerInfo info)
    {
        Info = info;

        View = new Views.DeleteServer
        {
            DataContext = this
        };
    }

    public override Task<bool> ProcessAsync()
    {
        ProgressDescription = App.GetText("Text.Delete_Server.Loading");
        return OnDeleteServerAsync();
    }

    private async Task<bool> OnDeleteServerAsync()
    {
        try
        {
            // Delete the server's directory and all its contents from the file system.
            var serverDirectoryPath = Path.Combine(Constants.SavePath, Info.SavePath);
            await ForceDeleteDirectoryAsync(serverDirectoryPath);
        }
        catch (Exception ex)
        {
            // If file deletion fails, notify the user and log the error.
            _logger.Error(ex, "Error deleting server directory for: {Name}", Info.Name);

            App.AddNotification("An error occurred while deleting server.", true);

            return false;
        }

        Settings.Instance.ServerInfoList.Remove(Info);
        Settings.Instance.Save();

        return true;
    }

    private async Task ForceDeleteDirectoryAsync(string path)
    {
        if (!Directory.Exists(path))
            return;

        await Task.Run(() =>
        {
            try
            {
                var directoryInfo = new DirectoryInfo(path);

                foreach (var info in directoryInfo.GetFileSystemInfos("*", SearchOption.AllDirectories))
                {
                    info.Attributes = FileAttributes.Normal;
                }

                // Delete the directory and all its contents.
                directoryInfo.Delete(true);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to forcefully delete directory: {Path}", path);
                throw; // Re-throw the exception to be caught by the calling method.
            }
        });
    }
}