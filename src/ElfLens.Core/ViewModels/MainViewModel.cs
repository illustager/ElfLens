using CommunityToolkit.Mvvm.ComponentModel;

namespace ElfLens.Core.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private string _connectionStatus = "Disconnected";

    [ObservableProperty]
    private string _connectionHost = string.Empty;

    public ConnectPageViewModel ConnectPage { get; }
    public WorkspaceViewModel Workspace { get; }

    public MainViewModel()
    {
        ConnectPage = new ConnectPageViewModel(OnConnectionSucceeded);
        Workspace = new WorkspaceViewModel();
    }

    private void OnConnectionSucceeded(string host)
    {
        IsConnected = true;
        ConnectionHost = host;
        ConnectionStatus = $"Connected to {host}";
    }
}
