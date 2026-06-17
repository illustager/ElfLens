using System;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ElfLens.Core.Services;

namespace ElfLens.Core.ViewModels;

public partial class FileInfoPanelViewModel : PanelViewModel
{
    private readonly ISshService _sshService;

    public override string Title => "File Info";
    public override PanelZone Zone => PanelZone.Right;

    [ObservableProperty] private string _targetPath = "";
    [ObservableProperty] private string _outputText = "";
    [ObservableProperty] private bool _isBusy;

    public FileInfoPanelViewModel(ISshService sshService)
    {
        _sshService = sshService;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (string.IsNullOrWhiteSpace(TargetPath)) return;
        IsBusy = true;
        var sb = new StringBuilder();
        try
        {
            sb.AppendLine($"$ file \"{TargetPath}\"");
            sb.AppendLine(await _sshService.ExecuteCommandAsync($"file \"{TargetPath}\""));
            sb.AppendLine();
            sb.AppendLine($"$ readelf -h \"{TargetPath}\"");
            sb.AppendLine(await _sshService.ExecuteCommandAsync($"readelf -h \"{TargetPath}\""));
            sb.AppendLine();
            sb.AppendLine($"$ readelf -S \"{TargetPath}\"");
            sb.AppendLine(await _sshService.ExecuteCommandAsync($"readelf -S \"{TargetPath}\""));
        }
        catch (Exception ex) { sb.AppendLine($"!!! {ex.Message}"); }
        finally { IsBusy = false; }
        OutputText = sb.ToString();
    }
}
