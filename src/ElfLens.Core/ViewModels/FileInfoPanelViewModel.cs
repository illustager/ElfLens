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

public partial class FileInfoPanelViewModel : PanelViewModel
{
    private readonly ISshService _sshService;

    public override string Title => "File Info";
    public override PanelZone Zone => PanelZone.Right;

    [ObservableProperty] private string _targetPath = "";
    [ObservableProperty] private string _fileType = "";
    [ObservableProperty] private bool _isBusy;

    public ObservableCollection<SecurityItem> SecurityInfo { get; } = new();
    public ObservableCollection<KeyValueItem> ElfHeaders { get; } = new();
    public ObservableCollection<SectionItem> Sections { get; } = new();

    // Computed column widths for section table
    [ObservableProperty] private double _colNameWidth = 80;
    [ObservableProperty] private double _colTypeWidth = 60;
    [ObservableProperty] private double _colAddrWidth = 100;
    [ObservableProperty] private double _colSizeWidth = 60;
    [ObservableProperty] private double _colFlagsWidth = 60;

    public FileInfoPanelViewModel(ISshService sshService)
    {
        _sshService = sshService;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (string.IsNullOrWhiteSpace(TargetPath)) return;
        IsBusy = true;
        try
        {
            var fileOut = await _sshService.ExecuteCommandAsync($"file \"{TargetPath}\"");
            FileType = fileOut.Replace(TargetPath + ":", "").Trim();

            var headerOut = await _sshService.ExecuteCommandAsync($"readelf -h \"{TargetPath}\"");
            ParseElfHeader(headerOut);

            var checksecOut = await _sshService.ExecuteCommandAsync($"checksec \"{TargetPath}\"");
            if (!string.IsNullOrWhiteSpace(checksecOut) && checksecOut.Contains("RELRO"))
                ParseChecksec(checksecOut);
            else
                await ParseSecurityFallback();

            var sectionsOut = await _sshService.ExecuteCommandAsync($"readelf -SW \"{TargetPath}\"");
            ParseSections(sectionsOut);
        }
        catch (Exception ex) { FileType = $"Error: {ex.Message}"; }
        finally { IsBusy = false; }
    }

    private void ParseElfHeader(string output)
    {
        ElfHeaders.Clear();
        // Parse lines like "  Class:                             ELF64" or "  Entry point address:               0x1040"
        foreach (var line in output.Split('\n'))
        {
            var m = Regex.Match(line, @"^\s+([^:]+):\s+(.+)$");
            if (m.Success)
                ElfHeaders.Add(new KeyValueItem(m.Groups[1].Value.Trim(), m.Groups[2].Value.Trim()));
        }
    }

    private void ParseSections(string output)
    {
        Sections.Clear();
        foreach (var line in output.Split('\n'))
        {
            // -W: [Nr] Name Type Address Off Size ES Flg Lk Inf Al
            // Flg may be empty; use (\S*) and check for numeric
            var m = Regex.Match(line,
                @"^\s*\[\s*(\d+)\]\s+(\S+)\s+(\S+)\s+(\S+)\s+(\S+)\s+(\S+)\s+(\S+)\s+(\S*)\s+(\S+)\s+(\S+)\s+(\S+)$");
            if (m.Success && m.Groups[1].Value != "0")
            {
                var flags = m.Groups[8].Value;
                if (flags.Length == 0 || char.IsDigit(flags[0]))
                    flags = "-"; // no flags or numeric (misparse)
                Sections.Add(new SectionItem(
                    m.Groups[2].Value, m.Groups[3].Value, m.Groups[4].Value,
                    m.Groups[5].Value, m.Groups[6].Value, flags));
            }
        }
        ComputeSectionColumnWidths();
    }

    private void ComputeSectionColumnWidths()
    {
        if (Sections.Count == 0) return;
        const double charWidth = 6.8; // Cascadia Code 11px approximate
        const double padding = 12;
        ColNameWidth = Math.Max(Sections.Max(s => s.Name.Length) * charWidth + padding, 60);
        ColTypeWidth = Math.Max(Sections.Max(s => s.Type.Length) * charWidth + padding, 60);
        ColAddrWidth = Math.Max(Sections.Max(s => s.Address.Length) * charWidth + padding, 80);
        ColSizeWidth = Math.Max(Sections.Max(s => s.Size.Length) * charWidth + padding, 50);
        ColFlagsWidth = Math.Max(Sections.Max(s => s.Flags.Length) * charWidth + padding, 50);
    }

    private async Task ParseSecurityFallback()
    {
        SecurityInfo.Clear();
        var progOut = await _sshService.ExecuteCommandAsync($"readelf -lW \"{TargetPath}\"");
        var symsOut = await _sshService.ExecuteCommandAsync($"readelf -sW \"{TargetPath}\"");

        bool hasNx = progOut.Contains("GNU_STACK") && !Regex.IsMatch(progOut, @"GNU_STACK.*\bE\b");
        bool hasRelro = progOut.Contains("GNU_RELRO");
        bool hasCanary = symsOut.Contains("__stack_chk_fail");
        SecurityInfo.Add(new SecurityItem("NX", hasNx, hasNx ? "NX enabled" : "NX disabled"));
        SecurityInfo.Add(new SecurityItem("RELRO", hasRelro, hasRelro ? "RELRO present" : "No RELRO"));
        SecurityInfo.Add(new SecurityItem("Stack Canary", hasCanary, hasCanary ? "Canary found" : "No canary"));
        // PIE: check ELF type from already-parsed header
        var typeItem = ElfHeaders.FirstOrDefault(h => h.Key == "Type");
        bool isPie = typeItem?.Value?.Contains("DYN") == true;
        SecurityInfo.Add(new SecurityItem("PIE", isPie, isPie ? "PIE enabled" : "No PIE"));
    }

    private void ParseChecksec(string output)
    {
        SecurityInfo.Clear();
        foreach (var line in output.Split('\n'))
        {
            var m = Regex.Match(line, @"^\s*(RELRO|Stack|NX|PIE)\s*:\s*(.+)$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                var name = m.Groups[1].Value.Trim();
                var value = m.Groups[2].Value.Trim();
                var enabled = !value.StartsWith("No ") && !value.Contains("disabled");
                SecurityInfo.Add(new SecurityItem(name, enabled, value));
            }
        }
    }
}

public record KeyValueItem(string Key, string Value);
public record SectionItem(string Name, string Type, string Address, string Offset, string Size, string Flags);
public record SecurityItem(string Name, bool Enabled, string Detail);
