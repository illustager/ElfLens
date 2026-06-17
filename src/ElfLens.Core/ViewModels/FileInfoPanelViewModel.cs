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
            var m = Regex.Match(line, @"^\s*\[\s*(\d+)\]\s+(\S+)\s+(\S+)\s+(\S+)\s+(\S+)\s+(\S+)\s+(\S+)\s+(.*)$");
            if (m.Success && m.Groups[1].Value != "0") // skip section [0] (NULL, has no name)
            {
                Sections.Add(new SectionItem(
                    m.Groups[2].Value, m.Groups[3].Value, m.Groups[4].Value,
                    m.Groups[5].Value, m.Groups[6].Value, m.Groups[8].Value));
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
}

public record KeyValueItem(string Key, string Value);
public record SectionItem(string Name, string Type, string Address, string Offset, string Size, string Flags);
