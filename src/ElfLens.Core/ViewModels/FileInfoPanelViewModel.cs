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
        // Parse section table: lines starting with whitespace + [Nr] or a number
        // Format: "  [ 0]  .interp  PROGBITS  0000000000000318  ..."
        foreach (var line in output.Split('\n'))
        {
            // readelf -SW format: [Nr] Name Type Address Off Size ES Flg Lk Inf Al
            var m = Regex.Match(line, @"^\s*\[\s*\d+\]\s+(\S+)\s+(\S+)\s+(\S+)\s+(\S+)\s+(\S+)\s+(\S+)\s+(.*)$");
            if (m.Success)
            {
                Sections.Add(new SectionItem(
                    m.Groups[1].Value,  // Name
                    m.Groups[2].Value,  // Type
                    m.Groups[3].Value,  // Address
                    m.Groups[4].Value,  // Offset
                    m.Groups[5].Value,  // Size
                    m.Groups[7].Value   // Flags
                ));
            }
        }
    }
}

public record KeyValueItem(string Key, string Value);
public record SectionItem(string Name, string Type, string Address, string Offset, string Size, string Flags);
