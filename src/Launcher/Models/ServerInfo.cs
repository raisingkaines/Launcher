using System.Xml.Serialization;

using CommunityToolkit.Mvvm.ComponentModel;

namespace Launcher.Models;

public sealed class ServerInfo : ObservableObject
{
    public required string Url { get; set; }

    private string name = string.Empty;
    public required string Name
    {
        get => name;
        set => SetProperty(ref name, value);
    }

    private string description = string.Empty;
    public required string Description
    {
        get => description;
        set => SetProperty(ref description, value);
    }

    public required string WebApiUrl { get; set; }
    public required string LoginServer { get; set; }

    /// <summary>
    /// Absolute path to this server folder. Generated when the server is added, and
    /// updated if the player moves the install location to another drive.
    /// </summary>
    public required string SavePath { get; set; }

    public string? Username { get; set; }
    public bool RememberUsername { get; set; }

    public bool RememberPassword { get; set; }

    /// <summary>
    /// Plaintext password (used by older launcher versions).
    /// Only here for deserialization and migration to the secret store.
    /// </summary>
    [XmlElement("Password")]
    public string? LegacyPassword { get; set; }
}