using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ElfLens.Core.Services;

namespace ElfLens.Core.ViewModels;

public enum LineType { Function, Instruction, Branch, Call, Ret, Other }
public record HighlightedLine(LineType Type, string Text);

public partial class DisassemblyPanelViewModel : PanelViewModel
{
    private readonly ISshService _sshService;

    public override string Title => "Disassembly";
    public override PanelZone Zone => PanelZone.Center;

    [ObservableProperty] private string _targetPath = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _functionCount = "";

    public ObservableCollection<HighlightedLine> Lines { get; } = new();

    public DisassemblyPanelViewModel(ISshService sshService)
    {
        _sshService = sshService;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (string.IsNullOrWhiteSpace(TargetPath)) return;
        IsBusy = true;
        Lines.Clear();
        try
        {
            var output = await _sshService.ExecuteCommandAsync($"objdump -d \"{TargetPath}\"");
            int funcs = 0;
            foreach (var line in output.Split('\n'))
            {
                if (line.Trim().Length == 0) continue;
                var type = Classify(line);
                if (type == LineType.Function) funcs++;
                Lines.Add(new HighlightedLine(type, line));
            }
            FunctionCount = $"{funcs} functions";
        }
        catch (Exception ex) { Lines.Add(new HighlightedLine(LineType.Other, $"Error: {ex.Message}")); }
        finally { IsBusy = false; }
    }

    private static LineType Classify(string line)
    {
        if (Regex.IsMatch(line, @"^[0-9a-f]+\s+<[^>]+>:$")) return LineType.Function;
        var m = Regex.Match(line, @"^\s*(?:[0-9a-f]+:\s+)?(?:[0-9a-f]{2}\s+)*([a-z]+)\b", RegexOptions.IgnoreCase);
        if (!m.Success) return LineType.Other;
        return m.Groups[1].Value.ToLower() switch
        {
            "call" => LineType.Call,
            "ret" or "retn" or "retf" or "iret" or "iretq" => LineType.Ret,
            var s when s.StartsWith('j') || s is "loop" or "loope" or "loopne" => LineType.Branch,
            _ => LineType.Instruction
        };
    }
}
