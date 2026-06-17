using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Renci.SshNet;

namespace ElfLens.Core.Models;

#pragma warning disable CA2022

public partial class ShellSession : IDisposable
{
    private readonly ShellStream _shellStream;
    private readonly StreamWriter _writer;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public string Prompt { get; private set; } = "$ ";

    internal ShellSession(ShellStream shellStream)
    {
        _shellStream = shellStream;
        _writer = new StreamWriter(_shellStream) { AutoFlush = true };
        Thread.Sleep(800);
        Drain();
        _writer.WriteLine();
        Thread.Sleep(400);
        CapturePrompt();
    }

    private void CapturePrompt()
    {
        var raw = ReadAvailable();
        if (raw.Length == 0) return;
        var t = AnsiRegex().Replace(raw, "").Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        var lines = t.Split('\n');
        for (int i = lines.Length - 1; i >= 0; i--)
        {
            var s = lines[i].Trim();
            if (s.Length > 0) { Prompt = s; break; }
        }
    }

    /// <summary>
    /// Executes a command.
    /// All output lines except the final prompt are sent via onLine (each gets \n in the ViewModel).
    /// The final prompt line is returned so the caller can place it without a trailing newline.
    /// </summary>
    public async Task<string> ExecuteCommandAsync(
        string command,
        Action<string> onLine,
        CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            await _writer.WriteLineAsync(command.AsMemory(), ct);
            var lines = await ReadAllLinesAsync(ct);
            return Emit(lines, onLine);
        }
        finally { _writeLock.Release(); }
    }

    // ---- raw I/O ----

    private string ReadAvailable()
    {
        var sb = new StringBuilder();
        var buf = new byte[4096];
        var sw = Environment.TickCount;
        while (Environment.TickCount - sw < 2000)
        {
            bool got = false;
            while (_shellStream.DataAvailable)
            {
                int n = _shellStream.Read(buf, 0, buf.Length);
                if (n > 0) { sb.Append(Encoding.UTF8.GetString(buf, 0, n)); got = true; }
                else break;
            }
            if (!got && sb.Length > 0) break;
            Thread.Sleep(50);
        }
        return sb.ToString();
    }

    private void Drain()
    {
        var buf = new byte[4096];
        var sw = Environment.TickCount;
        while (Environment.TickCount - sw < 1500)
        {
            while (_shellStream.DataAvailable) _shellStream.Read(buf, 0, buf.Length);
            Thread.Sleep(50);
        }
    }

    private async Task<List<string>> ReadAllLinesAsync(CancellationToken ct)
    {
        var result = new List<string>();
        var leftover = new StringBuilder();
        var buf = new byte[4096];
        int idle = 0;
        const int maxIdle = 3; // 150ms idle → command done

        while (!ct.IsCancellationRequested)
        {
            bool gotData = false;
            while (_shellStream.DataAvailable)
            {
                int n = _shellStream.Read(buf, 0, buf.Length);
                if (n > 0) { leftover.Append(Encoding.UTF8.GetString(buf, 0, n)); gotData = true; }
                else break;
            }

            if (gotData)
            {
                idle = 0;
                var text = leftover.ToString();
                int idx;
                while ((idx = text.IndexOf('\n')) >= 0)
                {
                    var raw = text[..idx];
                    text = text[(idx + 1)..];
                    var line = AnsiRegex().Replace(raw, "").Replace("\r", "").Trim();
                    if (line.Length > 0) result.Add(line);
                }
                leftover.Clear();
                leftover.Append(text);
            }
            else
            {
                idle++;
                if (idle >= maxIdle) break;
            }
            await Task.Delay(50, ct);
        }

        // Flush leftover
        if (leftover.Length > 0)
        {
            var line = AnsiRegex().Replace(leftover.ToString(), "").Replace("\r", "").Trim();
            if (line.Length > 0) result.Add(line);
        }

        return result;
    }

    /// <summary>
    /// Deduplicate and emit. Consecutive duplicate → keep last.
    /// Non-consecutive duplicate within the same output → skip.
    /// All lines except the last are emitted via callback.
    /// Returns the last line (prompt) without emitting.
    /// </summary>
    private static string Emit(List<string> raw, Action<string> onLine)
    {
        if (raw.Count == 0) return "";

        // Pass 1: collapse consecutive duplicates (keep last)
        var deduped = new List<string>();
        foreach (var line in raw)
        {
            if (deduped.Count > 0 && deduped[^1] == line)
                deduped.RemoveAt(deduped.Count - 1);
            deduped.Add(line);
        }

        // Pass 2: remove non-consecutive duplicates within this output
        var seen = new HashSet<string>();
        var final = new List<string>();
        foreach (var line in deduped)
        {
            if (seen.Add(line))
                final.Add(line);
        }

        if (final.Count == 0) return "";

        // Emit all but the last line
        for (int i = 0; i < final.Count - 1; i++)
            onLine(final[i]);

        // Return last line (prompt)
        var last = final[^1];
        DebugLog("RETURN", last);
        return last;
    }

    [GeneratedRegex(
        @"\x1b\[[0-9;?]*[a-zA-Z]|" +
        @"\x1b\][^\x07]*\x07|" +
        @"\x1b\][^\x1b]*\x1b\\|" +
        @"\x1b[PX^_].*?\x1b\\|" +
        @"\x1b[\x20-\x2f][^\x1b]*\x1b\\|" +
        @"\x1b[()][0-2AB]|" +
        @"\x1b\[\?[0-9]+[hl]")]
    private static partial Regex AnsiRegex();

    private static void DebugLog(string label, string content)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "shell_debug.log");
            if (File.Exists(path) && new FileInfo(path).Length > 200_000)
                File.WriteAllText(path, "");
            File.AppendAllText(path, $"\n=== {label} ===\n{content}\n=== END ===\n");
        }
        catch { }
    }

    public void Dispose()
    {
        _writer?.Dispose();
        _shellStream?.Dispose();
    }
}
