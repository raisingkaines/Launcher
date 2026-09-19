using System;
using System.IO;
using System.Threading;

using Avalonia.Platform;

using NAudio.Wave;
using NAudio.Wave.SampleProviders;

using NLog;

namespace Launcher.Services;

/// <summary>
/// Plays a quiet looping track while the launcher is open.
///
/// The track is not shipped with the launcher - drop an audio file at
/// Assets\music\theme.mp3 and it will be picked up. Without one this does nothing at all,
/// quietly, which is the intended state until somebody chooses the music.
///
/// Nothing here is worth interrupting anyone over: a missing file, a machine with no
/// audio device, a codec the system cannot read, all end the same way, with a log line
/// and no music.
/// </summary>
public static class MusicService
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
    private static readonly Lock _lock = new();

    /// <summary>Where the track is looked for, if anyone has supplied one.</summary>
    private const string TrackUri = "avares://Launcher/Assets/music/theme.mp3";

    /// <summary>
    /// Background music, so it sits well under everything else. This is deliberately not
    /// adjustable: the choice offered is on or off.
    /// </summary>
    private const float Volume = 0.18f;

    private static IWavePlayer? _output;
    private static WaveStream? _reader;
    private static Stream? _source;

    /// <summary>Set while stopping on purpose, so the loop does not restart it.</summary>
    private static bool _stopping;

    public static void Start()
    {
        lock (_lock)
        {
            if (_output is not null)
                return;

            try
            {
                var uri = new Uri(TrackUri);

                if (!AssetLoader.Exists(uri))
                {
                    _logger.Info("No launcher music at '{Uri}'; add one there to have it play.", TrackUri);

                    return;
                }

                _source = AssetLoader.Open(uri);

                // Reads whatever the system can decode rather than one hardcoded format,
                // so the track can be swapped without touching this.
                _reader = new StreamMediaFoundationReader(_source);

                var sample = new VolumeSampleProvider(_reader.ToSampleProvider()) { Volume = Volume };

                _stopping = false;
                _output = new WaveOutEvent();
                _output.PlaybackStopped += OnPlaybackStopped;
                _output.Init(sample);
                _output.Play();

                _logger.Info("Launcher music started.");
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Could not start the launcher music.");

                Cleanup();
            }
        }
    }

    public static void Stop()
    {
        lock (_lock)
        {
            if (_output is null)
                return;

            _stopping = true;

            try
            {
                _output.Stop();
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Could not stop the launcher music cleanly.");
            }

            Cleanup();

            _logger.Info("Launcher music stopped.");
        }
    }

    /// <summary>
    /// The track has run out, so start it again. NAudio raises this for a deliberate stop
    /// as well, which is what the flag is for.
    /// </summary>
    private static void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        // Deliberately not taking the lock: Stop() holds it while this can be raised.
        if (_stopping || _output is null || _reader is null)
            return;

        try
        {
            _reader.Position = 0;

            _output.Play();
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Could not loop the launcher music.");
        }
    }

    private static void Cleanup()
    {
        if (_output is not null)
            _output.PlaybackStopped -= OnPlaybackStopped;

        _output?.Dispose();
        _reader?.Dispose();
        _source?.Dispose();

        _output = null;
        _reader = null;
        _source = null;
    }
}
