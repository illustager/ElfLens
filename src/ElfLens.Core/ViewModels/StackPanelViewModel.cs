using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ElfLens.Core.Models;
using ElfLens.Core;

namespace ElfLens.Core.ViewModels;

public partial class StackFrameItem : ObservableObject
{
    public string FrameNum { get; init; } = "";
    public string Function { get; init; } = "";
    public string Address { get; init; } = "";
    public string RawLine { get; init; } = "";

    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _isLoading;

    public ObservableCollection<HighlightedLine> MemoryLines { get; } = new();
}

public partial class StackPanelViewModel : SessionPanelViewModel
{
    public override string Title => "Stack";
    public override PanelZone Zone => PanelZone.Left;

    public ObservableCollection<StackFrameItem> Frames { get; } = new();

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (Session == null) return;
        var text = await Session.CaptureOutputAsync("bt");
        ParseBacktrace(text);
    }

    private void ParseBacktrace(string output)
    {
        Frames.Clear();
        var rx = new Regex(@"^#(\d+)\s+(?:0x([0-9a-fA-F]+)\s+in\s+)?(\S+)",
            RegexOptions.Multiline);

        foreach (Match m in rx.Matches(output))
        {
            var addr = m.Groups[2].Success ? "0x" + m.Groups[2].Value : "";
            var func = m.Groups[3].Value;
            func = Regex.Replace(func, @"[\(\),]+$", "");
            Frames.Add(new StackFrameItem
            {
                FrameNum = m.Groups[1].Value,
                Function = func,
                Address = addr,
                RawLine = m.Value.Trim()
            });
        }
    }

    // Called from code-behind when toggle button is clicked
    public async Task ToggleFrameAsync(StackFrameItem frame)
    {
        if (Session == null) return;

        if (frame.IsExpanded)
        {
            frame.IsExpanded = false;
            frame.MemoryLines.Clear();
            return;
        }

        frame.IsLoading = true;
        frame.IsExpanded = true;
        frame.MemoryLines.Clear();

        try
        {
            var output = await CaptureStackMemory(frame.FrameNum);
            foreach (var line in output.Split('\n'))
            {
                var t = line.Trim();
                if (t.Length == 0) continue;
                if (t.Contains("pwndbg>") || t.Contains("(gdb)")) continue;
                if (t.StartsWith("stack ") || t.StartsWith("x/") || t.StartsWith("frame ") || t.StartsWith("print/")) continue;
                if (t.Contains("info frame") || t.Contains("info registers")) continue;
                frame.MemoryLines.Add(new HighlightedLine(StackHighlighter.Tokenize(t)));
            }
        }
        finally { frame.IsLoading = false; }
    }

    private async Task<string> CaptureStackMemory(string frameNum)
    {
        if (Session == null) return "";

        // Step 1: switch to target frame
        await Session.CaptureOutputAsync($"frame {frameNum}", 400);

        // Step 2: get frame boundaries via info frame
        var frameText = await Session.CaptureOutputAsync("info frame", 400);
        var atM = Regex.Match(frameText, @"frame at (0x[0-9a-fA-F]+)");
        var callerM = Regex.Match(frameText, @"caller of frame at (0x[0-9a-fA-F]+)");

        int count = 16;
        long startAddr = 0, endAddr = 0;

        if (atM.Success)
            endAddr = long.Parse(atM.Groups[1].Value.Replace("0x", ""),
                System.Globalization.NumberStyles.HexNumber);
        if (callerM.Success)
            startAddr = long.Parse(callerM.Groups[1].Value.Replace("0x", ""),
                System.Globalization.NumberStyles.HexNumber);
        else
        {
            var rspText = await Session.CaptureOutputAsync("print/x $rsp", 300);
            var rspM = Regex.Match(rspText, @"0x([0-9a-fA-F]+)");
            if (rspM.Success)
                startAddr = long.Parse(rspM.Groups[1].Value,
                    System.Globalization.NumberStyles.HexNumber);
        }

        if (startAddr != 0 && endAddr != 0)
        {
            var diff = endAddr - startAddr;
            count = Math.Clamp(Math.Abs((int)(diff / 8)) + 2, 4, 64);
        }

        // Step 3: dump stack memory
        var startHex = startAddr != 0 ? $"0x{startAddr:x}" : "$rsp";
        string text;
        if (frameNum == "0")
            text = await Session.CaptureOutputAsync($"stack {count}", 800);
        else
            text = await Session.CaptureOutputAsync($"x/{count}gx {startHex}", 800);

        if (text.Contains("Undefined command") || text.Contains("No symbol"))
            text = await Session.CaptureOutputAsync($"x/{count}gx {startHex}", 800);

        // Step 4: switch back to frame 0
        await Session.CaptureOutputAsync("frame 0", 200);

        return text;
    }
}
