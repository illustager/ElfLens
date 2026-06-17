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
    private volatile bool _disposed;

    public event Action<string>? OnOutput;
    public event Action? OnDisconnected;

    internal ShellSession(ShellStream shellStream, Func<bool>? isConnected = null)
    {
        _shellStream = shellStream;
        _writer = new StreamWriter(_shellStream) { AutoFlush = true };
        _readCts = new CancellationTokenSource();
        _ = Task.Run(() => ReadLoopAsync(_readCts.Token, isConnected));
    }

    public async Task SendCommandAsync(string command)
    {
        if (_disposed) return;
        await _writeLock.WaitAsync();
        try
        {
            if (_disposed) return;
            if (!command.EndsWith('\n'))
                command += "\n";
            await _writer.WriteAsync(command.AsMemory());
        }
        catch (ObjectDisposedException) { }
        finally { _writeLock.Release(); }
    }

    private async Task ReadLoopAsync(CancellationToken ct, Func<bool>? isConnected)
    {
        var buf = new byte[4096];
        try
        {
            while (!ct.IsCancellationRequested)
            {
                try
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
                        // Check if underlying SSH connection is still alive
                        if (isConnected?.Invoke() == false) break;
                        await Task.Delay(30, ct);
                    }
                }
                catch (ObjectDisposedException) { break; }
                catch (OperationCanceledException) { break; }
                catch { break; }
            }
        }
        finally
        {
            _disposed = true;
            OnDisconnected?.Invoke();
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

    public void Dispose()
    {
        _disposed = true;
        _readCts?.Cancel();
        _writer?.Dispose();
        _shellStream?.Dispose();
    }
}
