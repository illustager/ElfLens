using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Renci.SshNet;

namespace ElfLens.Core.Models;

/// <summary>
/// Wraps an SSH shell stream for command execution.
/// </summary>
public class ShellSession : IDisposable
{
    private readonly ShellStream _shellStream;
    private readonly StringBuilder _outputBuffer = new();

    internal ShellSession(ShellStream shellStream)
    {
        _shellStream = shellStream ?? throw new ArgumentNullException(nameof(shellStream));
    }

    /// <summary>
    /// Executes a command on the remote shell and returns the output.
    /// Uses a marker-based approach to detect command completion.
    /// </summary>
    public async Task<string> ExecuteCommandAsync(string command, CancellationToken ct = default)
    {
        // Write the command to the shell
        var writer = new StreamWriter(_shellStream) { AutoFlush = true };
        await writer.WriteLineAsync(command.AsMemory(), ct);

        // Read output until we see the next prompt
        // Simple approach: read available data with a timeout
        var result = new StringBuilder();
        var buffer = new byte[4096];

        try
        {
            // Give the command time to produce output
            await Task.Delay(200, ct);

            while (_shellStream.DataAvailable)
            {
                var bytesRead = await _shellStream.ReadAsync(buffer, 0, buffer.Length, ct);
                if (bytesRead > 0)
                {
                    var text = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    result.Append(text);
                }

                if (!_shellStream.DataAvailable)
                    await Task.Delay(100, ct);
            }

            // Additional wait for commands that might have delayed output
            await Task.Delay(100, ct);
            while (_shellStream.DataAvailable)
            {
                var bytesRead = await _shellStream.ReadAsync(buffer, 0, buffer.Length, ct);
                if (bytesRead > 0)
                {
                    var text = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    result.Append(text);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on cancellation
        }

        return CleanOutput(result.ToString(), command);
    }

    /// <summary>
    /// Cleans up shell output by removing the echoed command and prompt artifacts.
    /// </summary>
    private static string CleanOutput(string rawOutput, string command)
    {
        if (string.IsNullOrEmpty(rawOutput))
            return string.Empty;

        var lines = rawOutput.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        var cleaned = new StringBuilder();

        foreach (var line in lines)
        {
            // Skip the echoed command line
            if (line.Trim() == command.Trim())
                continue;

            // Skip empty prompt-only lines
            var trimmed = line.Trim();
            if (trimmed.EndsWith("$ ") || trimmed.EndsWith("# ") || trimmed.EndsWith("] "))
            {
                // Extract just the prompt part if there's content before it
                var promptIdx = trimmed.LastIndexOfAny(['$', '#', ']']);
                if (promptIdx > 0)
                {
                    var before = trimmed[..promptIdx].Trim();
                    if (!string.IsNullOrEmpty(before))
                        cleaned.AppendLine(before);
                }
                continue;
            }

            cleaned.AppendLine(line);
        }

        return cleaned.ToString().TrimEnd();
    }

    public void Dispose()
    {
        _shellStream?.Dispose();
    }
}
