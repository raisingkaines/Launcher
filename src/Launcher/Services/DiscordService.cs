using System;
using System.Threading;
using System.Threading.Tasks;

using Discord;

using NLog;

namespace Launcher.Services;

public static class DiscordService
{
    private static readonly Lock _lock = new();
    private static Discord.Discord? _discord;
    private static CancellationTokenSource _cts = new();
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

    // The Client ID for the "FreeRealms Legacy Launcher" Discord application.
    private const long ClientId = 1223728876199608410;

    public static void Start()
    {
        lock (_lock)
        {
            // If the service was previously stopped, create a new CancellationTokenSource.
            if (_cts.IsCancellationRequested)
            {
                _cts.Dispose();
                _cts = new CancellationTokenSource();
            }

            // Avoid re-initializing if it's already running.
            if (_discord != null)
                return;

            try
            {
                // Create a new Discord client instance.
                _discord = new Discord.Discord(ClientId, (ulong)CreateFlags.NoRequireDiscord);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to initialize Discord SDK.");
                Stop();
                return;
            }

            // Start a long-running background task to handle Discord's event loop.
            Task.Factory.StartNew(UpdateAsync, _cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            _logger.Info("Discord Rich Presence service started.");
        }
    }

    public static void Stop()
    {
        lock (_lock)
        {
            if (!_cts.IsCancellationRequested)
            {
                _cts.Cancel();
            }

            _discord?.Dispose();
            _discord = null;
            _logger.Info("Discord Rich Presence service stopped.");
        }
    }

    private static async Task UpdateAsync()
    {
        try
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                lock (_lock)
                {
                    // If the client was disposed by Stop(), exit the loop.
                    if (_discord == null)
                        break;

                    _discord.RunCallbacks();
                }
                await Task.Delay(1000 / 60, _cts.Token);
            }
        }
        catch (TaskCanceledException)
        {

        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An unhandled exception occurred in the Discord update loop.");
            Stop();
        }
    }

    public static void UpdateActivity(string details, string state)
    {
        lock (_lock)
        {
            if (_discord == null)
            {
                // Don't log an error here, as this can happen normally if Discord isn't running.
                return;
            }

            try
            {
                var activityManager = _discord.GetActivityManager();
                var activity = new Activity
                {
                    State = state,
                    Details = details,
                    Type = ActivityType.Playing,
                    Timestamps =
                    {
                       Start = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                    }
                };

                activityManager.UpdateActivity(activity, (result) =>
                {
                    if (result != Result.Ok)
                    {
                        _logger.Warn("Failed to update Discord activity: {result}", result);
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to update Discord activity.");
            }
        }
    }
}