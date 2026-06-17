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

    private const int ReadTimeoutMs = 10_000;

    internal ShellSession(ShellStream shellStream)
    {
        _shellStream = shellStream ?? throw new ArgumentNullException(nameof(shellStream));
        _writer = new StreamWriter(_shellStream) { AutoFlush = true };

        Thread.Sleep(500);
        Drain();
        _writer.WriteLine("stty -echo 2>/dev/null; export PS1='$ '");
        Thread.Sleep(300);
        Drain();
    }

    /// <summary>
    /// After executing a command, returns the detected prompt type (for UI display).
    /// </summary>
    public string DetectedPrompt { get; private set; } = "$ ";

    private void Drain()
    {
        try
        {
            var buffer = new byte[4096];
            for (int i = 0; i < 20; i++)
            {
                if (_shellStream.DataAvailable)
                {
                    int total = 0;
                    while (_shellStream.DataAvailable && total < buffer.Length)
                        total += _shellStream.Read(buffer, total, buffer.Length - total);
                }
                Thread.Sleep(50);
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

            // Debug log — write to working directory
            DebugLog(command, rawOutput);

            return CleanOutput(rawOutput, out var prompt);
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

                if (gotData)
                    idleLoops = 0;
                else
                {
                    idleLoops++;
                    if (idleLoops >= maxIdle && sb.Length > 0)
                        break;
                }

                await Task.Delay(50, cts.Token);
            }
        }
        catch (OperationCanceledException) { }

        return sb.ToString();
    }

    /// <summary>
    /// Cleans the raw shell output:
    /// 1. Strips all ANSI escape sequences
    /// 2. Strips leading and trailing prompt lines
    /// 3. Detects the current prompt type for UI display
    /// </summary>
    private string CleanOutput(string rawOutput, out string detectedPrompt)
    {
        detectedPrompt = DetectedPrompt;

        if (string.IsNullOrEmpty(rawOutput))
            return "(no output)";

        // 1. Strip ANSI escape sequences
        var text = AnsiRegex().Replace(rawOutput, "");

        // 2. Normalize line endings and collapse blank lines
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        text = CollapseBlankLines(text);

        // 3. Split into lines and strip leading + trailing prompt lines
        var lines = text.Split('\n');

        // Detect prompt from the last line
        detectedPrompt = DetectPromptFromLine(lines);

        // Find first and last non-prompt lines
        int firstContent = 0;
        int lastContent = lines.Length - 1;

        while (firstContent <= lastContent && IsPromptLine(lines[firstContent]))
            firstContent++;

        while (lastContent >= firstContent && IsPromptLine(lines[lastContent]))
            lastContent--;

        DetectedPrompt = detectedPrompt;

        if (firstContent > lastContent)
            return "(no output)";

        // 4. Build result from content lines
        var sb = new StringBuilder();
        for (int i = firstContent; i <= lastContent; i++)
        {
            var line = lines[i];

            // Also strip any inline prompt from the end of a line
            // e.g. "some output $ " -> "some output"
            line = StripTrailingPrompt(line);

            if (line.Length > 0 || (i > firstContent && i < lastContent))
                sb.AppendLine(line);
        }

        var result = sb.ToString().Trim();
        return result.Length > 0 ? result : "(no output)";
    }

    /// <summary>
    /// Detects the current prompt from the last non-empty line.
    /// </summary>
    private static string DetectPromptFromLine(string[] lines)
    {
        for (int i = lines.Length - 1; i >= 0; i--)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.Length == 0) continue;

            foreach (var pattern in PromptPatterns)
            {
                if (trimmed == pattern || trimmed.EndsWith(pattern))
                    return pattern;
            }
            break;
        }
        return "$ ";
    }

    /// <summary>
    /// Recognized shell/REPL prompts in order of specificity.
    /// </summary>
    private static readonly string[] PromptPatterns =
    {
        "pwndbg> ",   // GDB with pwndbg
        "(gdb) ",     // Plain GDB
        ">>> ",       // Python REPL
        "... ",       // Python continuation
        "# ",         // Root shell
        "$ ",         // User shell (most generic, check last)
    };

    private static bool IsPromptLine(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.Length == 0) return false;

        foreach (var pattern in PromptPatterns)
        {
            if (trimmed == pattern.Trim() || trimmed.EndsWith(pattern.Trim()))
                return true;
        }
        return false;
    }

    private static string StripTrailingPrompt(string line)
    {
        foreach (var pattern in PromptPatterns)
        {
            var p = pattern.Trim();
            if (line.EndsWith(p) && line.Length > p.Length)
            {
                // Only strip if the prompt is at the end preceded by a space
                var idx = line.LastIndexOf(p, StringComparison.Ordinal);
                if (idx > 0 && line[idx - 1] == ' ')
                    return line[..(idx - 1)].TrimEnd();
            }
        }
        return line;
    }

    private static string CollapseBlankLines(string text)
    {
        var lines = text.Split('\n');
        var sb = new StringBuilder();
        int consecutiveBlank = 0;

        foreach (var line in lines)
        {
            if (line.Trim().Length == 0)
            {
                consecutiveBlank++;
                if (consecutiveBlank <= 2)
                    sb.AppendLine();
            }
            else
            {
                consecutiveBlank = 0;
                sb.AppendLine(line);
            }
        }
        return sb.ToString();
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

    private static void DebugLog(string command, string rawOutput)
    {
        try
        {
            var logPath = Path.Combine(AppContext.BaseDirectory, "shell_debug.log");

            // Truncate log if too large
            if (File.Exists(logPath) && new FileInfo(logPath).Length > 100_000)
                File.WriteAllText(logPath, "");

            File.AppendAllText(logPath,
                $"\n=== CMD: {command} ===\n{rawOutput}\n=== END ===\n");
        }
        catch { }
    }

    public void Dispose()
    {
        _writer?.Dispose();
        _shellStream?.Dispose();
    }
}
