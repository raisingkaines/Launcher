using System;
using System.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
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

    /// <summary>
    /// What the interface measures at windowed, which is the size it was designed for.
    /// Full screen scales that up rather than re-laying anything out, so the proportions
    /// stay exactly as drawn.
    /// </summary>
    private double _designWidth;

    private double _designHeight;

    /// <summary>
    /// The windowed chrome, kept so full screen can strip it and put it back. The inset
    /// margin, rounded corners and shadow only make sense with a desktop behind them.
    /// </summary>
    private IBrush? _windowedBackground;

    private Thickness _windowedMargin;

    private CornerRadius _windowedCorner;

    private Thickness _windowedBorder;

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
            Background = _windowedBackground;
            WindowChrome.Margin = _windowedMargin;
            WindowChrome.CornerRadius = _windowedCorner;
            WindowChrome.BorderThickness = _windowedBorder;

            Width = _normalWidth;
            Height = _normalHeight;
            CanResize = false;
            ApplyUiScale();

            return;
        }

        CanResize = true;

        Width = double.NaN;
        Height = double.NaN;
        TransparencyLevelHint = [WindowTransparencyLevel.None];

        // Changing the hint alone does not make the surface opaque once the window is up:
        // the window itself is painted Transparent so the rounded corners can show the
        // desktop, and that is what was letting other windows through. Paint it instead.
        Background = WindowChrome.Background;
        WindowChrome.Margin = new Thickness(0);
        WindowChrome.CornerRadius = new CornerRadius(0);
        WindowChrome.BorderThickness = new Thickness(0);

        WindowState = WindowState.FullScreen;
        ApplyUiScale();
    }

    private void OnToggleFullScreen(object? sender, RoutedEventArgs e) => ToggleFullScreen();

    /// <summary>
    /// What the window actually ended up as, rather than what was asked for. The
    /// transparency hint in particular is a request: the platform reports back through
    /// ActualTransparencyLevel whether it honoured it.
    /// </summary>
    private void LogWindowState(string when)
    {
        _logger.Info(
            "window[{When}] state={State} askedTransparency={Asked} actualTransparency={Actual} " +
            "windowBg={WindowBg} chromeBg={ChromeBg} chromeMargin={Margin} bounds={W}x{H}",
            when, WindowState,
            string.Join(",", TransparencyLevelHint), ActualTransparencyLevel,
            Background, WindowChrome.Background, WindowChrome.Margin,
            Bounds.Width, Bounds.Height);
    }


    /// <summary>
    /// Scales what is below the title bar. The bar itself keeps its own size: it is
    /// chrome, and scaling it just made a chunky strip across the screen. Windowed it is left alone at 1:1, and that
    /// pass is also where the design size is learned; full screen it is pinned to that
    /// size and scaled up by whichever of width or height runs out first, so nothing is
    /// ever cropped.
    /// </summary>
    private void ApplyUiScale()
    {
        if (WindowState != WindowState.FullScreen)
        {
            StageRoot.Width = double.NaN;
            StageRoot.Height = double.NaN;
            UiScaler.LayoutTransform = null;

            // Bounds still hold the windowed layout here, which is what to scale from.
            if (StageRoot.Bounds.Width > 0 && StageRoot.Bounds.Height > 0)
            {
                _designWidth = StageRoot.Bounds.Width;
                _designHeight = StageRoot.Bounds.Height;
            }

            return;
        }

        // Never measured windowed, so there is nothing to scale from yet.
        if (_designWidth <= 0 || _designHeight <= 0)
            return;

        StageRoot.Width = _designWidth;
        StageRoot.Height = _designHeight;

        // Whatever the title bar leaves is what there is to fill; the bar keeps its
        // own size, because scaling it only made it a chunky stretched strip.
        var available = Bounds.Height - ContentRoot.RowDefinitions[0].ActualHeight;

        var scale = Math.Min(Bounds.Width / _designWidth, available / _designHeight);

        // Only ever bigger: the window cannot go below the design size anyway.
        scale = Math.Max(1, scale);

        UiScaler.LayoutTransform = new ScaleTransform(scale, scale);
    }


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
