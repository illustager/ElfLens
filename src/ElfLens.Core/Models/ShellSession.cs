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
    private const int ReadTimeoutMs = 10_000;

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
        var cleaned = AnsiRegex().Replace(raw, "");
        cleaned = cleaned.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        var lines = cleaned.Split('\n');
        for (int i = lines.Length - 1; i >= 0; i--)
        {
            var t = lines[i].Trim();
            if (t.Length > 0) { Prompt = t; break; }
        }
    }

    public async Task<string> ExecuteCommandAsync(string command, CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            await _writer.WriteLineAsync(command.AsMemory(), ct);
            var raw = await ReadOutputAsync(ct);
            DebugLog("RAW", raw);
            var result = Clean(raw);
            DebugLog("CLEAN", result);
            return result;
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
            while (_shellStream.DataAvailable)
                _shellStream.Read(buf, 0, buf.Length);
            Thread.Sleep(50);
        }
    }

    private async Task<string> ReadOutputAsync(CancellationToken ct)
    {
        var sb = new StringBuilder();
        var buf = new byte[4096];
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(ReadTimeoutMs);
        int idle = 0;
        while (!cts.IsCancellationRequested)
        {
            bool got = false;
            while (_shellStream.DataAvailable)
            {
                int n = _shellStream.Read(buf, 0, buf.Length);
                if (n > 0) { sb.Append(Encoding.UTF8.GetString(buf, 0, n)); got = true; }
                else break;
            }
            if (got) idle = 0; else { idle++; if (idle >= 20 && sb.Length > 0) break; }
            await Task.Delay(50, cts.Token);
        }
        return sb.ToString();
    }

    // ---- output cleaning ----

    /// <summary>
    /// Strips ANSI, normalizes newlines, and deduplicates the trailing
    /// double-prompt that shells emit with bracketed-paste mode.
    /// Keeps the LAST prompt occurrence (no trailing newline) so the next
    /// command's echo naturally lands on the same line.
    /// </summary>
    private string Clean(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "(no output)";

        var text = AnsiRegex().Replace(raw, "");
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');

        // First pass: collapse consecutive duplicate prompt lines, keep last copy
        var deduped = new List<string>();
        foreach (var line in text.Split('\n'))
        {
            if (line.Trim().Length == 0)
                continue; // skip blank lines

            // Replace consecutive duplicates (prompt\nprompt → keep last)
            if (deduped.Count > 0 && deduped[^1] == line)
                deduped.RemoveAt(deduped.Count - 1);

            deduped.Add(line);
        }

        // Second pass: remove any non-blank line that appears more than once
        var seen = new HashSet<string>();
        var result = new List<string>();
        foreach (var line in deduped)
        {
            if (seen.Add(line))
                result.Add(line);
        }

        if (result.Count == 0) return "(no output)";
        var output = string.Join("\n", result);
        DebugLog("CLEAN", output);
        return output;
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
