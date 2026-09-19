using System;
using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

using Launcher.Helpers;

using NLog;

namespace Launcher.Views;

public partial class Settings : Window
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

    public readonly ViewModels.Settings ViewModel = ViewModels.Settings.Instance;

    public Settings()
    {
        DataContext = ViewModel;

        InitializeComponent();

        WindowStartupLocation = WindowStartupLocation.CenterOwner;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        ViewModel.RefreshInstallPathStatus();
    }

    /// <summary>
    /// Asks the player where the game should live, using the operating system's own
    /// folder picker so any drive is fair game.
    /// </summary>
    private async void OnBrowseInstallPath(object? sender, RoutedEventArgs e)
    {
        try
        {
            var startLocation = await GetStartLocationAsync();

            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = App.GetText("Text.Settings.Install.PickerTitle"),
                AllowMultiple = false,
                SuggestedStartLocation = startLocation
            });

            if (folders.Count == 0)
                return;

            var picked = folders[0].TryGetLocalPath();

            if (string.IsNullOrWhiteSpace(picked))
            {
                App.AddNotification("That location is not a folder on this computer.", true);

                return;
            }

            await ViewModel.SetInstallPathAsync(picked);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to pick an install folder.");

            App.AddNotification("Could not open the folder picker.", true);
        }
    }

    private async Task<IStorageFolder?> GetStartLocationAsync()
    {
        try
        {
            return await StorageProvider.TryGetFolderFromPathAsync(InstallPaths.Root);
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Could not resolve the current install folder for the picker.");

            return null;
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        ViewModels.Settings.Instance.Save();
    }
}
