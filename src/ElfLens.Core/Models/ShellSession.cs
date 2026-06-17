using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Renci.SshNet;

namespace ElfLens.Core.Models;

public partial class ShellSession : IDisposable
{
    private readonly ShellStream _shellStream;
    private readonly StreamWriter _writer;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private const string EndMarker = "__ELFENS_END__";
    private const int ReadTimeoutMs = 30_000;

    internal ShellSession(ShellStream shellStream)
    {
        _shellStream = shellStream ?? throw new ArgumentNullException(nameof(shellStream));
        _writer = new StreamWriter(_shellStream) { AutoFlush = true };
        DrainInitialOutput();
    }

    private void DrainInitialOutput()
    {
        try
        {
            var buffer = new byte[4096];
            for (int i = 0; i < 30; i++)
            {
                if (_shellStream.DataAvailable)
                    _shellStream.Read(buffer, 0, buffer.Length);
                else
                    Thread.Sleep(100);
            }
        }
        catch { /* best-effort */ }
    }

    public async Task<string> ExecuteCommandAsync(string command, CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            // Append an end marker so we know when output is done
            var fullCommand = $"{command}; echo {EndMarker}$?";
            await _writer.WriteLineAsync(fullCommand.AsMemory(), ct);

            // Read everything until the end marker appears
            var rawOutput = await ReadUntilEndMarkerAsync(ct);

            return CleanOutput(rawOutput);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task<string> ReadUntilEndMarkerAsync(CancellationToken ct)
    {
        var sb = new StringBuilder();
        var buffer = new byte[4096];

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(ReadTimeoutMs);

        try
        {
            while (!timeoutCts.IsCancellationRequested)
            {
                if (_shellStream.DataAvailable)
                {
                    var bytesRead = _shellStream.Read(buffer, 0, buffer.Length);
                    if (bytesRead > 0)
                    {
                        sb.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead));
                        if (sb.ToString().Contains(EndMarker))
                            break;
                    }
                }
                else
                {
                    await Task.Delay(50, timeoutCts.Token);
                }
            }
        }
        catch (OperationCanceledException) { }

        return sb.ToString();
    }

    private static string CleanOutput(string rawOutput)
    {
        if (string.IsNullOrEmpty(rawOutput))
            return "(no output)";

        // 1. Strip all ANSI / OSC escape sequences
        var text = AnsiRegex().Replace(rawOutput, "");

        // 2. Normalize line endings
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');

        // 3. Locate the end marker and take everything before it
        var markerIdx = text.IndexOf(EndMarker, StringComparison.Ordinal);
        if (markerIdx < 0)
            return "(no output)";

        text = text[..markerIdx];

        // 4. Remove the echoed command line (first line that looks like our full command)
        var lines = text.Split('\n');
        var sb = new StringBuilder();
        var firstRealLine = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            // Skip empty lines before real output
            if (!firstRealLine && trimmed.Length == 0)
                continue;

            // Skip the echoed command line — it contains EndMarker text and $?
            // Since we already cut at the marker, this would be the full command echo
            if (!firstRealLine && trimmed.Contains("echo") && trimmed.Contains("$?"))
            {
                firstRealLine = true;
                continue;
            }

            // Skip lines that are just a prompt
            if (trimmed is "$" or "#" or "")
                continue;

            // Handle embedded prompt: "some output user@host:~$ "
            if (trimmed.EndsWith("$ ") || trimmed.EndsWith("# "))
            {
                var promptIdx = trimmed.LastIndexOfAny(['$', '#']);
                if (promptIdx > 0 && trimmed[promptIdx - 1] == ' ')
                {
                    var content = trimmed[..promptIdx].Trim();
                    if (content.Length > 0)
                    {
                        sb.AppendLine(content);
                    }
                }
                continue;
            }

            firstRealLine = true;
            sb.AppendLine(line);
        }

        var result = sb.ToString().Trim();
        return result.Length > 0 ? result : "(no output)";
    }

    // Matches CSI, OSC, and other ANSI escape codes
    [GeneratedRegex(
        @"\x1b\[[0-9;?]*[a-zA-Z]|" +       // CSI: \e[...m, \e[...J, etc.
        @"\x1b\][^\x07]*\x07|" +            // OSC ending with BEL
        @"\x1b\][^\x1b]*\x1b\\|" +          // OSC ending with ST
        @"\x1b[PX^_].*?\x1b\\|" +           // DCS/SOS/PM/APC
        @"\x1b[\x20-\x2f][^\x1b]*\x1b\\|" + // Escape sequences with intermediates
        @"\x1b[()][0-2AB]|" +               // Character set selection
        @"\x1b\[\?[0-9]+[hl]")]             // DEC private modes
    private static partial Regex AnsiRegex();

    public void Dispose()
    {
        _writer?.Dispose();
        _shellStream?.Dispose();
    }
}
