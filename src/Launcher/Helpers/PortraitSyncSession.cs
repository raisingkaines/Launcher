using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Launcher.Helpers;

/// <summary>Uploads new native headshots throughout one game session.</summary>
public sealed class PortraitSyncSession : IDisposable
{
    private readonly string _directory;
    private readonly Func<string, byte[], CancellationToken, Task<bool>> _upload;
    private readonly TimeProvider _time;
    private readonly long _started;
    private readonly CancellationTokenSource _cancel = new();
    private readonly Dictionary<string, (long Written, long Size)> _observed = new();
    private readonly Dictionary<string, string> _sent = new();
    private readonly HashSet<string> _pending = new();
    private readonly Dictionary<string, (int Failures, DateTimeOffset Next)> _retry = new();
    private readonly SemaphoreSlim _scan = new(1, 1);
    private Task? _run;
    private int _cursor;
    private bool _disposed;

    // Construct before Process.Start: files from another account's older session
    // are a baseline, never an upload queue. The server still verifies ownership.
    public PortraitSyncSession(string directory,
        Func<string, byte[], CancellationToken, Task<bool>> upload, TimeProvider? time = null)
    {
        _directory = directory;
        _upload = upload;
        _time = time ?? TimeProvider.System;
        _started = _time.GetUtcNow().UtcTicks;
        foreach (var file in Files())
            if (Stamp(file) is { } stamp) _observed[Path.GetFileNameWithoutExtension(file)] = stamp;
    }

    public void Start()
    {
        if (_disposed || _run is not null) return;
        _run = RunAsync();
    }

    private async Task RunAsync()
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
            do { await ScanAsync(_cancel.Token).ConfigureAwait(false); }
            while (await timer.WaitForNextTickAsync(_cancel.Token).ConfigureAwait(false));
        }
        catch (OperationCanceledException) { }
    }

    // Separate from scheduling so tests exercise actual file/ack races deterministically.
    public async Task ScanAsync(CancellationToken token = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _cancel.Token);
        await _scan.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            var files = Files();
            foreach (var file in files)
            {
                linked.Token.ThrowIfCancellationRequested();
                var guid = Path.GetFileNameWithoutExtension(file);
                if (Stamp(file) is not { } stamp) continue;
                var known = _observed.TryGetValue(guid, out var prior);
                if (!known && stamp.Written < _started) continue;
                if (!known || prior != stamp)
                {
                    _observed[guid] = stamp;
                    _pending.Add(guid);
                }
            }
            // Observe the entire cache, but read/upload only a rotating batch. An
            // old large cache or an invalid pending image cannot starve another GUID.
            var pending = files.Where(file => _pending.Contains(Path.GetFileNameWithoutExtension(file))).ToArray();
            for (var count = 0; count < Math.Min(8, pending.Length); count++)
            {
                linked.Token.ThrowIfCancellationRequested();
                _cursor %= pending.Length;
                var file = pending[_cursor++];
                var guid = Path.GetFileNameWithoutExtension(file);
                if (Stamp(file) is not { } stamp || stamp.Size is < 45 or > 262144) continue;
                if (_retry.TryGetValue(guid, out var retry) && _time.GetUtcNow() < retry.Next) continue;
                byte[] image;
                try { image = await File.ReadAllBytesAsync(file, linked.Token).ConfigureAwait(false); }
                catch (IOException) { continue; }
                catch (UnauthorizedAccessException) { continue; }
                if (Stamp(file) != stamp || !IsHeadshot(image)) continue;
                var hash = Convert.ToHexString(SHA256.HashData(image));
                if (_sent.TryGetValue(guid, out var sent) && sent == hash) { _pending.Remove(guid); continue; }
                bool accepted;
                try { accepted = await _upload(guid, image, linked.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { throw; }
                catch { accepted = false; } // An upload failure must not stop later captures.
                if (accepted)
                {
                    _sent[guid] = hash;
                    _pending.Remove(guid);
                    _retry.Remove(guid);
                }
                else
                {
                    var failures = Math.Min(retry.Failures + 1, 5);
                    _retry[guid] = (failures, _time.GetUtcNow().AddSeconds(Math.Min(60, 5 * (1 << (failures - 1)))));
                }
            }
        }
        finally { _scan.Release(); }
    }

    private string[] Files()
    {
        try
        {
            var directory = ResolveDirectory();
            if (directory is null || (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) return [];
            return Directory.EnumerateFiles(directory, "*.png").Where(file =>
            {
                var name = Path.GetFileNameWithoutExtension(file);
                return ulong.TryParse(name, out var guid) && guid > 1 && (guid & 15) == 1 &&
                    name == guid.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }).OrderBy(file => file, StringComparer.Ordinal).ToArray();
        }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }
    }

    private string? ResolveDirectory()
    {
        // Wine uses the native spelling "Resources/images/heads". Discover existing
        // components case-insensitively on Unix too, including folders created later.
        var full = Path.GetFullPath(_directory);
        var root = Path.GetPathRoot(full)!;
        var current = root;
        foreach (var part in full[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!Directory.Exists(current)) return null;
            var next = Path.Combine(current, part);
            if (!Directory.Exists(next))
            {
                var matches = Directory.EnumerateDirectories(current).Where(path =>
                    string.Equals(Path.GetFileName(path), part, StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
                if (matches.Length != 1) return null;
                next = matches[0];
            }
            current = next;
        }
        return Directory.Exists(current) ? current : null;
    }

    private static (long Written, long Size)? Stamp(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || (info.Attributes & FileAttributes.ReparsePoint) != 0) return null;
            return (info.LastWriteTimeUtc.Ticks, info.Length);
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static bool IsHeadshot(byte[] bytes) => bytes.Length is >= 45 and <= 262144 &&
        bytes.AsSpan(0, 8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10}) &&
        bytes.AsSpan(12, 4).SequenceEqual("IHDR"u8) &&
        BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16, 4)) == 70 &&
        BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20, 4)) == 70;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cancel.Cancel();
        // Reader/upload may still be unwinding; they own disposal of linked tokens.
    }
}
