using System.Xml.Serialization;

namespace Launcher.Models;

public sealed class ServerManifest
{
    public const int ManifestVersion = 2;

    public const string FileName = $"{nameof(ServerManifest)}.xml";
    public const string SchemaName = $"{nameof(ServerManifest)}.xsd";

    [XmlAttribute("version")]
    public int Version { get; set; }

    public required string Name { get; set; }
    public required string Description { get; set; }

    public required string WebApiUrl { get; set; }
    public required string LoginServer { get; set; }
}