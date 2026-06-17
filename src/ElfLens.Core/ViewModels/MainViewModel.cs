using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using ElfLens.Core.Models;
using ElfLens.Core.Services;

namespace ElfLens.Core.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly ISshService _sshService;

    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private string _connectionStatus = "Disconnected";
    [ObservableProperty] private string _connectionHost = string.Empty;
    [ObservableProperty] private string _targetBinary = string.Empty;

    public ConnectPageViewModel ConnectPage { get; }
    public WorkspaceViewModel Workspace { get; }
    public ShellPanelViewModel ShellPanel { get; }

    public ObservableCollection<PanelViewModel> LeftPanels { get; } = new();
    public ObservableCollection<PanelViewModel> CenterPanels { get; } = new();
    public ObservableCollection<PanelViewModel> RightPanels { get; } = new();
    public ObservableCollection<PanelViewModel> BottomPanels { get; } = new();

    public MainViewModel()
    {
        _sshService = new SshService();
        ConnectPage = new ConnectPageViewModel(_sshService, OnConnectionSucceeded);
        ShellPanel = new ShellPanelViewModel(_sshService);
        Workspace = new WorkspaceViewModel();

        BottomPanels.Add(ShellPanel);
    }

    private async void OnConnectionSucceeded(SshConnectionInfo info)
    {
        ConnectionHost = info.Host;
        TargetBinary = info.TargetBinaryPath;
        ConnectionStatus = $"Connecting to {info.Host}...";
        IsConnected = true;

        await Task.Delay(500);
        await ShellPanel.InitializeCommand.ExecuteAsync(null);

        ConnectionStatus = $"Connected to {info.Host}";
    }

    public void AddPanel(PanelViewModel panel)
    {
        switch (panel.Zone)
        {
            case PanelZone.Left: LeftPanels.Add(panel); break;
            case PanelZone.Center: CenterPanels.Add(panel); break;
            case PanelZone.Right: RightPanels.Add(panel); break;
            case PanelZone.Bottom: BottomPanels.Add(panel); break;
        }
    }

    public void RemovePanel(PanelViewModel panel)
    {
        switch (panel.Zone)
        {
            case PanelZone.Left: LeftPanels.Remove(panel); break;
            case PanelZone.Center: CenterPanels.Remove(panel); break;
            case PanelZone.Right: RightPanels.Remove(panel); break;
            case PanelZone.Bottom: BottomPanels.Remove(panel); break;
        }
    }
}
