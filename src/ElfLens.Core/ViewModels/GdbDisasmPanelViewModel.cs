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

    public ObservableCollection<HighlightedLine> Lines { get; } = new();
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
        Lines.Clear();
        _staticDisasm.HighlightFunction(null, null);
    }

    private async Task Step(string cmd)
    {
        if (_session == null) return;
        await _session.SendCommandAsync(cmd);
        await Task.Delay(200);
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (_session == null) return;
        IsBusy = true;
        Lines.Clear();
        try
        {
            var pcOut = await Capture("info registers pc");
            var pcMatch = Regex.Match(pcOut, @"0x([0-9a-f]+)");
            var pcAddr = pcMatch.Success ? pcMatch.Groups[1].Value : "";

            var funcOut = await Capture("info symbol $pc");
            var funcMatch = Regex.Match(funcOut, @"\b([a-zA-Z_]\w+)\b");
            var funcName = funcMatch.Success ? funcMatch.Groups[1].Value : "";
            CurrentFunction = funcName;

            var asm = string.IsNullOrEmpty(pcAddr)
                ? await Capture("disassemble /r")
                : await Capture($"disassemble /r 0x{pcAddr}");
            ParseGdb(asm, pcAddr);

            // Sync with static disassembly
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
        void H(string c) { sb.Add(c); if (sb.Sum(s => s.Length) > 50000) done.TrySetResult(true); }
        _session.OnOutput += H;
        await _session.SendCommandAsync(cmd);
        await Task.WhenAny(done.Task, Task.Delay(1500));
        _session.OnOutput -= H;
        return string.Join("", sb);
    }

    private void ParseGdb(string output, string? currentPc)
    {
        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed == "End of assembler dump.") continue;

            var gdbInst = Regex.Match(line, @"^\s+(0x[0-9a-f]+)\s+<\+(\d+)>:\s+(.*)$");
            if (gdbInst.Success)
            {
                var addr = gdbInst.Groups[1].Value[2..];
                var body = gdbInst.Groups[3].Value;
                var normalized = $"  {addr}:\t{body}";
                var isCur = currentPc != null && string.Equals(addr, currentPc, StringComparison.OrdinalIgnoreCase);
                Lines.Add(new HighlightedLine(DisassemblyHighlighter.Tokenize(normalized), isCur));
                continue;
            }

            var gdbFm = Regex.Match(line, @"^Dump of assembler code for function\s+(.+):$");
            if (gdbFm.Success)
            {
                Lines.Add(new HighlightedLine(
                    new List<Token> { new($"▼ {gdbFm.Groups[1].Value}", "#4FC3F7") }));
                continue;
            }

            if (trimmed.StartsWith("=>"))
            {
                var rest = trimmed[2..].Trim();
                var addrM = Regex.Match(rest, @"^(0x[0-9a-f]+)");
                var addr = addrM.Success ? addrM.Groups[1].Value[2..] : "";
                var isCur = currentPc != null && string.Equals(addr, currentPc, StringComparison.OrdinalIgnoreCase);
                Lines.Add(new HighlightedLine(DisassemblyHighlighter.Tokenize($"  {addr}:\t{rest}"), isCur));
                continue;
            }

            Lines.Add(new HighlightedLine(DisassemblyHighlighter.Tokenize(line)));
        }
    }
}
