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
        var cleaned = AnsiRegex().Replace(raw, "");
        cleaned = cleaned.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        var lines = cleaned.Split('\n');
        for (int i = lines.Length - 1; i >= 0; i--)
        {
            var t = lines[i].Trim();
            if (t.Length > 0) { Prompt = t; break; }
        }
    }

    /// <summary>
    /// Executes a command and streams output lines to the callback in real-time.
    /// Returns the final prompt line (if detected) for display continuity.
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
            return await ReadLinesAsync(onLine, ct);
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

    private async Task<string> ReadLinesAsync(Action<string> onLine, CancellationToken ct)
    {
        var leftover = new StringBuilder();
        var buf = new byte[4096];
        int idle = 0;
        const int maxIdle = 3; // 150ms idle → command done
        string? lastEmitted = null;
        var recent = new HashSet<string>(); // dedup within this command
        string? finalLine = null;

        void Emit(string line)
        {
            // Consecutive duplicate? Replace previous (keeps last copy)
            if (line == lastEmitted) return;

            // Non-consecutive duplicate within this command?
            if (!recent.Add(line)) return;

            // Trim recent set to avoid unbounded growth
            if (recent.Count > 50) recent.Clear();

            lastEmitted = line;
            finalLine = line;
            onLine(line);
        }

        while (!ct.IsCancellationRequested)
        {
            bool gotData = false;
            while (_shellStream.DataAvailable)
            {
                int n = _shellStream.Read(buf, 0, buf.Length);
                if (n > 0)
                {
                    leftover.Append(Encoding.UTF8.GetString(buf, 0, n));
                    gotData = true;
                }
                else break;
            }

            if (gotData)
            {
                idle = 0;
                var text = leftover.ToString();
                int idx;
                while ((idx = text.IndexOf('\n')) >= 0)
                {
                    var line = text[..idx];
                    text = text[(idx + 1)..];

                    var cleaned = AnsiRegex().Replace(line, "").Replace("\r", "").Trim();
                    if (cleaned.Length == 0) continue;

                    Emit(cleaned);
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
            var cleaned = AnsiRegex().Replace(leftover.ToString(), "").Replace("\r", "").Trim();
            if (cleaned.Length > 0) Emit(cleaned);
        }

        DebugLog("CLEAN", finalLine ?? "(empty)");
        return finalLine ?? "";
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
