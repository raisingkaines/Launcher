using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Launcher.Helpers;

/// <summary>Per-launch, bounded Wine stderr capture. Never records arguments or environment.</summary>
public sealed class WineProcessDiagnostics
{
    public const int MaximumLines = 80;
    public const int MaximumCharacters = 16384;
    private const int MaximumLineCharacters = 2048;
    private static readonly Regex Credentials = new(
        @"(?i)(\b(?:sessionid|ticket|password|authorization|access_token|refresh_token)""?\s*[=:]\s*)(?:(?:Bearer|Basic)\s+)?(?:""[^""]*""|'[^']*'|[^\s,;&]+)",
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    private readonly Process _process;
    private readonly string[] _secrets;
    private readonly object _gate = new();
    private readonly Queue<string> _tail = new();
    private readonly CancellationTokenSource _cancel = new();
    private readonly TaskCompletionSource _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _characters;
    private bool _truncated;
    private bool _failed;

    public WineProcessDiagnostics(Process process, params string?[] secrets)
    {
        _process = process;
        _secrets = secrets.Where(value => !string.IsNullOrEmpty(value)).Select(value => value!).ToArray();
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardError = true;
    }

    // Called immediately after Start. CompleteAsync may already be awaiting a very early exit.
    public void StartReading() => _ = ReadAsync();

    private async Task ReadAsync()
    {
        var buffer = new char[1024];
        var line = new StringBuilder();
        var overlong = false;
        try
        {
            while (await _process.StandardError.ReadAsync(buffer.AsMemory(), _cancel.Token).ConfigureAwait(false) is var count && count > 0)
            {
                for (var i = 0; i < count; i++)
                {
                    if (buffer[i] == '\n')
                    {
                        AddLine(overlong ? "[An overlong stderr line was omitted.]" : line.ToString().TrimEnd('\r'));
                        line.Clear();
                        overlong = false;
                    }
                    else if (!overlong)
                    {
                        if (line.Length == MaximumLineCharacters)
                        {
                            line.Clear();
                            overlong = true;
                        }
                        else
                            line.Append(buffer[i]);
                    }
                }
            }
            if (overlong || line.Length > 0)
                AddLine(overlong ? "[An overlong stderr line was omitted.]" : line.ToString());
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException or OperationCanceledException)
        {
            lock (_gate)
                _failed = true;
        }
        finally
        {
            _drained.TrySetResult();
        }
    }

    private void AddLine(string line)
    {
        // Redact before retaining a line. Oversized lines are omitted rather than clipped,
        // so clipping cannot expose a fragment of a credential split across read buffers.
        foreach (var secret in _secrets)
            line = line.Replace(secret, "[redacted]", StringComparison.Ordinal);
        try
        {
            line = Credentials.Replace(line, "$1[redacted]");
        }
        catch (RegexMatchTimeoutException)
        {
            line = "[A stderr line could not be safely included.]";
        }
        if (line.Length > MaximumLineCharacters)
            line = "[An overlong stderr line was omitted.]";
        if (string.IsNullOrWhiteSpace(line))
            return;
        lock (_gate)
        {
            while (_tail.Count >= MaximumLines || _characters + line.Length + 1 > MaximumCharacters)
            {
                _characters -= _tail.Dequeue().Length + 1;
                _truncated = true;
            }
            _tail.Enqueue(line);
            _characters += line.Length + 1;
        }
    }

    public async Task<string> CompleteAsync(TimeSpan? drainTimeout = null)
    {
        // Wine descendants can inherit the pipe. Never wait indefinitely for their EOF,
        // and never kill the game or a Wine service to collect diagnostics.
        var complete = await Task.WhenAny(_drained.Task, Task.Delay(drainTimeout ?? TimeSpan.FromSeconds(2))).ConfigureAwait(false) == _drained.Task;
        if (!complete)
            _cancel.Cancel();
        lock (_gate)
        {
            var result = new StringBuilder("Wine stderr from this launch:");
            result.AppendLine();
            if (_truncated)
                result.AppendLine("[Earlier stderr output omitted; bounded tail follows.]");
            if (_tail.Count == 0)
                result.AppendLine("[No stderr output was captured.]");
            foreach (var line in _tail)
                result.AppendLine(line);
            if (!complete || _failed)
                result.AppendLine("[Stderr capture ended before EOF; output may be incomplete.]");
            return result.ToString();
        }
    }
}
