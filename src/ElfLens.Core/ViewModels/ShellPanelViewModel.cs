using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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

    [ObservableProperty]
    private string _inputCommand = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _prompt = "$ ";

    public ObservableCollection<ShellOutputLine> OutputLines { get; } = new();

    public ShellPanelViewModel(ISshService sshService)
    {
        _sshService = sshService ?? throw new ArgumentNullException(nameof(sshService));
    }

    /// <summary>
    /// Initializes the shell session. Call after the SSH connection is established.
    /// </summary>
    [RelayCommand]
    private async Task InitializeAsync()
    {
        if (_session != null)
            return;

        IsBusy = true;
        try
        {
            _session = await _sshService.CreateShellSessionAsync();
            if (_session != null)
            {
                OutputLines.Add(new ShellOutputLine("Shell session established.", ShellOutputType.System));
                OutputLines.Add(new ShellOutputLine("Type 'help' for available commands, 'exit' to close.", ShellOutputType.System));
            }
            else
            {
                OutputLines.Add(new ShellOutputLine("Failed to create shell session.", ShellOutputType.Error));
            }
        }
        catch (Exception ex)
        {
            OutputLines.Add(new ShellOutputLine($"Error: {ex.Message}", ShellOutputType.Error));
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanExecuteCommand))]
    private async Task ExecuteCommandAsync()
    {
        var command = InputCommand.Trim();
        if (string.IsNullOrEmpty(command))
            return;

        // Add to history
        _commandHistory.Add(command);
        _historyIndex = _commandHistory.Count;

        // Echo the command
        OutputLines.Add(new ShellOutputLine($"{Prompt}{command}", ShellOutputType.Command));
        InputCommand = string.Empty;

        if (_session == null)
        {
            OutputLines.Add(new ShellOutputLine("Shell not initialized. Waiting for connection...", ShellOutputType.Error));
            return;
        }

        IsBusy = true;
        try
        {
            var output = await _session.ExecuteCommandAsync(command);
            if (!string.IsNullOrEmpty(output))
            {
                foreach (var line in output.Split('\n'))
                {
                    OutputLines.Add(new ShellOutputLine(line.TrimEnd(), ShellOutputType.Output));
                }
            }
            else
            {
                OutputLines.Add(new ShellOutputLine("(no output)", ShellOutputType.System));
            }
        }
        catch (Exception ex)
        {
            OutputLines.Add(new ShellOutputLine($"Error executing command: {ex.Message}", ShellOutputType.Error));
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanExecuteCommand() => !IsBusy && !string.IsNullOrWhiteSpace(InputCommand);

    /// <summary>
    /// Navigate command history: negative = back, positive = forward.
    /// </summary>
    public void NavigateHistory(int direction)
    {
        if (_commandHistory.Count == 0) return;

        _historyIndex = Math.Clamp(_historyIndex + direction, 0, _commandHistory.Count);
        InputCommand = _historyIndex < _commandHistory.Count
            ? _commandHistory[_historyIndex]
            : string.Empty;
    }

    public void Cleanup()
    {
        _session?.Dispose();
        _session = null;
    }
}

public enum ShellOutputType
{
    Command,
    Output,
    Error,
    System
}

public class ShellOutputLine
{
    public string Text { get; }
    public ShellOutputType Type { get; }

    public ShellOutputLine(string text, ShellOutputType type)
    {
        Text = text;
        Type = type;
    }
}
