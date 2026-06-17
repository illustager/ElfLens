using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ElfLens.Core.Models;
using ElfLens.Core.Services;

namespace ElfLens.Core.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly ISshService _sshService;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private string _connectionStatus = "Disconnected";

    [ObservableProperty]
    private string _connectionHost = string.Empty;

    [ObservableProperty]
    private string _targetBinary = string.Empty;

    public ConnectPageViewModel ConnectPage { get; }
    public WorkspaceViewModel Workspace { get; }
    public ShellPanelViewModel ShellPanel { get; }

    public MainViewModel()
    {
        _sshService = new SshService();
        ConnectPage = new ConnectPageViewModel(_sshService, OnConnectionSucceeded);
        ShellPanel = new ShellPanelViewModel(_sshService);
        ShellPanel.OnDisconnected += () =>
        {
            IsConnected = false;
            ConnectionStatus = "Disconnected";
        };
        Workspace = new WorkspaceViewModel();
    }

    private async void OnConnectionSucceeded(SshConnectionInfo info)
    {
        ConnectionHost = info.Host;
        TargetBinary = info.TargetBinaryPath;
        ConnectionStatus = $"Connecting to {info.Host}...";
        IsConnected = true;

        // Initialize the shell panel in the background
        await Task.Delay(500); // Brief delay for UI to render
        await ShellPanel.InitializeCommand.ExecuteAsync(null);

        ConnectionStatus = $"Connected to {info.Host}";
    }
}
