namespace ElfLens.Core.Models;

public enum AuthMethod
{
    Password,
    KeyFile
}

public class SshConnectionInfo
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 22;
    public string Username { get; set; } = string.Empty;
    public AuthMethod AuthMethod { get; set; } = AuthMethod.Password;
    public string? Password { get; set; }
    public string? KeyFilePath { get; set; }
    public string TargetBinaryPath { get; set; } = string.Empty;

    public bool IsValid =>
        !string.IsNullOrWhiteSpace(Host) &&
        Port is > 0 and <= 65535 &&
        !string.IsNullOrWhiteSpace(Username) &&
        (AuthMethod == AuthMethod.Password
            ? !string.IsNullOrEmpty(Password)
            : !string.IsNullOrEmpty(KeyFilePath)) &&
        !string.IsNullOrWhiteSpace(TargetBinaryPath);
}
