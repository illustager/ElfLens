using System;
using System.Threading.Tasks;
using ElfLens.Core.Models;

namespace ElfLens.Core.Services;

/// <summary>
/// Service for managing SSH connections to remote Linux hosts.
/// </summary>
public interface ISshService
{
    /// <summary>
    /// Whether the SSH client is currently connected.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// The connection info used for the current or last connection.
    /// </summary>
    SshConnectionInfo? ConnectionInfo { get; }

    /// <summary>
    /// Establishes an SSH connection using the provided connection info.
    /// </summary>
    Task<bool> ConnectAsync(SshConnectionInfo info);

    /// <summary>
    /// Creates a new shell session for executing commands.
    /// Each call creates an independent shell stream.
    /// </summary>
    Task<ShellSession?> CreateShellSessionAsync();

    /// <summary>
    /// Disconnects the SSH client.
    /// </summary>
    Task DisconnectAsync();
}
