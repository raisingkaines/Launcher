using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Launcher.Models;

using NLog;

namespace Launcher.Helpers;

/// <summary>
/// Sends the game client's own headshots to the server.
///
/// The client renders a headshot of the player a few seconds after every login and writes
/// it to Client\Resources\Images\heads\&lt;guid&gt;.png at 70x70 - exactly what the server
/// wants. It only ever uploads one when the in-game portrait button asks it to, and nothing
/// in normal play does, so the file just sits there and characters show no portrait.
///
/// So the launcher sends it. Nothing in the game client changes, and the upload is
/// authorised by the session the launcher already holds.
/// </summary>
public static class PortraitSync
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

    private const string HeadsDirectory = "Resources/Images/heads";
    private const string StateFileName = "portraits.json";

    /// <summary>
    /// Uploads any headshot that has changed since it was last sent. Never throws: a
    /// portrait is not worth failing a login over.
    /// </summary>
    public static async Task SyncAsync(
        ServerInfo info, string sessionId, IReadOnlyList<CharacterInfo> characters,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (characters.Count == 0 || string.IsNullOrWhiteSpace(sessionId))
                return;

            var heads = HeadsPath(info);

            if (heads is null || !Directory.Exists(heads))
                return;

            var state = ReadState();
            var uploaded = 0;

            foreach (var character in characters)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var guid = character.Guid;

                if (string.IsNullOrWhiteSpace(guid))
                    continue;

                var file = Path.Combine(heads, guid + ".png");

                if (!File.Exists(file))
                    continue;

                // The client rewrites the file on every login, so only send it when the
                // bytes are actually newer than what the server was last given.
                var written = File.GetLastWriteTimeUtc(file).Ticks;
                var key = StateKey(info, guid);

                if (state.TryGetValue(key, out var sent) && sent >= written)
                    continue;

                if (!await CharacterApi.TryUploadPortraitAsync(
                        info.WebApiUrl, sessionId, guid, file, cancellationToken))
                {
                    // A server that will not take portraits will not take them for any
                    // character, so stop rather than retrying down the whole list.
                    return;
                }

                state[key] = written;
                uploaded++;
            }

            if (uploaded > 0)
            {
                WriteState(state);

                _logger.Info("Uploaded {Count} captured headshot(s) for '{Server}'.", uploaded, info.Name);
            }
        }
        catch (OperationCanceledException)
        {
            // The launcher is closing; nothing to do.
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Could not sync captured headshots for '{Server}'.", info.Name);
        }
    }

    /// <summary>Where the client writes the headshots it renders.</summary>
    private static string? HeadsPath(ServerInfo info)
    {
        try
        {
            return Path.Combine(InstallPaths.GetClientPath(info), HeadsDirectory.Replace('/', Path.DirectorySeparatorChar));
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Could not resolve the headshot folder for '{Server}'.", info.Name);

            return null;
        }
    }

    // Servers keep their own portraits, so the same guid on two servers is two records.
    private static string StateKey(ServerInfo info, string guid)
        => $"{info.WebApiUrl}|{guid}";

    private static string StatePath => Path.Combine(Constants.SavePath, StateFileName);

    private static Dictionary<string, long> ReadState()
    {
        try
        {
            if (!File.Exists(StatePath))
                return [];

            return JsonSerializer.Deserialize<Dictionary<string, long>>(File.ReadAllText(StatePath)) ?? [];
        }
        catch (Exception ex)
        {
            // A corrupt record only costs one extra upload.
            _logger.Warn(ex, "Could not read '{Path}'; treating every headshot as new.", StatePath);

            return [];
        }
    }

    private static void WriteState(Dictionary<string, long> state)
    {
        try
        {
            Directory.CreateDirectory(Constants.SavePath);

            File.WriteAllText(StatePath, JsonSerializer.Serialize(state));
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Could not write '{Path}'; headshots will be sent again next time.", StatePath);
        }
    }
}
