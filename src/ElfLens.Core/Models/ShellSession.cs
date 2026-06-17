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
    private CancellationTokenSource? _readCts;

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

        // Start background read loop
        _readCts = new CancellationTokenSource();
        _ = Task.Run(() => ReadLoopAsync(_readCts.Token));
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

    /// <summary>Fires for every chunk of cleaned output from the shell.</summary>
    public event Action<string>? OnOutput;

    /// <summary>
    /// Sends a command to the shell and returns immediately.
    /// Output arrives asynchronously via OnOutput.
    /// </summary>
    public async Task SendCommandAsync(string command)
    {
        await _writeLock.WaitAsync();
        try
        {
            var line = command.EndsWith('\n') ? command : command + "\n";
            await _writer.WriteAsync(line.AsMemory());
        }
        finally { _writeLock.Release(); }
    }

    // ---- background read ----

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        var buf = new byte[4096];
        bool prevEndsWithNewline = false;
        var accum = new StringBuilder();

        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (_shellStream.DataAvailable)
                {
                    int n = _shellStream.Read(buf, 0, buf.Length);
                    if (n > 0)
                    {
                        var raw = Encoding.UTF8.GetString(buf, 0, n);
                        var clean = AnsiRegex().Replace(raw, "");
                        clean = clean.Replace("\r\n", "\n").Replace('\r', '\n');
                        clean = MultipleNewlineRegex().Replace(clean, "\n");

                        if (prevEndsWithNewline && clean.StartsWith('\n'))
                            clean = clean[1..];

                        if (clean.Length > 0)
                        {
                            prevEndsWithNewline = clean[^1] == '\n';
                            accum.Append(clean);
                            OnOutput?.Invoke(clean);

                            // Update prompt from last line
                            var text = accum.ToString().TrimEnd('\n');
                            var last = text.Split('\n')[^1].Trim();
                            if (last.Length > 0) Prompt = last;
                        }
                    }
                }
                else
                {
                    await Task.Delay(30, ct);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
    }

    // ---- helpers ----

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
        _readCts?.Cancel();
        _writer?.Dispose();
        _shellStream?.Dispose();
    }
}
