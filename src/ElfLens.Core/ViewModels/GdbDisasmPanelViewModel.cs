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

    private void UpdateHighlight(string pc)
    {
        FunctionItem? found = null;
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
                if (isCur) found = fb;
                newInsts.Add(new HighlightedLine(line.Tokens, isCur));
            }
            if (changed)
                FunctionBlocks[bi] = new FunctionItem(fb.Name, fb.Address, newInsts);
        }
        if (found != null)
        {
            found.IsExpanded = true;
            ScrollToBlock?.Invoke(found);
        }
    }

    public ObservableCollection<FunctionItem> FunctionBlocks { get; } = new();
    public event Action<ShellSession?, string>? SessionChanged;
    public event Action<FunctionItem>? ScrollToBlock;

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
            UpdateHighlight(pcAddr);

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

    private static List<Token> TokenizeGdbLine(string addr, string body)
    {
        // Format: "f3 0f 1e fa\tendbr64 " — bytes then tab then mnemonic
        var tokens = new List<Token>();
        // Address token: padded to fixed width for alignment
        tokens.Add(new Token($"  {addr}:\t", "#546E7A"));

        var m = Regex.Match(body, @"^((?:[0-9a-f]{2}\s)*[0-9a-f]{2})(\s+)([a-z]\w*)((?:\s+.*?)?)(\s*#.*)?$", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            // Bytes: pad to 24 chars for alignment (covers up to 8 hex pairs + spaces)
            var bytes = m.Groups[1].Value.PadRight(24);
            tokens.Add(new Token(bytes, "#546E7A"));
            // Mnemonic
            var mnem = m.Groups[3].Value.ToLower();
            tokens.Add(new Token(m.Groups[3].Value, MnemColor(mnem)));
            // Operands
            if (m.Groups[4].Value.Length > 0)
                TokenizeOperands(m.Groups[4].Value, tokens);
            // Comment
            if (m.Groups[5].Success)
                tokens.Add(new Token(m.Groups[5].Value, "#6A9955"));
        }
        else
        {
            tokens.Add(new Token(body, "#B0BEC5"));
        }
        return tokens;
    }

    private static string MnemColor(string m)
    {
        if (m is "call") return "#81C784";
        if (m is "ret" or "retn" or "retf" or "iret" or "iretq") return "#EF5350";
        if (m.StartsWith('j') || m is "loop" or "loope" or "loopne") return "#FFB74D";
        return "#B0BEC5";
    }

    // Register names (GDB format without % prefix)
    private static readonly HashSet<string> RegNames = new()
    {
        "rax","rbx","rcx","rdx","rsi","rdi","rbp","rsp","rip",
        "eax","ebx","ecx","edx","esi","edi","ebp","esp","eip",
        "ax","bx","cx","dx","si","di","bp","sp",
        "al","ah","bl","bh","cl","ch","dl","dh",
        "r8","r9","r10","r11","r12","r13","r14","r15",
        "r8d","r9d","r10d","r11d","r12d","r13d","r14d","r15d",
        "xmm0","xmm1","xmm2","xmm3","xmm4","xmm5","xmm6","xmm7",
        "ymm0","ymm1","ymm2","ymm3",
        "cs","ds","es","fs","gs","ss",
        "st0","st1","st2","st3","st4","st5","st6","st7",
        "cr0","cr2","cr3","cr4","dr0","dr1","dr2","dr3","dr6","dr7",
        "mm0","mm1","mm2","mm3","mm4","mm5","mm6","mm7",
    };

    private static void TokenizeOperands(string text, List<Token> tokens)
    {
        int p = 0;
        while (p < text.Length)
        {
            // Angle bracket ref: <name+offset> or <name@plt>
            var refM = Regex.Match(text[p..], @"^<[^>]+>");
            if (refM.Success)
            {
                if (p < refM.Index + p) tokens.Add(new Token(text[p..(p + refM.Index)], "#B0BEC5"));
                tokens.Add(new Token(refM.Value, "#4FC3F7"));
                p += refM.Index + refM.Length;
                continue;
            }
            // Hex: 0x...
            var hexM = Regex.Match(text[p..], @"^0x[0-9a-f]+");
            if (hexM.Success)
            {
                if (p < hexM.Index + p) tokens.Add(new Token(text[p..(p + hexM.Index)], "#B0BEC5"));
                tokens.Add(new Token(hexM.Value, "#FFE082"));
                p += hexM.Index + hexM.Length;
                continue;
            }
            // Register (GDB format, no %): match known register names
            var wordM = Regex.Match(text[p..], @"^[a-z][a-z0-9]*");
            if (wordM.Success && RegNames.Contains(wordM.Value.ToLower()))
            {
                if (p < wordM.Index + p) tokens.Add(new Token(text[p..(p + wordM.Index)], "#B0BEC5"));
                tokens.Add(new Token(wordM.Value, "#CE93D8"));
                p += wordM.Index + wordM.Length;
                continue;
            }
            // Advance to next special char
            int next = text.Length;
            foreach (var rx in new[] { @"<[^>]+>", @"0x[0-9a-f]+", @"[a-z][a-z0-9]*" })
            {
                var m2 = Regex.Match(text[p..], rx);
                if (m2.Success && m2.Index + p < next) next = m2.Index + p;
            }
            if (next > p) { tokens.Add(new Token(text[p..next], "#B0BEC5")); p = next; }
            else p++;
        }
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
                // Tokenize GDB output directly: addr + bytes + mnemonic + operands
                insts.Add(new HighlightedLine(TokenizeGdbLine(addr, body)));
            }
        }

        if (insts.Count == 0) return null;
        return new FunctionItem(funcName, firstAddr ?? "", insts);
    }
}
