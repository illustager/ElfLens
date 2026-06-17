using System;
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
    /// Executes a command, streaming cleaned output chunks to onChunk.
    /// No line splitting, no "last line" detection — the terminal data
    /// flows as-is, minus ANSI codes. The shell's own \r\n provides line breaks.
    /// The prompt has no trailing \r\n, so the next command echo joins it naturally.
    /// </summary>
    public async Task ExecuteCommandAsync(
        string command,
        Action<string> onChunk,
        CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            await _writer.WriteLineAsync(command.AsMemory(), ct);
            await ReadChunksAsync(onChunk, ct);
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

    private async Task ReadChunksAsync(Action<string> onChunk, CancellationToken ct)
    {
        var buf = new byte[4096];
        int idle = 0;
        const int maxIdle = 3; // 150ms
        bool prevEndsWithNewline = false;

        while (!ct.IsCancellationRequested)
        {
            bool gotData = false;
            while (_shellStream.DataAvailable)
            {
                int n = _shellStream.Read(buf, 0, buf.Length);
                if (n > 0)
                {
                    var raw = Encoding.UTF8.GetString(buf, 0, n);
                    var clean = AnsiRegex().Replace(raw, "");
                    clean = clean.Replace("\r\n", "\n").Replace('\r', '\n');

                    // Collapse consecutive newlines
                    clean = MultipleNewlineRegex().Replace(clean, "\n");

                    // If previous chunk ended with \n and this one starts with \n,
                    // strip the leading \n from this chunk
                    if (prevEndsWithNewline && clean.StartsWith('\n'))
                        clean = clean[1..];

                    if (clean.Length > 0)
                    {
                        prevEndsWithNewline = clean[^1] == '\n';
                        onChunk(clean);
                        gotData = true;
                    }
                }
                else break;
            }

            if (gotData) idle = 0;
            else { idle++; if (idle >= maxIdle) break; }
            await Task.Delay(50, ct);
        }
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

    [GeneratedRegex(@"\n\n+")]
    private static partial Regex MultipleNewlineRegex();

    public void Dispose()
    {
        _writer?.Dispose();
        _shellStream?.Dispose();
    }
}
