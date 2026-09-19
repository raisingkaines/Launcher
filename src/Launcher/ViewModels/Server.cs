using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Platform.Storage;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Downloader;

using HashDepot;

using Launcher.Helpers;
using Launcher.Models;

using LiveMarkdown.Avalonia;

using NLog;

namespace Launcher.ViewModels;

public partial class Server : ObservableObject
{
    private readonly Main _main = null!;
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    private static SolidColorBrush WhiteBrush = new(Colors.White);
    private static SolidColorBrush GreenBrush = new(Color.FromRgb(35, 165, 90));
    private static SolidColorBrush YellowBrush = new(Color.FromRgb(204, 204, 0));
    private static SolidColorBrush RedBrush = new(Color.FromRgb(242, 63, 67));

    [ObservableProperty]
    private ServerInfo info = null!;

    [ObservableProperty]
    private bool isEnabled;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    private string status = App.GetText("Text.ServerStatus.Offline");

    [ObservableProperty]
    private int onlinePlayers;

    [ObservableProperty]
    private bool isOnline;

    [ObservableProperty]
    private Process? process;

    [ObservableProperty]
    private IBrush? serverStatusFill = WhiteBrush;

    [ObservableProperty]
    private bool isDownloading = false;

    [ObservableProperty]
    private ObservableStringBuilder markdownBuilder = new();

    [ObservableProperty]
    private string welcome = string.Empty;

    [ObservableProperty]
    private string subtitle = string.Empty;

    [ObservableProperty]
    private bool isNewsVisible;

    [ObservableProperty]
    private bool isInstalled;

    /// <summary>
    /// Set while the rail fills in every server status at startup, so servers that happen
    /// to be down do not greet the player with a stack of error toasts.
    /// </summary>
    public bool QuietRefresh { get; set; }

    private void Notify(string message, bool isError = false)
    {
        if (QuietRefresh)
            return;

        App.AddNotification(message, isError);
    }

    /// <summary>
    /// Greets the player by the name they logged in with, when there is one.
    /// </summary>
    public void UpdateWelcome()
    {
        Welcome = string.IsNullOrWhiteSpace(Info?.Username)
            ? App.GetText("Text.Main.WelcomeGuest")
            : App.GetText("Text.Main.Welcome") + Info.Username;

        Subtitle = FirstLine(Info?.Description) is { Length: > 0 } line ? line : Status;
    }

    private static readonly char[] NewLineChars = [(char)13, (char)10];

    /// <summary>
    /// The first sentence of the server description, short enough for the rail.
    /// </summary>
    private static string FirstLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var line = text.AsSpan();

        var end = line.IndexOfAny(NewLineChars);

        if (end >= 0)
            line = line[..end];

        var trimmed = line.Trim().ToString();

