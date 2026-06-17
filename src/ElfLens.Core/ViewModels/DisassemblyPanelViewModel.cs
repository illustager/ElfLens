using System;
using System.Text.RegularExpressions;
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
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _functionCount = "";
    [ObservableProperty] private string _outputText = "Loading...";

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
            var output = await _sshService.ExecuteCommandAsync($"objdump -d \"{TargetPath}\"");
            OutputText = output;
            FunctionCount = $"{CountFunctions(output)} functions";
        }
        catch (Exception ex) { OutputText = $"Error: {ex.Message}"; }
        finally { IsBusy = false; }
    }

    private static int CountFunctions(string text)
    {
        return Regex.Matches(text, @"^[0-9a-f]+\s+<[^>]+>:$", RegexOptions.Multiline).Count;
    }
}
