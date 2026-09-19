using System;
using System.IO;

namespace Launcher.Helpers;

public static class Constants
{
    public const string AppName = "Free Realms Legacy";

    public const string LogFile = "Launcher.log";
    public const string SettingsFile = "Launcher.xml";

    public const string SaveDirectory = "FreeRealmsLegacyLauncher";
    public const string ServersDirectory = "Servers";
    public const string ClientDirectory = "Client";

    public const string ClientExecutableName = "FreeRealms.exe";
    public const string DirectXDownloadUrl = "https://www.microsoft.com/download/details.aspx?id=35";

    public const string DiscordInviteUrl = "https://discord.gg/freerealmslegacy";
    public const string WebsiteUrl = "https://raisingkaines.com";

    public static readonly string[] DefaultServerUrls = ["https://play.raisingkaines.com"];

    public static readonly string LogsDirectory = Path.Combine(AppContext.BaseDirectory, "logs");

    /// <summary>
    /// Where the launcher keeps its own data: settings, saved credentials and keys.
    /// This always lives in the user profile, so the launcher can find its settings
    /// before it knows where the player put the game.
    /// </summary>
    public static readonly string SavePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                SaveDirectory);

    /// <summary>
    /// Where game clients go when the player has not chosen a location of their own.
    /// </summary>
    public static readonly string DefaultInstallPath = Path.Combine(SavePath, ServersDirectory);
}
