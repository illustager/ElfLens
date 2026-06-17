using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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

    public ObservableCollection<FunctionItem> Functions { get; } = new();

    public DisassemblyPanelViewModel(ISshService sshService)
    {
        _sshService = sshService;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (string.IsNullOrWhiteSpace(TargetPath)) return;
        IsBusy = true;
        Functions.Clear();
        try
        {
            var output = await _sshService.ExecuteCommandAsync($"objdump -d \"{TargetPath}\"");
            ParseDisassembly(output);
        }
        catch (Exception ex)
        {
            Functions.Add(new FunctionItem($"Error: {ex.Message}", "", new List<string>()));
        }
        finally
        {
            IsBusy = false;
            FunctionCount = $"{Functions.Count} functions";
        }
    }

    private static readonly Regex FuncHeaderRegex =
        new(@"^([0-9a-f]+)\s+<([^>]+)>:$", RegexOptions.Compiled);

    private void ParseDisassembly(string output)
    {
        FunctionItem? current = null;
        var instructions = new List<string>();

        foreach (var line in output.Split('\n'))
        {
            var m = FuncHeaderRegex.Match(line);
            if (m.Success)
            {
                if (current != null)
                {
                    current.Instructions = instructions;
                    Functions.Add(current);
                }
                current = new FunctionItem(m.Groups[2].Value, m.Groups[1].Value, new List<string>());
                instructions = new List<string>();
                continue;
            }

            var trimmed = line.Trim();
            if (trimmed.Length > 0 && current != null)
                instructions.Add(trimmed);
        }

        if (current != null)
        {
            current.Instructions = instructions;
            Functions.Add(current);
        }
    }
}

public partial class FunctionItem : ObservableObject
{
    public string Name { get; }
    public string Address { get; }
    public IList<string> Instructions { get; set; }

    [ObservableProperty] private bool _isExpanded;

    public string Summary =>
        $"{Address}: {Name}  ({Instructions?.Count ?? 0} instructions)";

    public FunctionItem(string name, string address, IList<string> instructions)
    {
        Name = name;
        Address = address;
        Instructions = instructions;
    }

    [RelayCommand]
    private void Toggle() => IsExpanded = !IsExpanded;
}
