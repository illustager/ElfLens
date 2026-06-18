using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ElfLens.Core.Models;

namespace ElfLens.Core.ViewModels;

public record RegisterEntry(string Name, string HexValue, string DecValue);

public partial class RegistersPanelViewModel : SessionPanelViewModel
{
    public override string Title => "Registers";
    public override PanelZone Zone => PanelZone.Left;

    public ObservableCollection<RegisterEntry> Registers { get; } = new();

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (Session == null) return;
        var text = await Session.CaptureOutputAsync("info registers",
            stopPredicate: s => s.Contains("eflags"));
        ParseRegisters(text);
    }

    private void ParseRegisters(string output)
    {
        // GDB "info registers" format: "rax            0x401000            4198400"
        // Also handles lines like "rip            0x401000            4198400 <main>"
        var rx = new Regex(@"^(\w+)\s+(0x[0-9a-fA-F]+)\s+(\d+)", RegexOptions.Multiline);
        var matches = rx.Matches(output);

        Registers.Clear();
        foreach (Match m in matches)
        {
            Registers.Add(new RegisterEntry(
                m.Groups[1].Value,
                m.Groups[2].Value,
                m.Groups[3].Value));
        }
    }
}
