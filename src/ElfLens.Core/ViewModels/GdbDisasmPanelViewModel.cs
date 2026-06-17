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
        _lastFunc = "";
        _staticDisasm.HighlightFunction(null, null);
    }

    private async Task Step(string cmd)
    {
        if (_session == null) return;
        await _session.SendCommandAsync(cmd);
        await Task.Delay(80);
        await RefreshAsync();
    }

    private string _lastFunc = "";

    private async Task RefreshAsync()
    {
        if (_session == null) return;
        IsBusy = true;
        try
        {
            var pc = await Capture("info registers pc");
            var pcM = Regex.Match(pc, @"0x([0-9a-f]+)");
            var pcAddr = pcM.Success ? pcM.Groups[1].Value : "";

            var fnOut = await Capture("info symbol $pc");
            var fnM = Regex.Match(fnOut, @"\b([a-zA-Z_]\w+)\b");
            var funcName = fnM.Success ? fnM.Groups[1].Value : "";
            CurrentFunction = funcName;

            // Only disassemble when entering a new function
            if (funcName != _lastFunc || string.IsNullOrEmpty(funcName))
            {
                _lastFunc = funcName;
                var asm = string.IsNullOrEmpty(pcAddr)
                    ? await Capture("disassemble /r")
                    : await Capture($"disassemble /r 0x{pcAddr}");
                var block = ParseGdbBlock(asm, funcName, pcAddr);
                if (block != null) FunctionBlocks.Add(block);
            }
            else
            {
                // Same function — clear all then re-highlight current
                ClearAllHighlights();
                var last = FunctionBlocks.LastOrDefault();
                if (last != null) HighlightInBlock(last, pcAddr);
            }

            if (_staticDisasm.HasFunction(funcName))
                _staticDisasm.HighlightFunction(funcName, pcAddr);
            else
                _staticDisasm.HighlightFunction(null, null);
        }
        catch { }
        finally { IsBusy = false; }
    }

    private void ClearAllHighlights()
    {
        foreach (var fb in FunctionBlocks)
        {
            for (int i = 0; i < fb.Instructions.Count; i++)
            {
                if (fb.Instructions[i].IsCurrent)
                    fb.Instructions[i] = new HighlightedLine(fb.Instructions[i].Tokens, false);
            }
        }
    }

    private void HighlightInBlock(FunctionItem block, string pcAddr)
    {
        for (int i = 0; i < block.Instructions.Count; i++)
        {
            var line = block.Instructions[i];
            var isCur = line.Tokens.Any(t =>
                t.Text.Contains(pcAddr, StringComparison.OrdinalIgnoreCase));
            if (isCur)
                block.Instructions[i] = new HighlightedLine(line.Tokens, true);
        }
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
        await _session.SendCommandAsync(cmd);
        collecting = true;
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
                var normalized = $"  {addr}:\t{body}";
                var isCur = currentPc != null && string.Equals(addr, currentPc, StringComparison.OrdinalIgnoreCase);
                insts.Add(new HighlightedLine(DisassemblyHighlighter.Tokenize(normalized), isCur));
            }
        }

        if (insts.Count == 0) return null;
        return new FunctionItem(funcName, firstAddr ?? "", insts);
    }
}