        return trimmed.Length > 42 ? string.Concat(trimmed.AsSpan(0, 41), "…") : trimmed;
    }

    [RelayCommand]
    private void ToggleNews() => IsNewsVisible = !IsNewsVisible;

    /// <summary>
    /// Whether this server's client has been downloaded, which decides whether the card
    /// offers Download or Play Now.
    /// </summary>
    public void UpdateInstalledState()
    {
        try
        {
            var executable = Path.Combine(InstallPaths.GetClientPath(Info), Constants.ClientExecutableName);

            IsInstalled = File.Exists(executable);
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Could not tell whether the client is installed for: {Name}.", Info.Name);

            IsInstalled = false;
        }
    }

    partial void OnStatusChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(Subtitle) || Subtitle == value)
            Subtitle = value;
    }

    public Server()
    {
#if DEBUG && DESIGNMODE
        if (Avalonia.Controls.Design.IsDesignMode)
        {
            var faker = new Bogus.Faker();

            Info = new ServerInfo
            {
                Url = "https://example.com",
                Name = $"{faker.Name.FirstName()}'s Server",
                Description = faker.Lorem.Paragraphs(5),
                SavePath = "Name",
                LoginServer = "127.0.0.1:20042",
                WebApiUrl = "https://example.com"
            };
        }
#endif
    }

    public Server(ServerInfo info, Main main)
    {
        Info = info;
        _main = main;
    }

    public async Task OnShowAsync()
    {
        MarkdownBuilder.Clear();
        MarkdownBuilder.Append(Info.Description);

        UpdateWelcome();
        UpdateInstalledState();

        await RefreshCommand.ExecuteAsync(null);

        UpdateWelcome();
    }

    /// <summary>
    /// When the client was started, so an exit can be judged against how long it ran.
    /// </summary>
    private DateTime _launchedAt;

    public void NoteClientLaunched() => _launchedAt = DateTime.UtcNow;

    public void ClientProcessExited(object? sender, EventArgs e)
    {
        // Read the exit code before disposing, and remember when it started.
        int? exitCode = null;

        try
        {
            exitCode = Process?.ExitCode;
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Could not read the exit code for: {Name}.", Info.Name);
        }

        var ranFor = _launchedAt == default ? TimeSpan.Zero : DateTime.UtcNow - _launchedAt;

        Process?.Dispose();

        // Exited is raised on a thread pool thread; everything below touches the UI.
        Dispatcher.UIThread.Post(() =>
        {
            Process = null;

            ReportIfClosedBadly(exitCode, ranFor);
        });
    }

    /// <summary>
    /// A non-zero exit means the game fell over. A clean exit in the first few seconds
    /// means it never finished loading - to the player both just look like it died.
    /// </summary>
    private void ReportIfClosedBadly(int? exitCode, TimeSpan ranFor)
    {
        var crashed = exitCode is not null and not 0;
        var endedEarly = exitCode == 0 && ranFor > TimeSpan.Zero && ranFor < CrashReportHelper.ShortSessionThreshold;

        if (!crashed && !endedEarly)
            return;

        _logger.Warn("Client for {Name} exited with code {ExitCode} after {Seconds:0.#}s.",
            Info.Name, exitCode, ranFor.TotalSeconds);

        App.ShowPopup(new CrashReport(Info, exitCode, ranFor, endedEarly));
    }

    [RelayCommand]
    private async Task OpenUriAsync(LinkClickedEventArgs args)
    {
        if (args.HRef is { IsAbsoluteUri: true, Scheme: "http" or "https" } url)
        {
            var window = App.GetWindow();

            await window.Launcher.LaunchUriAsync(url);
        }
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    public async Task RefreshAsync()
    {
        Status = App.GetText("Text.ServerStatus.Refreshing");

        ServerStatusFill = YellowBrush;

        if (!string.IsNullOrEmpty(Info.Url))
        {
        try
        {
                var result = await HttpHelper.GetServerManifestAsync(Info.Url);

                if (result.Result != ManifestResult.Success || result.ServerManifest is null)
                {
                    Notify($"""
                           Failed to get server info.
                           {result.Error}
                           """, true);

                    _logger.Error("Failed to get server manifest for: {Url}: {Error}.", Info.Url, result.Error);

                    switch (result.Result)
                    {
                        case ManifestResult.UnsupportedVersion:
                            ServerStatusFill = RedBrush;
                            Status = App.GetText("Text.ServerStatus.UnsupportedVersion");
                            break;

                        default:
                            ServerStatusFill = RedBrush;
                            Status = App.GetText("Text.ServerStatus.Offline");
                            break;
                    }

                    IsEnabled = false;

                    return;
                }

                var serverManifest = result.ServerManifest;

                Info.Name = serverManifest.Name;
                Info.Description = serverManifest.Description;

                Info.WebApiUrl = serverManifest.WebApiUrl;
                Info.LoginServer = serverManifest.LoginServer;

                Settings.Instance.Save();

                UpdateWelcome();
            }
            catch (Exception ex)
            {
                ServerStatusFill = RedBrush;
                Status = App.GetText("Text.ServerStatus.Offline");

                Notify("An error occurred while getting server info.", true);

                _logger.Error(ex, "An exception was thrown while getting server info for: {Url}.", Info.Url);

                IsEnabled = false;

                return;
            }
        }

        await RefreshServerStatusAsync(quiet: false);

        IsEnabled = true;
    }

    /// <summary>
    /// Asks the login server for its status and player count.
    ///
    /// This is the cheap half of a refresh - a single UDP round trip, with no HTTP call
    /// and no write to the settings file - so it is what the auto-refresh ticker uses to
    /// keep player counts current.
    /// </summary>
    public async Task RefreshServerStatusAsync(bool quiet = true)
    {
        // Before the manifest has been read we do not know where to ping, and a silent
        // refresh should not flip a server to offline over that.
        UpdateInstalledState();

        if (quiet && string.IsNullOrEmpty(Info.LoginServer))
            return;

        try
        {
            var serverStatus = await ServerStatusHelper.GetAsync(Info.LoginServer);

            IsOnline = serverStatus.IsOnline;

            if (serverStatus.IsOnline)
            {
                Status = App.GetText(serverStatus.IsLocked
                    ? "Text.ServerStatus.Locked"
                    : "Text.ServerStatus.Online");

                OnlinePlayers = serverStatus.OnlinePlayers;

                ServerStatusFill = serverStatus.IsLocked
                    ? RedBrush
                    : GreenBrush;
            }
            else
            {
                Status = App.GetText("Text.ServerStatus.Offline");

                OnlinePlayers = 0;
                ServerStatusFill = RedBrush;
            }
        }
        catch (Exception ex)
        {
            ServerStatusFill = RedBrush;
            Status = App.GetText("Text.ServerStatus.Offline");

            _logger.Error(ex, "Error refreshing server status for: '{Name}'.", Info.Name);

            if (!quiet)
                App.AddNotification("Unable to refresh server status.", true);
        }
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    public async Task DownloadAsync()
    {
        if (await EnsureClientAsync())
            App.AddNotification($"{Info.Name} is ready to play.");
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    public async Task PlayAsync()
    {
        if (Process != null)
        {
            App.AddNotification("Unable to launch, the game is already open.", true);

            _logger.Warn("Unable to launch, the game is already open for server: {Name}.", Info.Name);

            return;
        }

        if (!await EnsureClientAsync())
            return;

        if (!IsOnline)
        {
            App.AddNotification("Unable to login, the server is offline.", true);

            return;
        }

        // All checks passed, show the login popup
        App.ShowPopup(new Login(this));
    }

    /// <summary>
    /// Makes sure the client is present and current, fetching whatever is missing.
    /// Shared by Download and Play, so pressing either one leaves the server playable.
    /// </summary>
    private async Task<bool> EnsureClientAsync()
    {
        try
        {
            if (string.IsNullOrEmpty(Info.Url))
                return true;

            var clientManifest = await GetClientManifestAsync();

            if (clientManifest is null)
                return false;

            StatusMessage = App.GetText("Text.Server.VerifyClientFiles");

            if (!await VerifyClientFilesAsync(clientManifest))
                return false;

            if (!clientManifest.Languages.Contains(Settings.Instance.Locale))
            {
                var selectedLanguage = Locale.LocaleMap[Settings.Instance.Locale];
                var supportedLanguages = clientManifest.Languages.Select(l => Locale.LocaleMap[l]);

                App.AddNotification($"""
                                     The selected language "{selectedLanguage}" is not supported by this server.
                                     Please choose a supported language and try again.
                                     Supported languages:
                                     {string.Join(Environment.NewLine, supportedLanguages)}
                                     """, true);

                return false;
            }

            return true;
        }
        finally
        {
            StatusMessage = string.Empty;

            UpdateInstalledState();
        }
    }

    [RelayCommand]
    public async Task OpenFolderAsync()
    {
        bool result;

        try
        {
            var window = App.GetWindow();

            var folderPath = InstallPaths.GetServerRoot(Info);

            var directoryInfo = new DirectoryInfo(folderPath);

            result = await window.Launcher.LaunchDirectoryInfoAsync(directoryInfo);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error opening client folder directory.");

            result = false;
        }

        if (!result)
            App.AddNotification("Unable to open server directory.", true);
    }

    private async Task<ClientManifest?> GetClientManifestAsync()
    {
        if (string.IsNullOrEmpty(Info.Url))
            return null;

        try
        {
            var result = await HttpHelper.GetClientManifestAsync(Info.Url);

            if (result.Result != ManifestResult.Success || result.ClientManifest is null)
            {
                App.AddNotification($"""
                                     Failed to get client info.
                                     {result.Error}
                                     """, true);

                _logger.Error("Failed to get client manifest for: {Url}: {Error}.", Info.Url, result.Error);

                return null;
            }

            return result.ClientManifest;
        }
        catch (Exception ex)
        {
            App.AddNotification("An error occurred while getting client info.", true);

            _logger.Error(ex, "An exception was thrown while getting client info for: {Url}.", Info.Url);
        }

        return null;
    }

    private async Task<bool> VerifyClientFilesAsync(ClientManifest clientManifest)
    {
        _logger.Info("Starting verifying client files for: {Name}.", Info.Name);

        var filesToDownload = await GetFilesToDownloadAsync(clientManifest.RootFolder);

        if (filesToDownload.Count == 0)
        {
            _logger.Info("All client files are up to date.");
            return true;
        }

        IsDownloading = true;

        var failedFiles = new ConcurrentBag<string>();

        try
        {
            var filesDownloaded = 0;

            // Choose between parallel and sequential download based on settings
            if (Settings.Instance.ParallelDownload)
            {
                var numParallelDownloads = Math.Max(2, Settings.Instance.DownloadThreads);

                var parallelOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = numParallelDownloads
                };

                var servicePool = new ConcurrentBag<DownloadService>();

                for (var i = 0; i < numParallelDownloads; i++)
                    servicePool.Add(new DownloadService(CreateDownloadConfiguration()));

                try
                {
                    await Parallel.ForEachAsync(filesToDownload, parallelOptions, async (file, ct) =>
                    {
                        servicePool.TryTake(out var downloadService); // guaranteed non-null

                        try
                        {
                            if (!await DownloadFileAsync(downloadService!, file.Path, file.Name))
                                failedFiles.Add(file.Name);
                        }
                        finally
                        {
                            servicePool.Add(downloadService!);
                        }

                        filesDownloaded = Interlocked.Increment(ref filesDownloaded);

                        StatusMessage = App.GetText("Text.Server.PreparingGameFiles", filesDownloaded, filesToDownload.Count);
                    });
                }
                finally
                {
                    foreach (var downloadService in servicePool)
                        downloadService.Dispose();
                }
            }
            else
            {
                using var downloadService = new DownloadService(CreateDownloadConfiguration());

                foreach (var file in filesToDownload)
                {
                    if (!await DownloadFileAsync(downloadService, file.Path, file.Name))
                        failedFiles.Add(file.Name);

                    filesDownloaded++;

                    StatusMessage = App.GetText("Text.Server.PreparingGameFiles", filesDownloaded, filesToDownload.Count);
                }
            }
        }
        finally
        {
            IsDownloading = false;
        }

        // Report any files that failed to download
        if (!failedFiles.IsEmpty)
        {
            var message = new StringBuilder();

            message.AppendLine($"Failed to download {failedFiles.Count} file(s):");
            message.AppendLine(string.Join("\n", failedFiles.Take(10)));

            if (failedFiles.Count > 10)
                message.AppendLine($"...And {failedFiles.Count - 10} more.");

            App.AddNotification(message.ToString(), true);
        }

        _logger.Info("Finished verifying client files for: {Name}.", Info.Name);

        return failedFiles.IsEmpty;
    }

    private static DownloadConfiguration CreateDownloadConfiguration() => new()
    {
        CustomHttpClientFactory = () => HttpHelper.DownloadHttpClient,
        MaxTryAgainOnFailure = 5,

        // Parallelism happens per-file
        ChunkCount = 1,
        ParallelDownload = false,
    };

    private async Task<bool> DownloadFileAsync(DownloadService downloadService, string path, string fileName)
    {
        if (string.IsNullOrEmpty(Info.Url))
            return false;

        var downloadFilePath = Path.Combine(path, fileName);

        try
        {
            var clientFileUri = UriHelper.JoinUriPaths(Info.Url, "client", path, fileName);
            var fileDirectory = Path.Combine(InstallPaths.GetClientPath(Info), path);
            var filePath = Path.Combine(fileDirectory, fileName);

            Directory.CreateDirectory(fileDirectory);

            await using var fileStream = await downloadService.DownloadFileTaskAsync(clientFileUri);

            if (fileStream is null || fileStream.Length == 0)
            {
                _logger.Error("Failed to get client file or received empty stream: {Path}.", downloadFilePath);
                return false;
            }

            await using var writeStream = File.Create(filePath);

            await fileStream.CopyToAsync(writeStream);

            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error downloading: {Path}.", downloadFilePath);

            return false;
        }
    }

    private async Task<List<LocalFile>> GetFilesToDownloadAsync(ClientFolder rootFolder, string path = "")
    {
        var results = new List<LocalFile>();

        // Recurse into subfolders
        foreach (var folder in rootFolder.Folders)
        {
            var folderPath = Path.Combine(path, folder.Name);

            var folderResults = await GetFilesToDownloadAsync(folder, folderPath);

            results.AddRange(folderResults);
        }

        // Check files in the current folder
        foreach (var file in rootFolder.Files)
        {
            var fileDirectory = Path.Combine(InstallPaths.GetClientPath(Info), path);
            var filePath = Path.Combine(fileDirectory, file.Name);

            if (File.Exists(filePath))
            {
                try
                {
                    await using var readStream = File.OpenRead(filePath);

                    // First, check if file size matches. This is a quick check before hashing.
                    if (file.Size == readStream.Length)
                    {
                        var hash = await Task.Run(() => XXHash.Hash64(readStream));

                        // If hash also matches, the file is valid.
                        if (file.Hash == hash)
                            continue;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Could not verify hash for file: {Path}.", filePath);
                }
            }

            // If file doesn't exist, or size/hash mismatch, add it for download.
            results.Add(new LocalFile
            {
                Path = path,
                Name = file.Name
            });
        }

        return results;
    }

    partial void OnProcessChanged(Process? value)
    {
        _main.UpdateDiscordActivity();
    }
}