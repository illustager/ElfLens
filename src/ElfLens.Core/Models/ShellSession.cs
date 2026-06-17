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

    // Avoid $, _, and other shell-special chars in the marker
    private const string EndMarker = "EEELFENSEND999";
    private const int ReadTimeoutMs = 30_000;

    // Temporary log for debugging raw shell output
    private static readonly string LogPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "elflens_shell_debug.log");

    internal ShellSession(ShellStream shellStream)
    {
        _shellStream = shellStream ?? throw new ArgumentNullException(nameof(shellStream));
        _writer = new StreamWriter(_shellStream) { AutoFlush = true };
        DrainInitialOutput();

        // Turn off complex prompts — use a simple dollar sign
        _writer.WriteLine("export PS1='$ '");
        Thread.Sleep(200);
        DrainRemaining();
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
        catch { }
    }

    private void DrainRemaining()
    {
        try
        {
            var buffer = new byte[4096];
            for (int i = 0; i < 10; i++)
            {
                if (_shellStream.DataAvailable)
                    _shellStream.Read(buffer, 0, buffer.Length);
                else
                    Thread.Sleep(100);
            }
        }
        catch { }
    }

    public async Task<string> ExecuteCommandAsync(string command, CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            // Append end marker — pure alphanumeric, no shell-special chars
            var fullCommand = $"{command} ; echo {EndMarker}";
            await _writer.WriteLineAsync(fullCommand.AsMemory(), ct);

            var rawOutput = await ReadUntilEndMarkerAsync(ct);

            // Write raw output to debug log
            File.AppendAllText(LogPath,
                $"\n=== CMD: {command} ===\n{rawOutput}\n=== END ===\n");

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

        // 1. Strip ANSI sequences
        var text = AnsiRegex().Replace(rawOutput, "");

        // 2. Normalize line endings
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');

        // 3. Find the end marker and cut before it
        var markerIdx = text.IndexOf(EndMarker, StringComparison.Ordinal);
        if (markerIdx < 0)
        {
            // Marker not found — return whatever we got (stripped)
            var trimmed = text.Trim();
            return trimmed.Length > 0 ? trimmed : "(no output)";
        }

        text = text[..markerIdx];

        // 4. Remove the echoed command line and prompt artifacts
        var lines = text.Split('\n');
        var sb = new StringBuilder();
        var foundContent = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            // Skip the echoed command (contains our marker reference in the full command)
            if (!foundContent && trimmed.Contains("echo") && trimmed.Contains(EndMarker))
                continue;

            // Skip typical prompt lines
            if (trimmed is "$" or "#" or "")
                continue;

            // Handle line with embedded prompt at end: "some output $ "
            if (trimmed.EndsWith("$ ") || trimmed.EndsWith("# "))
            {
                var promptIdx = trimmed.LastIndexOfAny(['$', '#']);
                if (promptIdx > 0 && trimmed[promptIdx - 1] == ' ')
                {
                    var content = trimmed[..promptIdx].Trim();
                    if (content.Length > 0)
                    {
                        foundContent = true;
                        sb.AppendLine(content);
                    }
                }
                continue;
            }

            foundContent = true;
            sb.AppendLine(line);
        }

        var result = sb.ToString().Trim();
        return result.Length > 0 ? result : "(no output)";
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
