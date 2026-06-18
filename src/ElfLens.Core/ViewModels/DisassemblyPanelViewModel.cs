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

public record HighlightedLine(List<Token> Tokens, bool IsCurrent = false, bool IsBreakpoint = false, bool IsBreakpointDisabled = false);

public partial class FunctionItem : ObservableObject
{
    public string Name { get; }
    public string Address { get; }
    public IList<HighlightedLine> Instructions { get; set; }
    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _isCurrent;

    public FunctionItem(string name, string addr, IList<HighlightedLine> insts)
    { Name = name; Address = addr; Instructions = insts; }

    [RelayCommand] private void Toggle() => IsExpanded = !IsExpanded;

    /// <summary>Mark instruction lines matching (func, offset, enabled) breakpoints in-place.</summary>
    public static void MarkBreakpoints(IList<FunctionItem> items, List<(string func, int offset, bool enabled)> bps)
    {
        for (int bi = 0; bi < items.Count; bi++)
        {
            var fb = items[bi];
            if (!long.TryParse(fb.Address, System.Globalization.NumberStyles.HexNumber, null, out var baseAddr))
                continue;
            var changed = false;
            var newInsts = new List<HighlightedLine>();
            foreach (var line in fb.Instructions)
            {
                var isBp = false;
                var isDisabled = false;
                var firstText = line.Tokens.FirstOrDefault()?.Text ?? "";
                var addrM = Regex.Match(firstText, @"([0-9a-fA-F]+)");
                if (addrM.Success && long.TryParse(addrM.Groups[1].Value,
                    System.Globalization.NumberStyles.HexNumber, null, out var instAddr))
                {
                    var byteOffset = (int)(instAddr - baseAddr);
                    foreach (var bp in bps)
                    {
                        if (fb.Name == bp.func && byteOffset == bp.offset)
                        { isBp = true; isDisabled = !bp.enabled; break; }
                    }
                }
                if (isBp != line.IsBreakpoint || isDisabled != line.IsBreakpointDisabled) changed = true;
                newInsts.Add(new HighlightedLine(line.Tokens, line.IsCurrent, isBp, isDisabled));
            }
            if (changed)
                items[bi] = new FunctionItem(fb.Name, fb.Address, newInsts) { IsExpanded = fb.IsExpanded };
        }
    }
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
    public event Action<FunctionItem>? NavigateToFunction;
    public event Action<string, int>? BreakpointRequested;

    public void RequestBreakpoint(string func, int offset) =>
        BreakpointRequested?.Invoke(func, offset);

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
            ParseObjdump(output);
        }
        catch (Exception ex)
        {
            Functions.Add(new FunctionItem("Error", "", new List<HighlightedLine>
                { new(new List<Token> { new(ex.Message, "#EF5350") }) }));
        }
        finally { IsBusy = false; }
    }

    /// <summary>Highlight a function by name/address from external source (e.g. GDB panel).</summary>
    public void HighlightFunction(string? name, string? addr)
    {
        foreach (var f in Functions) f.IsCurrent = false;
        var fn = Functions.FirstOrDefault(f =>
            (!string.IsNullOrEmpty(name) && f.Name == name) ||
            (!string.IsNullOrEmpty(addr) && f.Address == addr));
        if (fn != null) fn.IsCurrent = true;
    }

    /// <summary>Check if a function name/address exists in this panel.</summary>
    public bool HasFunction(string? name) =>
        Functions.Any(f => f.Name == name);

    /// <summary>Mark instruction lines matching (func, offset) breakpoints.</summary>
    public void MarkBreakpoints(List<(string func, int offset, bool enabled)> bps) =>
        FunctionItem.MarkBreakpoints(Functions, bps);

    private void ParseObjdump(string output, string? highlightPc = null)
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
            {
                var isCur = highlightPc != null && line.Contains(highlightPc);
                insts.Add(new HighlightedLine(DisassemblyHighlighter.Tokenize(line), isCur));
            }
        }
        if (cur != null) { cur.Instructions = insts; Functions.Add(cur); }
        FunctionCount = $"{Functions.Count} functions";
    }

    [RelayCommand]
    private void Navigate(string? target)
    {
        if (string.IsNullOrEmpty(target)) return;
        var fn = Functions.FirstOrDefault(f => f.Name == target || f.Address == target);
        if (fn != null) { fn.IsExpanded = true; NavigateToFunction?.Invoke(fn); }
    }
}
