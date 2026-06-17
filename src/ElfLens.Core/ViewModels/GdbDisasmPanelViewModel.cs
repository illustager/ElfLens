using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ElfLens.Core.Models;
using ElfLens.Core.Services;

namespace ElfLens.Core.ViewModels;

public partial class GdbDisasmPanelViewModel : PanelViewModel
{
    private readonly ISshService _sshService;
    private ShellSession? _session;
    private readonly DisassemblyPanelViewModel _staticDisasm;

    public override string Title => "GDB";
    public override PanelZone Zone => PanelZone.Center;

    [ObservableProperty] private string _targetPath = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isDebugging;
    [ObservableProperty] private string _currentFunction = "";
    [ObservableProperty] private string _currentPc = "";

    partial void OnCurrentPcChanged(string value)
    {
        UpdateHighlight(value);
    }

    private void UpdateHighlight(string pc)
    {
        for (int bi = 0; bi < FunctionBlocks.Count; bi++)
        {
            var fb = FunctionBlocks[bi];
            var changed = false;
            var newInsts = new List<HighlightedLine>();
            foreach (var line in fb.Instructions)
            {
                var first = line.Tokens.FirstOrDefault();
                var isCur = first?.Text.Trim()
                    .Contains(pc, StringComparison.OrdinalIgnoreCase) == true;
                if (isCur != line.IsCurrent) changed = true;
                newInsts.Add(new HighlightedLine(line.Tokens, isCur));
            }
            if (changed)
                FunctionBlocks[bi] = new FunctionItem(fb.Name, fb.Address, newInsts);
        }
    }

    public ObservableCollection<FunctionItem> FunctionBlocks { get; } = new();
    public event Action<ShellSession?, string>? SessionChanged;

    public GdbDisasmPanelViewModel(ISshService sshService, DisassemblyPanelViewModel staticDisasm)
    {
        _sshService = sshService;
        _staticDisasm = staticDisasm;
    }

    [RelayCommand]
    private async Task StartDebuggingAsync()
    {
        if (string.IsNullOrWhiteSpace(TargetPath)) return;
        IsBusy = true;
        try
        {
            _session = await _sshService.CreateShellSessionAsync();
            if (_session == null) return;

            await _session.SendCommandAsync($"gdb -q \"{TargetPath}\"");
            await Task.Delay(800);
            IsDebugging = true;
            SessionChanged?.Invoke(_session, "GDB");

            await Task.Delay(300);
            await _session.SendCommandAsync("break _start");
            await Task.Delay(200);
            await _session.SendCommandAsync("run");
            await Task.Delay(500);
            await RefreshAsync();
        }
        catch { }
        finally { IsBusy = false; }
    }

    [RelayCommand] private async Task StepIntoAsync() => await Step("stepi");
    [RelayCommand] private async Task StepOverAsync() => await Step("nexti");
    [RelayCommand] private async Task ContinueAsync() => await Step("continue");
    [RelayCommand] private async Task RestartAsync() => await Step("run");

    [RelayCommand]
    private async Task StopAsync()
    {
        if (_session == null) return;
        await _session.SendCommandAsync("kill");
        await Task.Delay(200);
        _session.Dispose();
        _session = null;
        SessionChanged?.Invoke(null, "");
        IsDebugging = false;
        FunctionBlocks.Clear();
        CurrentFunction = "";
        CurrentPc = "";
        _staticDisasm.HighlightFunction(null, null);
    }

    private async Task Step(string cmd)
    {
        if (_session == null) return;
        await _session.SendCommandAsync(cmd);
        await Task.Delay(80);
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (_session == null) return;
        IsBusy = true;
        try
        {
            var all = await Capture("info registers pc");
            var pcM = Regex.Match(all, @"0x([0-9a-f]+)");
            var pcAddr = pcM.Success ? pcM.Groups[1].Value : "";

            var nameM = Regex.Match(all, @"<([a-zA-Z_]\w*)(?:\+\d+)?>");
            var funcName = nameM.Success ? nameM.Groups[1].Value
                : (pcAddr.Length > 0 ? "0x" + pcAddr : "??");
            CurrentFunction = funcName;

            if (!FunctionBlocks.Any(f => f.Name == funcName))
            {
                var asm = string.IsNullOrEmpty(pcAddr)
                    ? await Capture("disassemble /r")
                    : await Capture($"disassemble /r 0x{pcAddr}");
                var block = ParseGdbBlock(asm, funcName, pcAddr);
                if (block != null)
                {
                    block.IsExpanded = true;
                    FunctionBlocks.Add(block);
                }
            }

            CurrentPc = pcAddr;

            if (_staticDisasm.HasFunction(funcName))
                _staticDisasm.HighlightFunction(funcName, pcAddr);
            else
                _staticDisasm.HighlightFunction(null, null);
        }
        catch { }
        finally { IsBusy = false; }
    }

    private async Task<string> Capture(string cmd)
    {
        if (_session == null) return "";
        var sb = new List<string>();
        var done = new TaskCompletionSource<bool>();
        var collecting = false;
        void H(string c)
        {
            if (!collecting) return;
            sb.Add(c);
            if (sb.Any(s => s.Contains("End of assembler dump.")) ||
                sb.Sum(s => s.Length) > 50000)
                done.TrySetResult(true);
        }
        _session.OnOutput += H;
        collecting = true;
        await _session.SendCommandAsync(cmd);
        await Task.WhenAny(done.Task, Task.Delay(500));
        _session.OnOutput -= H;
        return string.Join("", sb);
    }

    private FunctionItem? ParseGdbBlock(string output, string funcName, string? currentPc)
    {
        var insts = new List<HighlightedLine>();
        bool inDump = false;
        string? firstAddr = null;

        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0) continue;
            if (!inDump && !trimmed.Contains("Dump of assembler")) continue;
            if (trimmed == "End of assembler dump.") { inDump = false; continue; }
            if (trimmed.StartsWith("pwndbg") || trimmed.EndsWith("pwndbg>")) continue;
            if (trimmed == "disassemble" || trimmed.StartsWith("disassemble /r")) continue;
            if (trimmed.StartsWith("info ")) continue;

            if (Regex.IsMatch(line, @"^Dump of assembler code for function\s+(.+):$"))
            {
                inDump = true;
                continue;
            }
            if (!inDump) continue;

            var gdbInst = Regex.Match(line, @"^\s*(?:=>\s*)?(0x[0-9a-f]+)\s+<\+(\d+)>:\s+(.*)$");
            if (gdbInst.Success)
            {
                var addr = gdbInst.Groups[1].Value[2..];
                firstAddr ??= addr;
                var body = gdbInst.Groups[3].Value;
                // Use spaces not tabs — matches objdump format that Tokenize expects
                var normalized = $"  {addr}:\t{body}".Replace('\t', ' ');
                insts.Add(new HighlightedLine(DisassemblyHighlighter.Tokenize(normalized)));
            }
        }

        if (insts.Count == 0) return null;
        return new FunctionItem(funcName, firstAddr ?? "", insts);
    }
}
