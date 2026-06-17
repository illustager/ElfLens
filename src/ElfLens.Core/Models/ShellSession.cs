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

    /// <summary>The shell's actual prompt, captured on init.</summary>
    public string Prompt { get; private set; } = "$ ";

    internal ShellSession(ShellStream shellStream)
    {
        _shellStream = shellStream ?? throw new ArgumentNullException(nameof(shellStream));
        _writer = new StreamWriter(_shellStream) { AutoFlush = true };

        // Drain MOTD, then force a prompt and capture it
        Thread.Sleep(800);
        Drain();
        _writer.WriteLine();
        Thread.Sleep(400);
        var promptRaw = ReadAvailable();
        if (promptRaw.Length > 0)
        {
            var cleaned = AnsiRegex().Replace(promptRaw, "");
            cleaned = cleaned.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
            var lastLine = cleaned.Split('\n')[^1].TrimEnd();
            if (lastLine.Length > 0)
                Prompt = lastLine;
        }
    }

    public async Task<string> ExecuteCommandAsync(string command, CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            Drain();
            await _writer.WriteLineAsync(command.AsMemory(), ct);
            var rawOutput = await ReadOutputAsync(ct);
            Drain();
            DebugLog(command, rawOutput);
            return CleanOutput(rawOutput);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private string ReadAvailable()
    {
        var sb = new StringBuilder();
        var buffer = new byte[4096];
        try
        {
            var start = Environment.TickCount;
            while (Environment.TickCount - start < 2000)
            {
                bool got = false;
                while (_shellStream.DataAvailable)
                {
                    int n = _shellStream.Read(buffer, 0, buffer.Length);
                    if (n > 0) { sb.Append(Encoding.UTF8.GetString(buffer, 0, n)); got = true; }
                    else break;
                }
                if (!got && sb.Length > 0) break;
                Thread.Sleep(50);
            }
        }
        catch { }
        return sb.ToString();
    }

    private void Drain()
    {
        try
        {
            var buffer = new byte[4096];
            var start = Environment.TickCount;
            while (Environment.TickCount - start < 1500)
            {
                while (_shellStream.DataAvailable)
                    _shellStream.Read(buffer, 0, buffer.Length);
                Thread.Sleep(50);
            }
        }
        catch { }
    }

    private async Task<string> ReadOutputAsync(CancellationToken ct)
    {
        var sb = new StringBuilder();
        var buffer = new byte[4096];
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(ReadTimeoutMs);
        int idleLoops = 0;
        const int maxIdle = 8;

        try
        {
            while (!cts.IsCancellationRequested)
            {
                bool gotData = false;
                while (_shellStream.DataAvailable)
                {
                    int bytesRead = _shellStream.Read(buffer, 0, buffer.Length);
                    if (bytesRead > 0) { sb.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead)); gotData = true; }
                    else break;
                }
                if (gotData) idleLoops = 0;
                else { idleLoops++; if (idleLoops >= maxIdle && sb.Length > 0) break; }
                await Task.Delay(40, cts.Token);
            }
        }
        catch (OperationCanceledException) { }
        return sb.ToString();
    }

    private string CleanOutput(string rawOutput)
    {
        if (string.IsNullOrEmpty(rawOutput)) return "(no output)";

        var text = AnsiRegex().Replace(rawOutput, "");
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');

        var lines = text.Split('\n');
        var result = new List<string>();
        string? prev = null;
        int pending = 0;

        foreach (var line in lines)
        {
            var isBlank = line.Trim().Length == 0;

            if (isBlank)
            {
                // If next non-blank matches prev, skip this blank + next
                pending++;
                continue;
            }

            if (line == prev)
            {
                pending = 0;
                continue;
            }

            // Flush at most one blank
            if (pending > 0 && result.Count > 0)
                result.Add("");
            pending = 0;

            prev = line;
            result.Add(line);
        }

        // Remove trailing blanks only
        while (result.Count > 0 && result[^1].Length == 0)
            result.RemoveAt(result.Count - 1);

        var sb = new StringBuilder();
        foreach (var line in result)
            sb.AppendLine(line);

        var final = sb.ToString().TrimEnd('\n');
        DebugLog("CLEAN", final);
        return final.Length > 0 ? final : "(no output)";
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
