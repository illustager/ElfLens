using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ElfLens.Core.Models;
using ElfLens.Core.Services;

namespace ElfLens.Core.ViewModels;

public partial class ConnectPageViewModel : ViewModelBase
{
    private readonly ISshService _sshService;
    private readonly Action<SshConnectionInfo>? _onConnected;

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

    [ObservableProperty]
    private bool _isConnecting;

    public ConnectPageViewModel() : this(new SshService(), null) { }

    public ConnectPageViewModel(ISshService sshService, Action<SshConnectionInfo>? onConnected)
    {
        _sshService = sshService ?? throw new ArgumentNullException(nameof(sshService));
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

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync()
    {
        var info = BuildConnectionInfo();

        if (!info.IsValid)
        {
            ErrorMessage = "Please fill in all required fields.";
            return;
        }

        ErrorMessage = null;
        IsConnecting = true;

        try
        {
            var connected = await _sshService.ConnectAsync(info);

            if (connected)
            {
                _onConnected?.Invoke(info);
            }
            else
            {
                ErrorMessage = "Failed to connect. Check host, credentials, and network.";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Connection error: {ex.Message}";
        }
        finally
        {
            IsConnecting = false;
        }
    }

    private bool CanConnect() => !IsConnecting;
}
