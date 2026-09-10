using System.Xml.Serialization;

namespace Launcher.Models;

public sealed class ClientFile
{
    [XmlAttribute("name")]
    public required string Name { get; set; }

    [XmlAttribute("size")]
    public required uint Size { get; set; }

    [XmlAttribute("hash")]
    public required ulong Hash { get; set; }
}