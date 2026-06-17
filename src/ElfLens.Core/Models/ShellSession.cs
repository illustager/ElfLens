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
    private const int ReadTimeoutMs = 10_000;

    internal ShellSession(ShellStream shellStream)
    {
        _shellStream = shellStream ?? throw new ArgumentNullException(nameof(shellStream));
        _writer = new StreamWriter(_shellStream) { AutoFlush = true };

        Thread.Sleep(800);
        Drain();
        _writer.WriteLine("stty -echo 2>/dev/null; export PS1='$ '");
        Thread.Sleep(300);
        Drain();
    }

    private void Drain()
    {
        try
        {
            var buffer = new byte[4096];
            var start = Environment.TickCount;
            while (Environment.TickCount - start < 1200)
            {
                while (_shellStream.DataAvailable)
                    _shellStream.Read(buffer, 0, buffer.Length);
                Thread.Sleep(60);
            }
        }
        catch { }
    }

    public async Task<string> ExecuteCommandAsync(string command, CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            Drain();
            await _writer.WriteLineAsync(command.AsMemory(), ct);
            var rawOutput = await ReadOutputAsync(ct);
            return CleanOutput(rawOutput);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task<string> ReadOutputAsync(CancellationToken ct)
    {
        var sb = new StringBuilder();
        var buffer = new byte[4096];

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(ReadTimeoutMs);

        int idleLoops = 0;
        const int maxIdle = 6;

        try
        {
            while (!cts.IsCancellationRequested)
            {
                bool gotData = false;
                while (_shellStream.DataAvailable)
                {
                    int bytesRead = _shellStream.Read(buffer, 0, buffer.Length);
                    if (bytesRead > 0)
                    {
                        sb.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead));
                        gotData = true;
                    }
                    else break;
                }
                if (gotData) idleLoops = 0;
                else
                {
                    idleLoops++;
                    if (idleLoops >= maxIdle && sb.Length > 0) break;
                }
                await Task.Delay(50, cts.Token);
            }
        }
        catch (OperationCanceledException) { }

        return sb.ToString();
    }

    /// <summary>
    /// Strips ANSI and the trailing prompt line.
    /// With stty -echo there is no echoed command — output is just result + prompt.
    /// </summary>
    private static string CleanOutput(string rawOutput)
    {
        if (string.IsNullOrEmpty(rawOutput))
            return "(no output)";

        var text = AnsiRegex().Replace(rawOutput, "");
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');

        var lines = text.Split('\n');
        var end = lines.Length;

        // Strip trailing blank lines and prompt lines
        while (end > 0)
        {
            var trimmed = lines[end - 1].Trim();
            if (trimmed.Length == 0 || IsTrailingPrompt(lines[end - 1]))
                end--;
            else
                break;
        }

        // Rebuild, collapsing consecutive blank lines
        var sb = new StringBuilder();
        var prevBlank = false;
        for (int i = 0; i < end; i++)
        {
            var line = lines[i];
            var isBlank = line.Trim().Length == 0;

            if (isBlank)
            {
                if (!prevBlank && sb.Length > 0)
                    sb.AppendLine();
                prevBlank = true;
            }
            else
            {
                sb.AppendLine(line);
                prevBlank = false;
            }
        }

        var result = sb.ToString().Trim();
        return result.Length > 0 ? result : "(no output)";
    }

    private static bool IsTrailingPrompt(string line)
    {
        var trimmed = line.Trim();
        return trimmed is "$" or "#" or "$ " or "# "
            || trimmed.EndsWith("$ ") || trimmed.EndsWith("# ");
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

    public void Dispose()
    {
        _writer?.Dispose();
        _shellStream?.Dispose();
    }
}
