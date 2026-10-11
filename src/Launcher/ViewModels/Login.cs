using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Launcher.Helpers;
using Launcher.Models;
using Launcher.Services;

using NLog;

namespace Launcher.ViewModels;

public partial class Login : Popup
{
    private readonly Server _server;
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    [ObservableProperty]
    private string? warning;

    [Required]
    [ObservableProperty]
    [NotifyDataErrorInfo]
    private string username = string.Empty;

    [Required]
    [ObservableProperty]
    [NotifyDataErrorInfo]
    private string password = string.Empty;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    private bool rememberUsername;

    [ObservableProperty]
    private bool rememberPassword;

    public bool AutoFocusUsername => string.IsNullOrEmpty(Username);
    public bool AutoFocusPassword => !string.IsNullOrEmpty(Username) && string.IsNullOrEmpty(Password);

    public Login(Server server)
    {
        _server = server;

        AddSecureWarning();

        // Load saved credentials based on user's preferences
        RememberUsername = _server.Info.RememberUsername;
        RememberPassword = _server.Info.RememberPassword;
        Username = RememberUsername ? _server.Info.Username ?? string.Empty : string.Empty;

        // Move any legacy plaintext password into the OS secret store, then load it back.
        MigrateLegacyPassword();
        Password = RememberPassword ? CredentialHelper.GetPassword(_server.Info) ?? string.Empty : string.Empty;

        View = new Views.Login
        {
            DataContext = this
        };
    }

    // Handles changes to the "Remember Username" checkbox
    partial void OnRememberUsernameChanged(bool value)
    {
        _server.Info.RememberUsername = value;

        if (!value)
            _server.Info.Username = null;

        Settings.Instance.Save();
    }

    // Handles changes to the "Remember Password" checkbox
    partial void OnRememberPasswordChanged(bool value)
    {
        _server.Info.RememberPassword = value;

        if (!value)
            CredentialHelper.Clear(_server.Info);

        Settings.Instance.Save();
    }

    [RelayCommand]
    public void Register()
    {
        App.ShowPopup(new Register(_server));
    }

    public override async Task<bool> ProcessAsync()
    {
        try
        {
            ProgressDescription = App.GetText("Text.Login.Loading");

            using var httpClient = HttpHelper.CreateHttpClient();

            var loginRequest = new LoginRequest
            {
                Username = Username,
                Password = Password
            };

            var baseUri = new Uri(_server.Info.WebApiUrl);

            var loginUri = new Uri(baseUri, "login");

            // Send login request to the API
            var httpResponse = await httpClient.PostAsJsonAsync(loginUri, loginRequest);

            if (httpResponse.StatusCode == HttpStatusCode.Unauthorized)
            {
                App.AddNotification(App.GetText("Text.Login.Unauthorized"), true);

                Password = string.Empty; // Clear password field on failure

                return false;
            }

            if (!httpResponse.IsSuccessStatusCode)
            {
                App.AddNotification("Login failed. Please check your username and password and try again", true);

                _logger.Warn("Login failed for server: '{Name}'. API returned {StatusCode}: {Reason}.", _server.Info.Name, httpResponse.StatusCode, httpResponse.ReasonPhrase);

                return false;
            }

            var loginResponse = await httpResponse.Content.ReadFromJsonAsync<LoginResponse>();

            if (loginResponse == null || string.IsNullOrEmpty(loginResponse.SessionId))
            {
                App.AddNotification("Login failed. Please check your username and password and try again.", true);

                _logger.Warn("Invalid login API response from server: '{Name}'. Response body was null or SessionId was missing.", _server.Info.Name);

                return false;
            }

            SaveRememberedCredentials();

            await ContinueAfterLoginAsync(loginResponse);

            return true;
        }
        catch (Exception ex)
        {
            App.AddNotification("Login failed. Please check your username and password and try again", true);

            _logger.Error(ex, "An exception occurred logging into server: '{Name}'.", _server.Info.Name);

            return false;
        }
    }

    /// <summary>
    /// Servers that support it let the player pick a character here rather than in game.
    /// All of it is optional: if the server has not implemented it, or refuses the
    /// selection, the client is launched exactly as it always was.
    /// See docs/character-select-protocol.md.
    /// </summary>
    private async Task ContinueAfterLoginAsync(LoginResponse loginResponse)
    {
        var characters = await CharacterApi.TryGetCharactersAsync(
            _server.Info.WebApiUrl, loginResponse.SessionId);

        // No support, or an account with no characters yet - launch into the in-game flow,
        // which is what sends a new player to character creation.
        if (characters is null || characters.Count == 0)
        {
            await LaunchClientAsync(loginResponse.SessionId, loginResponse.LaunchArguments);

            return;
        }

        // The client captured these on previous logins and never sent them. Do it in the
        // background: a portrait is not worth making the player wait.
        _ = Task.Run(() => PortraitSync.SyncAsync(_server.Info, loginResponse.SessionId, characters));

        var characterSelect = new CharacterSelect(
            _server,
            loginResponse.SessionId,
            characters,
            launch => LaunchClientAsync(loginResponse.SessionId, loginResponse.LaunchArguments, launch));

        // This login popup is still marked in progress, and ShowPopup refuses to replace
        // a popup that is. Queue the swap so it runs once this one has closed.
        Dispatcher.UIThread.Post(() => App.ShowPopup(characterSelect));
    }

    private void AddSecureWarning()
    {
        if (Uri.TryCreate(_server.Info.WebApiUrl, UriKind.Absolute, out var webApiUrl)
            && webApiUrl.Scheme != Uri.UriSchemeHttps)
        {
            Warning = App.GetText("Text.Server.SecureApiWarning");
        }
    }

