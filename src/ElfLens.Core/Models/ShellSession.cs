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

    public async Task ExecuteCommandAsync(
        string command,
        Action<string> onChunk,
        CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            var line = command.EndsWith('\n') ? command : command + "\n";
            await _writer.WriteAsync(line.AsMemory(), ct);
            await ReadChunksAsync(onChunk, ct);
        }
        finally { _writeLock.Release(); }
    }

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
        bool prevEndsWithNewline = false;
        // Accumulated cleaned text for prompt detection
        var accum = new StringBuilder();
        const int maxIdle = 20; // 1 second fallback
        int idle = 0;

        while (!ct.IsCancellationRequested)
        {
            bool gotData = false;
            while (_shellStream.DataAvailable)
            {
                int n = _shellStream.Read(buf, 0, buf.Length);
                if (n <= 0) break;

                gotData = true;
                var raw = Encoding.UTF8.GetString(buf, 0, n);
                var clean = AnsiRegex().Replace(raw, "");
                clean = clean.Replace("\r\n", "\n").Replace('\r', '\n');
                clean = MultipleNewlineRegex().Replace(clean, "\n");

                if (prevEndsWithNewline && clean.StartsWith('\n'))
                    clean = clean[1..];

                if (clean.Length == 0) continue;

                prevEndsWithNewline = clean[^1] == '\n';
                onChunk(clean);
                accum.Append(clean);
            }

            if (gotData)
            {
                idle = 0;
                // Check if the accumulated output ends with the known prompt
                var text = accum.ToString().TrimEnd('\n');
                if (text.EndsWith(Prompt, StringComparison.Ordinal))
                    break; // prompt appeared → command done
            }
            else
            {
                idle++;
                if (idle >= maxIdle) break; // 1s fallback
            }
            await Task.Delay(50, ct);
        }

        // Update prompt from last line (handles changes like shell → GDB → Python)
        var final = accum.ToString().TrimEnd('\n');
        var last = final.Split('\n')[^1].Trim();
        if (last.Length > 0) Prompt = last;
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
