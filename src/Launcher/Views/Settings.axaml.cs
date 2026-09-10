using Avalonia.Controls;

namespace Launcher.Views;

public partial class Settings : Window
{
    public readonly ViewModels.Settings ViewModel = ViewModels.Settings.Instance;

    public Settings()
    {
        DataContext = ViewModel;

        InitializeComponent();

        WindowStartupLocation = WindowStartupLocation.CenterOwner;
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        ViewModels.Settings.Instance.Save();
    }
}