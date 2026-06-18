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

    public event Action<string>? OnOutput;

    internal ShellSession(ShellStream shellStream)
    {
        _shellStream = shellStream;
        _writer = new StreamWriter(_shellStream) { AutoFlush = true };
        _readCts = new CancellationTokenSource();
        _ = Task.Run(() => ReadLoopAsync(_readCts.Token));
    }

    public async Task SendCommandAsync(string command)
    {
        await _writeLock.WaitAsync();
        try
        {
            if (!command.EndsWith('\n'))
                command += "\n";
            await _writer.WriteAsync(command.AsMemory());
        }
        finally { _writeLock.Release(); }
    }

    /// <summary>
    /// Sends a command and captures output until the stop predicate matches,
    /// output exceeds 8000 chars, the GDB prompt appears, or timeout expires.
    /// </summary>
    public async Task<string> CaptureOutputAsync(
        string command,
        int timeoutMs = 800,
        Func<string, bool>? stopPredicate = null)
    {
        var output = new List<string>();
        var done = new TaskCompletionSource<bool>();

        void Handler(string chunk)
        {
            output.Add(chunk);
            var total = output.Sum(x => x.Length);
            if (total > 8000
                || chunk.Contains("(gdb)") || chunk.Contains("pwndbg>")
                || (stopPredicate?.Invoke(string.Join("", output)) == true))
                done.TrySetResult(true);
        }

        OnOutput += Handler;
        await SendCommandAsync(command);
        await Task.WhenAny(done.Task, Task.Delay(timeoutMs));
        OnOutput -= Handler;

        return string.Join("", output);
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        var buf = new byte[4096];
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
                        raw = AnsiRegex().Replace(raw, "");
                        raw = raw.Replace("\r\n", "\n").Replace("\r", "");
                        if (raw.Length > 0)
                            OnOutput?.Invoke(raw);
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
        _readCts?.Cancel();
        _writer?.Dispose();
        _shellStream?.Dispose();
    }
}
