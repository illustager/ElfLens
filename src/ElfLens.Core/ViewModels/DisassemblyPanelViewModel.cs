using System;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ElfLens.Core.Services;

namespace ElfLens.Core.ViewModels;

public partial class DisassemblyPanelViewModel : PanelViewModel
{
    private readonly ISshService _sshService;

    public override string Title => "Disassembly";
    public override PanelZone Zone => PanelZone.Center;

    [ObservableProperty] private string _targetPath = "";
    [ObservableProperty] private string _outputText = "";
    [ObservableProperty] private bool _isBusy;

    public DisassemblyPanelViewModel(ISshService sshService)
    {
        _sshService = sshService;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (string.IsNullOrWhiteSpace(TargetPath)) return;
        IsBusy = true;
        try
        {
            OutputText = $"$ objdump -d \"{TargetPath}\"\n";
            OutputText += await _sshService.ExecuteCommandAsync($"objdump -d \"{TargetPath}\"");
        }
        catch (Exception ex) { OutputText += $"\n!!! {ex.Message}"; }
        finally { IsBusy = false; }
    }
}
