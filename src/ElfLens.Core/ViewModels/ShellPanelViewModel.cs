using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ElfLens.Core.Models;
using ElfLens.Core.Services;

namespace ElfLens.Core.ViewModels;

public partial class ShellPanelViewModel : ViewModelBase
{
    private readonly ISshService _sshService;
    private ShellSession? _session;
    private readonly List<string> _commandHistory = new();
    private int _historyIndex = -1;
    private readonly StringBuilder _outputBuffer = new();

    [ObservableProperty] private string _inputCommand = string.Empty;
    [ObservableProperty] private string _prompt = "$ ";
    [ObservableProperty] private string _outputText = string.Empty;

    public ShellPanelViewModel(ISshService sshService)
    {
        _sshService = sshService;
    }

    [RelayCommand]
    private async Task InitializeAsync()
    {
        if (_session != null) return;
        var uiCtx = SynchronizationContext.Current;
        try
        {
            _session = await _sshService.CreateShellSessionAsync();
            if (_session != null)
            {
                AppendOutput(_session.Prompt + " ");
                _session.OnOutput += chunk =>
                {
                    uiCtx?.Post(_ => AppendOutput(chunk), null);
                };
            }
            else AppendOutput("!!! Failed to create shell session\n");
        }
        catch (Exception ex) { AppendOutput($"!!! Error: {ex.Message}\n"); }
    }

    [RelayCommand]
    private async Task SendCommand()
    {
        var command = InputCommand;

        _commandHistory.Add(command);
        _historyIndex = _commandHistory.Count;
        InputCommand = string.Empty;

        if (_session == null) { AppendOutput("!!! Shell not initialized\n"); return; }

        try
        {
            await _session.SendCommandAsync(command);
        }
        catch (Exception ex) { AppendOutput($"!!! Error: {ex.Message}\n"); }
    }

    private void AppendOutput(string text)
    {
        _outputBuffer.Append(text);
        OutputText = _outputBuffer.ToString();
    }

    public void NavigateHistory(int direction)
    {
        if (_commandHistory.Count == 0) return;
        _historyIndex = Math.Clamp(_historyIndex + direction, 0, _commandHistory.Count);
        InputCommand = _historyIndex < _commandHistory.Count
            ? _commandHistory[_historyIndex] : string.Empty;
    }

    public void Cleanup() { _session?.Dispose(); _session = null; }
}
