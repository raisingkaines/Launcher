using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Launcher.Helpers;
using Launcher.Models;

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
        Password = RememberPassword ? _server.Info.Password ?? string.Empty : string.Empty;

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
            _server.Info.Password = null;

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

            // If login is successful, launch the client
            await LaunchClientAsync(loginResponse.SessionId, loginResponse.LaunchArguments);

            return true;
        }
        catch (Exception ex)
        {
            App.AddNotification("Login failed. Please check your username and password and try again", true);

            _logger.Error(ex, "An exception occurred logging into server: '{Name}'.", _server.Info.Name);

            return false;
        }
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
        _server.Info.Password = RememberPassword && !string.IsNullOrEmpty(Password) ? Password : null;

        Settings.Instance.Save();
    }

    private async Task LaunchClientAsync(string sessionId, string? serverArguments)
    {
        if (!Dx9Helper.IsInstalled())
        {
            await NotifyDirectX9MissingAsync();
            return;
        }

        var launcherArguments = new List<string>
        {
            $"Server={_server.Info.LoginServer}",
            $"SessionId={sessionId}",
            $"Internationalization:Locale={Settings.Instance.Locale}"
        };

        if (!string.IsNullOrEmpty(serverArguments))
            launcherArguments.Add(serverArguments);

        var arguments = string.Join(' ', launcherArguments);
        var workingDirectory = Path.Combine(Constants.SavePath, _server.Info.SavePath, "Client");
        var executablePath = Path.Combine(workingDirectory, Constants.ClientExecutableName);

        if (!File.Exists(executablePath))
        {
            App.AddNotification("Unable to launch the game. The executable file could not be found.", true);

            _logger.Error("Client executable not found for server: '{Name}' at path: {Path}.", _server.Info.Name, executablePath);

            return;
        }

        _server.Process = new Process();
        _server.Process.StartInfo.WorkingDirectory = workingDirectory;

        // Platform-specific process startup logic
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var winePath = WineHelper.GetPath();

            if (string.IsNullOrEmpty(winePath))
            {
                App.AddNotification("Unable to launch the game, wine is not installed.", true);

                return;
            }

            _server.Process.StartInfo.FileName = winePath;
            _server.Process.StartInfo.Arguments = $"{Constants.ClientExecutableName} {arguments}";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            _server.Process.StartInfo.FileName = executablePath;
            _server.Process.StartInfo.Arguments = arguments;
        }
        else
        {
            App.AddNotification("Unable to launch the game, your operating system is not supported.", true);

            return;
        }

        _server.Process.StartInfo.UseShellExecute = true;
        _server.Process.EnableRaisingEvents = true;
        _server.Process.Exited += _server.ClientProcessExited;

        try
        {
            _server.Process.Start();
        }
        catch (Exception ex)
        {
            App.AddNotification("An error occurred while launching the game. Please try again.", true);

            _logger.Error(ex, "Failed to start the client process for server: {Name}.", _server.Info.Name);
        }
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