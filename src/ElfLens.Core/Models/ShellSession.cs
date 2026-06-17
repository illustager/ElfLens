using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Renci.SshNet;

namespace ElfLens.Core.Models;

/// <summary>
/// Wraps an SSH client for non-interactive command execution.
/// Uses exec channels (SshCommand) to get clean output without ANSI escape sequences.
/// </summary>
public class ShellSession : IDisposable
{
    private readonly SshClient _client;

    internal ShellSession(SshClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    /// <summary>
    /// Executes a command on the remote host and returns the combined output.
    /// Uses a non-interactive exec channel — no ANSI escape sequences, no prompts.
    /// </summary>
    public Task<string> ExecuteCommandAsync(string command, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            using var sshCommand = _client.CreateCommand(command);
            sshCommand.CommandTimeout = TimeSpan.FromSeconds(30);

            try
            {
                var result = sshCommand.Execute();

                var sb = new StringBuilder();

                if (!string.IsNullOrEmpty(result))
                {
                    sb.Append(result.TrimEnd());
                }

                if (!string.IsNullOrEmpty(sshCommand.Error))
                {
                    if (sb.Length > 0)
                        sb.AppendLine();
                    sb.Append(sshCommand.Error.TrimEnd());
                }

                if (sshCommand.ExitStatus != 0 && sb.Length == 0)
                {
                    sb.Append($"(exit code: {sshCommand.ExitStatus})");
                }

                return sb.ToString();
            }
            catch (Exception ex)
            {
                return $"(error: {ex.Message})";
            }
        }, ct);
    }

    public void Dispose()
    {
        // SshClient is owned by SshService, don't dispose it here
    }
}
