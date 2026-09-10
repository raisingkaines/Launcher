using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;

namespace Launcher.Models;

public sealed class ClientManifest
{
    public const int ManifestVersion = 1;

    public const string FileName = $"{nameof(ClientManifest)}.xml";
    public const string SchemaName = $"{nameof(ClientManifest)}.xsd";

    [XmlAttribute("version")]
    public int Version { get; set; }

    [XmlAttribute("languages")]
    public required string LanguagesString { get; set; }
    public IEnumerable<LocaleType> Languages
    {
        get
        {
            try
            {
                return LanguagesString.Split(',').Select(x => Enum.Parse<LocaleType>(x, true));
            }
            catch
            {
            }

            return [];
        }
    }

    [XmlElement("Folder")]
    public required ClientFolder RootFolder { get; set; }
}