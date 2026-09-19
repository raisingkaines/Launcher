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

    /// <summary>
    /// How often the server list re-pings for status and player counts.
    /// </summary>
    public static readonly TimeSpan ServerStatusRefreshInterval = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long each background picture is held before the next one fades in. Long
    /// enough not to be busy while someone reads the server list.
    /// </summary>
    /// <summary>
    /// The Discord application whose name and artwork appear in Rich Presence.
    ///
    /// This is still the upstream "OSFR Launcher" application, so Discord shows that name
    /// rather than this one. Replace it with a Free Realms Legacy application created at
    /// https://discord.com/developers/applications, and upload art there under the keys in
    /// <see cref="DiscordLargeImage"/> and <see cref="DiscordSmallImage"/>.
    /// </summary>
    public const long DiscordClientId = 1223728876199608410;

    /// <summary>Art uploaded to the Discord application, by asset key.</summary>
    public const string DiscordLargeImage = "legacy_logo";

    public const string DiscordSmallImage = "legacy_mark";

    public static readonly TimeSpan BackdropInterval = TimeSpan.FromSeconds(11);

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
