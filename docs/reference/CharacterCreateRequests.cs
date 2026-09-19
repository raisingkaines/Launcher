// Reference implementation for the character-create half of the launcher protocol.
// Drop this into your login server project and call Consume() from
// CharacterSelectInfoRequestHandler - see docs/character-select-protocol.md.
//
// The launcher cannot send a player to the in-game creator on its own: the client decides
// between the creator and the select screen purely from CharacterSelectInfoReply.Status,
// which the login server sets from characters.Count == 0. So the launcher asks the server,
// and the server answers the next character-select request as though the account were
// empty. This is the "asks the server" side of that.
//
// The signal is a file rather than shared memory or a database column so that whatever
// serves your /characters/create endpoint - a separate web process, very likely - can
// raise it without talking to the login server at all.

using System;
using System.IO;

namespace Sanctuary.Login;

public static class CharacterCreateRequests
{
    /// <summary>Relative to the login server's working directory.</summary>
    private const string FlagDirectory = "character-create";

    /// <summary>
    /// A request older than this is ignored, so one left behind by a crash or an
    /// abandoned launch cannot strand an account on the creation screen.
    /// </summary>
    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Whether this account asked to be sent to character creation, consuming the request
    /// so it only applies once.
    ///
    /// Never throws. This runs in the login path for every character-select request on the
    /// server, so any failure has to mean "no" rather than a login nobody can complete.
    /// </summary>
    public static bool Consume(ulong userId)
    {
        try
        {
            if (userId == 0)
                return false;

            var flag = Path.Combine(AppContext.BaseDirectory, FlagDirectory, userId + ".flag");

            if (!File.Exists(flag))
                return false;

            var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(flag);

            // Spend it either way. An account with no characters already reaches the
            // creator on its own, and leaving the request behind would send the player
            // there a second time after they had made someone.
            TryDelete(flag);

            // A clock that has gone backwards reads as a negative age; treat that as stale
            // rather than trusting it.
            return age >= TimeSpan.Zero && age <= MaxAge;
        }
        catch
        {
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // A file we cannot delete expires on its own.
        }
    }
}