    private void SaveRememberedCredentials()
    {
        _server.Info.Username = RememberUsername && !string.IsNullOrEmpty(Username) ? Username : null;

        if (RememberPassword && !string.IsNullOrEmpty(Password))
            CredentialHelper.SavePassword(_server.Info, Password);
        else
            CredentialHelper.Clear(_server.Info);

        Settings.Instance.Save();
    }

    private void MigrateLegacyPassword()
    {
        var legacyPassword = _server.Info.LegacyPassword;

        if (string.IsNullOrEmpty(legacyPassword))
            return;

        if (_server.Info.RememberPassword)
            CredentialHelper.SavePassword(_server.Info, legacyPassword);

        _server.Info.LegacyPassword = null;
        Settings.Instance.Save();
    }

    private async Task LaunchClientAsync(string sessionId, string? serverArguments, CharacterLaunchResponse? launch = null)
    {
        if (!Dx9Helper.IsInstalled())
        {
            await NotifyDirectX9MissingAsync();
            return;
        }

        var launcherArguments = new List<string>
        {
            $"Server={launch?.Server ?? _server.Info.LoginServer}",
            $"Internationalization:Locale={Settings.Instance.Locale}"
        };

        if (launch is not null)
        {
            // The retail path: the client connects straight in as this character rather
            // than showing its own select screen.
            launcherArguments.Add($"Guid={launch.Guid}");
            launcherArguments.Add($"Ticket={launch.Ticket}");
        }
        else
        {
            launcherArguments.Add($"SessionId={sessionId}");
        }

        if (!string.IsNullOrEmpty(serverArguments))
        {
            // A server that already launches straight into a character sends its own
            // Server/Guid/Ticket in LaunchArguments, for the character it picked. Those
            // would land after ours and override the player's choice, so drop them and
            // keep everything else the server asked for.
            launcherArguments.Add(launch is null
                ? serverArguments
                : StripLaunchOverrides(serverArguments));
        }

        var arguments = string.Join(' ', launcherArguments);
        var workingDirectory = InstallPaths.GetClientPath(_server.Info);
        var executablePath = Path.Combine(workingDirectory, Constants.ClientExecutableName);

        if (!File.Exists(executablePath))
        {
            App.AddNotification("Unable to launch the game. The executable file could not be found.", true);

            _logger.Error("Client executable not found for server: '{Name}' at path: {Path}.", _server.Info.Name, executablePath);

            return;
        }

        // Platform-specific process startup logic
        string fileName;
        string processArguments;
        var usesWine = false;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var winePath = WineHelper.GetPath();

            if (string.IsNullOrEmpty(winePath))
            {
                // Say what to do about it, in terms of this system, rather than only that
                // something is missing.
                App.AddNotification(WineHelper.DescribeHowToInstall(), true);

                return;
            }

            _logger.Info("Launching through Wine at '{Path}' ({Version}).",
                winePath, WineHelper.GetVersion(winePath));

            fileName = winePath;
            processArguments = $"{Constants.ClientExecutableName} {arguments}";
            usesWine = true;
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            fileName = executablePath;
            processArguments = arguments;
        }
        else
        {
            App.AddNotification("Unable to launch the game, your operating system is not supported.", true);

            return;
        }

        _server.Process = new Process();
        _server.Process.StartInfo.WorkingDirectory = workingDirectory;
        _server.Process.StartInfo.FileName = fileName;
        _server.Process.StartInfo.Arguments = processArguments;
        _server.Process.StartInfo.UseShellExecute = true;
        _server.WineDiagnostics = usesWine
            ? new WineProcessDiagnostics(_server.Process, sessionId, launch?.Ticket)
            : null;
        _server.Process.EnableRaisingEvents = true;
        _server.Process.Exited += _server.ClientProcessExited;

        try
        {
            _server.NoteClientLaunched();
            _server.Process.Start();
            _server.WineDiagnostics?.StartReading();
        }
        catch (Exception ex)
        {
            App.AddNotification("An error occurred while launching the game. Please try again.", true);

            _logger.Error(ex, "Failed to start the client process for server: {Name}.", _server.Info.Name);

            _server.Process?.Dispose();
            _server.Process = null;
            _server.WineDiagnostics = null;
        }
    }

    private static readonly string[] _characterLaunchKeys = ["Server", "Guid", "Ticket", "SessionId"];

    /// <summary>
    /// Removes the arguments that decide which character is loaded, leaving the rest of
    /// what the server asked for (ini file, asset delivery, TCG settings) untouched.
    /// </summary>
    private static string StripLaunchOverrides(string serverArguments)
    {
        var kept = serverArguments
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(argument =>
            {
                var separator = argument.IndexOf('=');

                if (separator <= 0)
                    return true;

                var key = argument[..separator];

                return !_characterLaunchKeys.Contains(key, StringComparer.OrdinalIgnoreCase);
            });

        return string.Join(' ', kept);
    }

    private async Task NotifyDirectX9MissingAsync()
    {
        App.AddNotification("Unable to launch the game, DirectX 9 is not available.", true);

        await Task.Delay(500);

        try
        {
            var window = App.GetWindow();

            await window.Launcher.LaunchUriAsync(new Uri(Constants.DirectXDownloadUrl));
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to open the DirectX download page automatically.");

            App.AddNotification($"""
                                 Could not open the DirectX download page.
                                 Please open this URL manually: {Constants.DirectXDownloadUrl}
                                 """, true);
        }
    }
}
