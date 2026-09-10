using System.Collections.Generic;
using System.Linq;

namespace Launcher.Models;

/// <summary>
/// Locales supported by the original game.
/// </summary>
public enum LocaleType
{
    None,
    zh_CN,  // Chinese (Simplified, China)
    de_DE,  // German (Germany)
    fr_FR,  // French (France)
    en_GB,  // English (United Kingdom)
    ja_JP,  // Japanese (Japan)
    ko_KR,  // Korean (South Korea)
    zh_TW,  // Chinese (Traditional, Taiwan)
    en_US,  // English (United States)
    es_ES,  // Spanish (Spain)
    it_IT,  // Italian (Italy)
    pt_PT,  // Portuguese (Portugal)
    ru_RU,  // Russian (Russia)
    sv_SE,  // Swedish (Sweden)
    pt_BR,  // Portuguese (Brazil)
    es_MX,  // Spanish (Mexico)
    nl_NL,  // Dutch (Netherlands)
    pl_PL,  // Polish (Poland)
    fi_FL,  // Finnish (Finland)
    da_DK,  // Danish (Denmark)
    nn_NO   // Norwegian Nynorsk (Norway)
}

public class Locale
{
    public string Name { get; set; }
    public LocaleType Type { get; set; }

    public Locale(LocaleType type, string name)
    {
        Type = type;
        Name = name;
    }

    public override string ToString()
    {
        return Name;
    }

    public static readonly List<Locale> Locales =
    [
        new Locale(LocaleType.en_US, "English (United States)"),
        new Locale(LocaleType.en_GB, "English (United Kingdom)"),
        new Locale(LocaleType.zh_CN, "Chinese (Simplified, China)"),
        new Locale(LocaleType.zh_TW, "Chinese (Traditional, Taiwan)"),
        new Locale(LocaleType.es_ES, "Spanish (Spain)"),
        new Locale(LocaleType.es_MX, "Spanish (Mexico)"),
        new Locale(LocaleType.pt_BR, "Portuguese (Brazil)"),
        new Locale(LocaleType.pt_PT, "Portuguese (Portugal)"),
        new Locale(LocaleType.de_DE, "German (Germany)"),
        new Locale(LocaleType.fr_FR, "French (France)"),
        new Locale(LocaleType.ru_RU, "Russian (Russia)"),
        new Locale(LocaleType.ja_JP, "Japanese (Japan)"),
        new Locale(LocaleType.ko_KR, "Korean (South Korea)"),
        new Locale(LocaleType.it_IT, "Italian (Italy)"),
        new Locale(LocaleType.nl_NL, "Dutch (Netherlands)"),
        new Locale(LocaleType.pl_PL, "Polish (Poland)"),
        new Locale(LocaleType.sv_SE, "Swedish (Sweden)"),
        new Locale(LocaleType.da_DK, "Danish (Denmark)"),
        new Locale(LocaleType.fi_FL, "Finnish (Finland)"),
        new Locale(LocaleType.nn_NO, "Norwegian Nynorsk (Norway)")
    ];

    public static readonly Dictionary<LocaleType, string> LocaleMap = Locales.ToDictionary(x => x.Type, x => x.Name);
}