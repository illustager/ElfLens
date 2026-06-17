using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ElfLens.Core.Models;

namespace ElfLens.Core.ViewModels;

public partial class ConnectPageViewModel : ViewModelBase
{
    private readonly Action<string>? _onConnected;

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

    public ConnectPageViewModel() : this(null) { }

    public ConnectPageViewModel(Action<string>? onConnected)
    {
        _onConnected = onConnected;
    }

    partial void OnUsePasswordAuthChanged(bool value)
    {
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

        var authInfo = info.AuthMethod == AuthMethod.Password
            ? $"password (length={info.Password?.Length ?? 0})"
            : $"key file: {info.KeyFilePath}";

        System.Diagnostics.Debug.WriteLine(
            $"[ElfLens] Connecting to {info.Username}@{info.Host}:{info.Port} using {authInfo}");
        System.Diagnostics.Debug.WriteLine(
            $"[ElfLens] Target binary: {info.TargetBinaryPath}");

        _onConnected?.Invoke(info.Host);
    }
}
