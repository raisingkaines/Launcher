using System.Collections.Generic;
using System.Xml.Serialization;

namespace Launcher.Models;

public sealed class ClientFolder
{
    [XmlAttribute("name")]
    public required string Name { get; set; }

    [XmlElement("File")]
    public List<ClientFile> Files { get; set; } = [];

    [XmlElement("Folder")]
    public List<ClientFolder> Folders { get; set; } = [];
}