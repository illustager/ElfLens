using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ElfLens.Core.Models;

namespace ElfLens.Core.ViewModels;

public partial class ConnectPageViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _host = string.Empty;

    [ObservableProperty]
    private int _port = 22;

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private bool _usePasswordAuth = true;

    [ObservableProperty]
    private string? _password;

    [ObservableProperty]
    private string? _keyFilePath;

    [ObservableProperty]
    private string _targetBinaryPath = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    partial void OnUsePasswordAuthChanged(bool value)
    {
        // Clear the inactive auth method's value
        if (value)
            KeyFilePath = null;
        else
            Password = null;
        ErrorMessage = null;
    }

    public SshConnectionInfo BuildConnectionInfo() => new()
    {
        Host = Host?.Trim() ?? string.Empty,
        Port = Port,
        Username = Username?.Trim() ?? string.Empty,
        AuthMethod = UsePasswordAuth ? AuthMethod.Password : AuthMethod.KeyFile,
        Password = UsePasswordAuth ? Password : null,
        KeyFilePath = UsePasswordAuth ? null : KeyFilePath,
        TargetBinaryPath = TargetBinaryPath?.Trim() ?? string.Empty,
    };

    [RelayCommand]
    private void Connect()
    {
        var info = BuildConnectionInfo();

        if (!info.IsValid)
        {
            ErrorMessage = "Please fill in all required fields.";
            return;
        }

        ErrorMessage = null;

        // Step 1: just log connection info to debug output
        var authInfo = info.AuthMethod == AuthMethod.Password
            ? $"password (length={info.Password?.Length ?? 0})"
            : $"key file: {info.KeyFilePath}";

        System.Diagnostics.Debug.WriteLine(
            $"[ElfLens] Connecting to {info.Username}@{info.Host}:{info.Port} using {authInfo}");
        System.Diagnostics.Debug.WriteLine(
            $"[ElfLens] Target binary: {info.TargetBinaryPath}");
    }
}
