using System;
using System.Collections.Generic;

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

    public DateTimeOffset? LastLogin { get; set; }
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
