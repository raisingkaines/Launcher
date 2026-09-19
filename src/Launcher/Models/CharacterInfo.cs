using System;
using System.Collections.Generic;
using System.Globalization;

namespace Launcher.Models;

/// <summary>
/// One character on the logged in account, as described by the server.
///
/// Everything here is produced by the server. The launcher never derives a guid or builds
/// a portrait path itself - see docs/character-select-protocol.md for why that matters.
/// </summary>
public sealed class CharacterInfo
{
    /// <summary>
    /// The player guid, as a decimal string. Passed back verbatim; never parsed or
    /// recomputed by the launcher.
    /// </summary>
    public string Guid { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Absolute URL of the character's headshot, or null when they have never played and
    /// the client has not uploaded one yet.
    /// </summary>
    public string? PortraitUrl { get; set; }

    /// <summary>
    /// Kept as a string on purpose. Servers format timestamps differently - SQLite hands
    /// back "2026-09-19 01:07:39.819852+00:00", which is not ISO 8601 - and one odd
    /// value in an optional field must never throw away the whole character list.
    /// </summary>
    public string? LastLogin { get; set; }

    public DateTimeOffset? LastLoginAt =>
        DateTimeOffset.TryParse(LastLogin, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;
}

public sealed class CharacterListResponse
{
    public List<CharacterInfo> Characters { get; set; } = [];
}

/// <summary>
/// What the server hands back once it has resolved a chosen character against the
/// session's account: where to connect, and the ticket that authorises it.
/// </summary>
public sealed class CharacterLaunchResponse
{
    public string? Server { get; set; }

    public string? Guid { get; set; }

    public string? Ticket { get; set; }
}

/// <summary>
/// The answer to a request to be sent to the in-game character creator.
/// </summary>
public sealed class CharacterCreateResponse
{
    public bool Ok { get; set; }

    /// <summary>
    /// Whether the login server can actually act on the request. False means the server
    /// accepted it but has no character-create patch installed, so the client will land
    /// on the in-game select screen instead.
    /// </summary>
    public bool HookInstalled { get; set; }
}
