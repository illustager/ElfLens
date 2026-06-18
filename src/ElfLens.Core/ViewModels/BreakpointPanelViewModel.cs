using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ElfLens.Core.Models;

namespace ElfLens.Core.ViewModels;

public partial class BreakpointEntry : ObservableObject
{
    public string Location { get; init; } = "";
    [ObservableProperty] private bool _enabled = true;
    [ObservableProperty] private string _gdbNum = ""; // GDB-assigned number after execution
    [ObservableProperty] private string _resolvedAddr = ""; // resolved address from GDB
    [ObservableProperty] private string _resolvedFunc = ""; // resolved function name
}

public partial class BreakpointPanelViewModel : SessionPanelViewModel
{
    public override string Title => "Breakpoints";
    public override PanelZone Zone => PanelZone.Right;

    [ObservableProperty] private string _newBreakpoint = "";

    public ObservableCollection<BreakpointEntry> Entries { get; } = new();

    private Action? _onBreakpointsChanged;

    public override void SetSession(ShellSession? session)
    {
        base.SetSession(session);
        if (session != null)
            _ = BatchApplyAsync();
    }

    public void OnChanged(Action callback) => _onBreakpointsChanged = callback;

    [RelayCommand]
    private void Add()
    {
        var loc = NewBreakpoint.Trim();
        if (loc.Length == 0) return;
        // Auto-prefix plain function names with * (GDB address-of syntax)
        if (!loc.StartsWith('*') && !loc.StartsWith("0x") && !char.IsDigit(loc[0]))
            loc = "*" + loc;
        var entry = new BreakpointEntry { Location = loc };
        Entries.Add(entry);
        NewBreakpoint = "";
        NotifyChanged();
        if (Session != null) _ = ApplySingleAsync(loc, entry);
    }

    /// <summary>Add breakpoint from disassembly panel right-click (func+offset).</summary>
    public void AddFromDisasm(string func, int offset)
    {
        var loc = offset == 0 ? $"*{func}" : $"*{func}+{offset}";
        var entry = new BreakpointEntry { Location = loc };
        Entries.Add(entry);
        NotifyChanged();
        if (Session != null) _ = ApplySingleAsync(loc, entry);
    }

    [RelayCommand]
    private void Remove(BreakpointEntry? entry)
    {
        if (entry == null) return;
        if (Session != null && entry.GdbNum.Length > 0)
            _ = Session.SendCommandAsync($"delete {entry.GdbNum}");
        Entries.Remove(entry);
        NotifyChanged();
    }

    [RelayCommand]
    private void Toggle(BreakpointEntry? entry)
    {
        if (entry == null) return;
        entry.Enabled = !entry.Enabled;
        if (Session != null && entry.GdbNum.Length > 0)
            _ = Session.SendCommandAsync(entry.Enabled
                ? $"enable {entry.GdbNum}"
                : $"disable {entry.GdbNum}");
        NotifyChanged();
    }

    private async Task BatchApplyAsync()
    {
        if (Session == null) return;
        foreach (var e in Entries.Where(e => e.Enabled))
            await ApplySingleAsync(e.Location, e);
        await RefreshFromGdbAsync();
    }

    private async Task ApplySingleAsync(string loc, BreakpointEntry? entry = null)
    {
        if (Session == null) return;
        var text = await Session.CaptureOutputAsync($"break {loc}",
            stopPredicate: s => s.Contains("Breakpoint"));

        // Parse breakpoint number from output like "Breakpoint 1 at 0x401000"
        if (entry != null)
        {
            var m = Regex.Match(text,
                @"Breakpoint\s+(\d+)\s+at\s+(0x[0-9a-fA-F]+)(?:\s+in\s+(\S+))?");
            if (m.Success)
            {
                entry.GdbNum = m.Groups[1].Value;
                entry.ResolvedAddr = m.Groups[2].Value;
                if (m.Groups[3].Success) entry.ResolvedFunc = m.Groups[3].Value;
            }
        }
    }

    public async Task RefreshFromGdbAsync()
    {
        if (Session == null || !HasSession) return;
        var output = await Session.CaptureOutputAsync("info breakpoints",
            stopPredicate: s => s.Contains("No breakpoints"));

        if (output.Contains("No breakpoints"))
        {
            foreach (var e in Entries) { e.GdbNum = ""; e.ResolvedAddr = ""; e.ResolvedFunc = ""; }
            return;
        }

        // Parse: "1   breakpoint     keep y   0x00007ffff7fe3290 in _start at ..."
        var rx = new Regex(@"^(\d+)\s+breakpoint\s+keep\s+([yn])\s+(0x[0-9a-f]+)\s+in\s+(\S+)",
            RegexOptions.Multiline | RegexOptions.IgnoreCase);
        foreach (Match m in rx.Matches(output))
        {
            var addr = m.Groups[3].Value;
            var func = m.Groups[4].Value;
            var enabled = m.Groups[2].Value == "y";
            // Try to match against our entries by resolved function
            foreach (var e in Entries)
            {
                if (e.Location.Contains(func) || e.Location == func || e.Location == "*" + addr)
                {
                    e.GdbNum = m.Groups[1].Value;
                    e.ResolvedAddr = addr;
                    e.ResolvedFunc = func;
                    e.Enabled = enabled;
                }
            }
        }
        NotifyChanged();
    }

    private void NotifyChanged() => _onBreakpointsChanged?.Invoke();

    /// <summary>Returns (func, offset, enabled) tuples for marking breakpoints on disassembly.</summary>
    public List<(string func, int offset, bool enabled)> GetFuncBreakpoints()
    {
        var result = new List<(string, int, bool)>();
        foreach (var e in Entries)
        {
            var loc = e.Location;
            // func+offset (with optional * prefix)
            var m = Regex.Match(loc, @"^\*?([a-zA-Z_]\w*)\+(\d+)$");
            if (m.Success) { result.Add((m.Groups[1].Value, int.Parse(m.Groups[2].Value), e.Enabled)); continue; }
            // func only (with optional * prefix)
            var m2 = Regex.Match(loc, @"^\*?([a-zA-Z_]\w*)$");
            if (m2.Success) { result.Add((m2.Groups[1].Value, 0, e.Enabled)); continue; }
            // *addr — skip for marking
        }
        return result;
    }
}
