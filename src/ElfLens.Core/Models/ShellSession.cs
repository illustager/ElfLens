using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Renci.SshNet;

namespace ElfLens.Core.Models;

/// <summary>
/// Wraps an interactive SSH shell stream for command execution.
/// Uses an end-marker pattern to reliably capture command output,
/// then strips ANSI escape sequences for clean display.
/// </summary>
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

        // Discard initial login banner / MOTD / first prompt
        Task.Run(async () => await ReadUntilPromptAsync()).Wait(3000);
    }

    /// <summary>
    /// Executes a command interactively on the remote shell and returns cleaned output.
    /// </summary>
    public async Task<string> ExecuteCommandAsync(string command, CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            // Send the command with an end marker so we know when it's done
            var fullCommand = $"{command}; echo {EndMarker}$?";
            await _writer.WriteLineAsync(fullCommand.AsMemory(), ct);

            // Read until the end marker appears
            var rawOutput = await ReadUntilEndMarkerAsync(ct);

            // Strip ANSI escape sequences and clean up
            return CleanOutput(rawOutput, command);
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
                    var bytesRead = await _shellStream.ReadAsync(buffer, 0, buffer.Length, timeoutCts.Token);
                    if (bytesRead > 0)
                    {
                        sb.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead));

                        // Check if we've received the end marker
                        var current = sb.ToString();
                        if (current.Contains(EndMarker))
                            break;
                    }
                }
                else
                {
                    await Task.Delay(50, timeoutCts.Token);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Timeout or cancellation — return what we have
        }

        return sb.ToString();
    }

    private async Task<string> ReadUntilPromptAsync()
    {
        var sb = new StringBuilder();
        var buffer = new byte[4096];

        try
        {
            using var cts = new CancellationTokenSource(3000);
            while (!cts.IsCancellationRequested)
            {
                if (_shellStream.DataAvailable)
                {
                    var bytesRead = await _shellStream.ReadAsync(buffer, 0, buffer.Length, cts.Token);
                    if (bytesRead > 0)
                        sb.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead));
                }
                else
                {
                    await Task.Delay(100, cts.Token);
                }
            }
        }
        catch (OperationCanceledException) { }

        return sb.ToString();
    }

    /// <summary>
    /// Strips ANSI escape sequences and command artifacts from raw shell output.
    /// </summary>
    private static string CleanOutput(string rawOutput, string command)
    {
        if (string.IsNullOrEmpty(rawOutput))
            return string.Empty;

        // 1. Strip ANSI escape sequences
        var cleaned = AnsiRegex().Replace(rawOutput, string.Empty);

        // 2. Convert to lines
        var lines = cleaned.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var result = new StringBuilder();
        bool commandPassed = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            // Skip the end marker line
            if (trimmed.Contains(EndMarker))
                break;

            // Skip empty/whitespace-only lines before command output starts
            if (!commandPassed)
            {
                // The echoed command usually contains our command text
                if (trimmed.Contains(command.Trim()))
                {
                    commandPassed = true;
                    continue; // skip the echoed command line itself
                }
                if (string.IsNullOrEmpty(trimmed))
                    continue;
                // First non-empty, non-command line
                commandPassed = true;
            }

            // Skip typical prompt-only lines
            if (trimmed is "$" or "#")
                continue;
            if (trimmed.EndsWith("$ ") || trimmed.EndsWith("# "))
            {
                // Might contain output before the prompt
                var dollarIdx = trimmed.LastIndexOfAny(['$', '#']);
                if (dollarIdx > 0 && trimmed[dollarIdx - 1] == ' ')
                {
                    var before = trimmed[..dollarIdx].Trim();
                    if (!string.IsNullOrEmpty(before))
                        result.AppendLine(before);
                }
                continue;
            }

            result.AppendLine(line);
        }

        var final = result.ToString().TrimEnd();
        return string.IsNullOrEmpty(final) ? "(no output)" : final;
    }

    /// <summary>
    /// Regex matching ANSI escape sequences (CSI, OSC, and other ESC-prefixed codes).
    /// </summary>
    [GeneratedRegex(@"\x1b\[[0-9;?]*[a-zA-Z]|\x1b\][^\x07]*\x07|\x1b\][^\x1b]*\x1b\\|\x1b].*?\x1b\\|\x1b[PX^_][^\x1b]*\x1b\\|\x1b[\x20-\x2f][^\x1b]*\x1b\\|\x1b\[[0-9;?]*[A-Za-z]|\x1b\].*?(?:\x07|\x1b\\)|\x1b[()][0-2AB]")]
    private static partial Regex AnsiRegex();

    public void Dispose()
    {
        _writer?.Dispose();
        _shellStream?.Dispose();
    }
}
