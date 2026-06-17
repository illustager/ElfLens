using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ElfLens.Core.Services;

namespace ElfLens.Core.ViewModels;

public record HighlightedLine(List<Token> Tokens);

public partial class FunctionItem : ObservableObject
{
    public string Name { get; }
    public string Address { get; }
    public IList<HighlightedLine> Instructions { get; set; }
    [ObservableProperty] private bool _isExpanded;

    public FunctionItem(string name, string addr, IList<HighlightedLine> insts)
    {
        Name = name; Address = addr; Instructions = insts;
    }

    [RelayCommand]
    private void Toggle() => IsExpanded = !IsExpanded;
}

public partial class DisassemblyPanelViewModel : PanelViewModel
{
    private readonly ISshService _sshService;

    public override string Title => "Disassembly";
    public override PanelZone Zone => PanelZone.Center;

    [ObservableProperty] private string _targetPath = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _functionCount = "";

    public ObservableCollection<FunctionItem> Functions { get; } = new();

    public DisassemblyPanelViewModel(ISshService sshService) => _sshService = sshService;

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (string.IsNullOrWhiteSpace(TargetPath)) return;
        IsBusy = true;
        Functions.Clear();
        try
        {
            var output = await _sshService.ExecuteCommandAsync($"objdump -d \"{TargetPath}\"");
            Parse(output);
        }
        catch (Exception ex)
        {
            Functions.Add(new FunctionItem("Error", "", new List<HighlightedLine>
                { new(new List<Token> { new(ex.Message, "#EF5350") }) }));
        }
        finally { IsBusy = false; }
    }

    private void Parse(string output)
    {
        FunctionItem? cur = null;
        var insts = new List<HighlightedLine>();
        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0) continue;

            var fm = Regex.Match(line, @"^([0-9a-f]+)\s+<([^>]+)>:$");
            if (fm.Success)
            {
                if (cur != null) { cur.Instructions = insts; Functions.Add(cur); }
                cur = new FunctionItem(fm.Groups[2].Value, fm.Groups[1].Value, new List<HighlightedLine>());
                insts = new List<HighlightedLine>();
                continue;
            }
            if (cur != null)
                insts.Add(new HighlightedLine(DisassemblyHighlighter.Tokenize(line)));
        }
        if (cur != null) { cur.Instructions = insts; Functions.Add(cur); }
        FunctionCount = $"{Functions.Count} functions";
    }
}
