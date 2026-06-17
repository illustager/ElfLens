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

public record HighlightedLine(List<Token> Tokens, bool IsCurrent = false);

public partial class FunctionItem : ObservableObject
{
    public string Name { get; }
    public string Address { get; }
    public IList<HighlightedLine> Instructions { get; set; }
    [ObservableProperty] private bool _isExpanded;

    public FunctionItem(string name, string addr, IList<HighlightedLine> insts)
    { Name = name; Address = addr; Instructions = insts; }

    [RelayCommand]
    private void Toggle() => IsExpanded = !IsExpanded;
}

public partial class DisassemblyPanelViewModel : PanelViewModel
{
    private readonly ISshService _sshService;
    private ShellSession? _gdbSession;
    private CancellationTokenSource? _gdbCts;

    public override string Title => "Disassembly";
    public override PanelZone Zone => PanelZone.Center;

    [ObservableProperty] private string _targetPath = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _functionCount = "";
    [ObservableProperty] private bool _isDebugging;

    public ObservableCollection<FunctionItem> Functions { get; } = new();
    public event Action<FunctionItem>? NavigateToFunction;

    public DisassemblyPanelViewModel(ISshService sshService) => _sshService = sshService;

    // ---- Static mode ----

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (string.IsNullOrWhiteSpace(TargetPath)) return;
        IsBusy = true;
        Functions.Clear();
        try
        {
            var output = await _sshService.ExecuteCommandAsync($"objdump -d \"{TargetPath}\"");
            ParseStatic(output);
        }
        catch (Exception ex)
        {
            Functions.Add(new FunctionItem("Error", "", new List<HighlightedLine>
                { new(new List<Token> { new(ex.Message, "#EF5350") }) }));
        }
        finally { IsBusy = false; }
    }

    // ---- Dynamic (GDB) mode ----

    [RelayCommand]
    private async Task StartDebuggingAsync()
    {
        if (string.IsNullOrWhiteSpace(TargetPath)) return;
        IsBusy = true;
        try
        {
            _gdbSession = await _sshService.CreateShellSessionAsync();
            if (_gdbSession == null) return;
            _gdbCts = new CancellationTokenSource();

            await _gdbSession.SendCommandAsync($"gdb -q \"{TargetPath}\"");
            await Task.Delay(800); // wait for GDB banner

            IsDebugging = true;
            await RefreshDynamicAsync();
        }
        catch (Exception ex)
        {
            Functions.Clear();
            Functions.Add(new FunctionItem("Error", "", new List<HighlightedLine>
                { new(new List<Token> { new(ex.Message, "#EF5350") }) }));
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task StepIntoAsync() => await GdbStep("stepi");
    [RelayCommand]
    private async Task StepOverAsync() => await GdbStep("nexti");
    [RelayCommand]
    private async Task ContinueAsync() => await GdbStep("continue");
    [RelayCommand]
    private async Task StopAsync() => await GdbSend("kill");
    [RelayCommand]
    private async Task RestartAsync() => await GdbSend("run");

    private async Task GdbStep(string cmd)
    {
        if (_gdbSession == null) return;
        await _gdbSession.SendCommandAsync(cmd);
        await Task.Delay(200);
        await RefreshDynamicAsync();
    }

    private async Task GdbSend(string cmd)
    {
        if (_gdbSession == null) return;
        await _gdbSession.SendCommandAsync(cmd);
        await Task.Delay(200);
        await RefreshDynamicAsync();
    }

    private async Task RefreshDynamicAsync()
    {
        if (_gdbSession == null) return;
        IsBusy = true;
        Functions.Clear();
        try
        {
            // Get $pc
            var pcOut = new List<string>();
            var pcComplete = new TaskCompletionSource<bool>();

            void OnPcOutput(string chunk)
            {
                pcOut.Add(chunk);
                if (pcOut.Any(s => s.Contains("0x"))) pcComplete.TrySetResult(true);
            }

            _gdbSession.OnOutput += OnPcOutput;
            await _gdbSession.SendCommandAsync("info registers pc");
            await Task.WhenAny(pcComplete.Task, Task.Delay(2000));
            _gdbSession.OnOutput -= OnPcOutput;

            var pcLine = string.Join("", pcOut);
            var pcMatch = Regex.Match(pcLine, @"0x([0-9a-f]+)");
            var pc = pcMatch.Success ? pcMatch.Groups[1].Value : "";

            // Disassemble around $pc
            var asmOut = string.IsNullOrEmpty(pc)
                ? await _sshService.ExecuteCommandAsync($"gdb -q -batch -ex \"disassemble\" \"{TargetPath}\"")
                : await _sshService.ExecuteCommandAsync($"gdb -q -batch -ex \"disassemble 0x{pc}\" \"{TargetPath}\"");

            ParseStatic(asmOut, pc);
        }
        catch { }
        finally { IsBusy = false; }
    }

    // ---- Parsing ----

    private void ParseStatic(string output, string? currentPc = null)
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
                var lineAddr = Regex.Match(line, @"^\s*([0-9a-f]+):");
                var isCurrent = currentPc != null && lineAddr.Success &&
                                string.Equals(lineAddr.Groups[1].Value, currentPc, StringComparison.OrdinalIgnoreCase);
                insts.Add(new HighlightedLine(DisassemblyHighlighter.Tokenize(line), isCurrent));
            }
        }
        if (cur != null) { cur.Instructions = insts; Functions.Add(cur); }
        FunctionCount = $"{Functions.Count} functions";
    }

    // ---- Navigation ----

    [RelayCommand]
    private void Navigate(string? target)
    {
        if (string.IsNullOrEmpty(target)) return;
        var fn = Functions.FirstOrDefault(f => f.Name == target || f.Address == target);
        if (fn != null)
        {
            fn.IsExpanded = true;
            NavigateToFunction?.Invoke(fn);
        }
    }
}
