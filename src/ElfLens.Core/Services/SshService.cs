using System;
using System.Threading.Tasks;
using ElfLens.Core.Models;
using Renci.SshNet;

namespace ElfLens.Core.Services;

public class SshService : ISshService, IDisposable
{
    private SshClient? _client;
    private SshConnectionInfo? _connectionInfo;

    public bool IsConnected => _client?.IsConnected ?? false;
    public SshConnectionInfo? ConnectionInfo => _connectionInfo;

    public async Task<bool> ConnectAsync(SshConnectionInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);

        await DisconnectAsync();

        _connectionInfo = info;

        var connectionInfo = CreateConnectionInfo(info);
        _client = new SshClient(connectionInfo);

        try
        {
            await Task.Run(() => _client.Connect());
            return _client.IsConnected;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ElfLens] SSH connection failed: {ex.Message}");
            _client?.Dispose();
            _client = null;
            return false;
        }
    }

    public async Task<ShellSession?> CreateShellSessionAsync()
    {
        if (_client is not { IsConnected: true })
            return null;

        try
        {
            var shellStream = await Task.Run(() =>
                _client.CreateShellStream(
                    terminalName: "xterm-256color",
                    columns: 200,
                    rows: 40,
                    width: 800,
                    height: 600,
                    bufferSize: 65536));

            return new ShellSession(shellStream);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ElfLens] Failed to create shell session: {ex.Message}");
            return null;
        }
    }

    public async Task DisconnectAsync()
    {
        if (_client != null)
        {
            await Task.Run(() =>
            {
                if (_client.IsConnected)
                    _client.Disconnect();
                _client.Dispose();
            });
            _client = null;
        }
    }

    private static ConnectionInfo CreateConnectionInfo(SshConnectionInfo info)
    {
        AuthenticationMethod auth;

        if (info.AuthMethod == AuthMethod.KeyFile && !string.IsNullOrEmpty(info.KeyFilePath))
        {
            var keyFile = new PrivateKeyFile(info.KeyFilePath);
            auth = new PrivateKeyAuthenticationMethod(info.Username, keyFile);
        }
        else
        {
            auth = new PasswordAuthenticationMethod(info.Username, info.Password ?? string.Empty);
        }

        return new ConnectionInfo(
            info.Host,
            info.Port,
            info.Username,
            auth);
    }

    public void Dispose()
    {
        _client?.Dispose();
        _client = null;
    }
}
