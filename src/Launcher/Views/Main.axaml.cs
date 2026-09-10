using System.Linq;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Launcher.Views;

public partial class Main : Window
{
    public readonly ViewModels.Main ViewModel = new();

    public Main()
    {
        DataContext = ViewModel;

        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        ViewModel.OnLoad();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (ViewModel.Servers.Any(s => s.IsDownloading))
        {
            e.Cancel = true;
            App.AddNotification(App.GetText("Text.Downloading.OnClose"), true);
        }
        else
        {
            base.OnClosing(e);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            App.CancelPopup();
            e.Handled = true;

            return;
        }

        base.OnKeyDown(e);
    }
}
