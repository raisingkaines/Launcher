using System;
using System.Linq;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

using Launcher.Helpers;

using NLog;

namespace Launcher.Views;

public partial class Main : Window
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

    /// <summary>Wide enough for the window at a high DPI, far short of the full picture.</summary>
    private const int BackdropDecodeWidth = 1400;

    public readonly ViewModels.Main ViewModel = new();

    /// <summary>The size to come back to, captured before full screen clears it.</summary>
    private readonly double _normalWidth;

    private readonly double _normalHeight;

    public Main()
    {
        DataContext = ViewModel;

        InitializeComponent();

        // After InitializeComponent, or these read NaN: the XAML is what sets the size.
        _normalWidth = Width;
        _normalHeight = Height;
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        ViewModel.OnLoad();

        StartBackdropRotation();
    }

    protected override void OnClosed(EventArgs e)
    {
        _backdropTimer.Stop();
        _backdropTimer.Tick -= OnBackdropTick;

        base.OnClosed(e);
    }

    /// <summary>
    /// The pictures behind the launcher, in the order they cycle. The first one is what
    /// the window opens on.
    /// </summary>
    private static readonly string[] _backdrops =
    [
        "avares://Launcher/Assets/background.png",
        "avares://Launcher/Assets/backgrounds/seaside_sunset.jpg",
        "avares://Launcher/Assets/backgrounds/merryvale.jpg",
        "avares://Launcher/Assets/backgrounds/sanctuary_cherrytree.jpg",
        "avares://Launcher/Assets/backgrounds/highroad.jpg",
        "avares://Launcher/Assets/backgrounds/crossroads.jpg",
        "avares://Launcher/Assets/backgrounds/sanctuary_bigtree.jpg",
        "avares://Launcher/Assets/backgrounds/highroad_party.jpg",
        "avares://Launcher/Assets/backgrounds/sanctuary.jpg",
    ];

    private readonly DispatcherTimer _backdropTimer = new() { Interval = Constants.BackdropInterval };

    private int _backdropIndex;

    /// <summary>Which of the two layers is the one currently showing.</summary>
    private bool _backdropOnB;

    private void StartBackdropRotation()
    {
        if (_backdrops.Length < 2)
            return;

        _backdropTimer.Tick += OnBackdropTick;
        _backdropTimer.Start();
    }

    /// <summary>
    /// Fades the next picture in over the one showing. Nothing here is worth an error in
    /// front of the player: a picture that will not load is skipped and the rotation
    /// carries on with the next one.
    /// </summary>
    private void OnBackdropTick(object? sender, EventArgs e)
    {
        for (var attempt = 0; attempt < _backdrops.Length; attempt++)
        {
            _backdropIndex = (_backdropIndex + 1) % _backdrops.Length;

            var next = TryLoadBackdrop(_backdrops[_backdropIndex]);

            if (next is null)
                continue;

            var incoming = _backdropOnB ? BackdropA : BackdropB;
            var outgoing = _backdropOnB ? BackdropB : BackdropA;

            incoming.Source = next;
            incoming.Opacity = 1;
            outgoing.Opacity = 0;

            _backdropOnB = !_backdropOnB;

            return;
        }

        // Every picture failed, so there is nothing left to rotate through.
        _backdropTimer.Stop();
        _logger.Warn("No usable background pictures; stopping the rotation.");
    }

    private Bitmap? TryLoadBackdrop(string uri)
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri(uri));

            // Decoded to roughly what the window can show rather than full size, so the
            // two layers alive at once stay cheap.
            return Bitmap.DecodeToWidth(stream, BackdropDecodeWidth);
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Could not load the background '{Uri}'.", uri);

            return null;
        }
    }


    /// <summary>
    /// The window draws its own title bar, so dragging it has to be wired up by hand.
    /// </summary>
    private void OnTitleBarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (WindowState == WindowState.FullScreen)
            return;

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void OnMinimise(object? sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    /// <summary>
    /// Going full screen has to undo three things the floating window relies on.
    ///
    /// The window cannot be resized, and a window that cannot be resized cannot go full
    /// screen. It also has an explicit Width and Height, which pin it at that size even
    /// once the state changes - which is why it did not fill the screen. And its backdrop
    /// is transparent so the rounded corners and shadow can show the desktop through,
    /// which full screen renders as a see-through window.
    ///
    /// All three are restored on the way back, so it is never resizable while it has
    /// borders to drag.
    /// </summary>
    private void ToggleFullScreen()
    {
        if (WindowState == WindowState.FullScreen)
        {
            WindowState = WindowState.Normal;

            TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
            Width = _normalWidth;
            Height = _normalHeight;
            CanResize = false;

            return;
        }

        CanResize = true;

        Width = double.NaN;
        Height = double.NaN;
        TransparencyLevelHint = [WindowTransparencyLevel.None];

        WindowState = WindowState.FullScreen;
    }

    private void OnToggleFullScreen(object? sender, RoutedEventArgs e) => ToggleFullScreen();

    // Goes through Close() so the "still downloading" guard in OnClosing still applies.
    private void OnClose(object? sender, RoutedEventArgs e)
        => Close();

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
        if (e.Key == Key.F11)
        {
            ToggleFullScreen();
            e.Handled = true;

            return;
        }

        if (e.Key == Key.Escape && WindowState == WindowState.FullScreen && ViewModel.Popup is null)
        {
            ToggleFullScreen();
            e.Handled = true;

            return;
        }

        if (e.Key == Key.Escape)
        {
            App.CancelPopup();
            e.Handled = true;

            return;
        }

        base.OnKeyDown(e);
    }
}
