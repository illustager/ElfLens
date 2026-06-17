using Avalonia.Controls;
using Avalonia.Interactivity;
using ElfLens.Core.ViewModels;

namespace ElfLens.Views;

public partial class BreakpointPanelView : UserControl
{
    public BreakpointPanelView()
    {
        InitializeComponent();
    }

    private void OnToggleClick(object? sender, RoutedEventArgs e)
    {
        try { System.IO.File.AppendAllText(System.IO.Path.Combine(System.AppContext.BaseDirectory, "bp_click.log"), $"TOGGLE sender={sender?.GetType().Name} btn_dc={((sender as Button)?.DataContext)?.GetType().Name} page_dc={DataContext?.GetType().Name}\n"); } catch { }
        if (sender is Button btn && btn.DataContext is BreakpointEntry entry &&
            DataContext is BreakpointPanelViewModel vm)
        {
            try { System.IO.File.AppendAllText(System.IO.Path.Combine(System.AppContext.BaseDirectory, "bp_click.log"), "EXECUTING TOGGLE\n"); } catch { }
            vm.ToggleCommand.Execute(entry);
        }
    }

    private void OnRemoveClick(object? sender, RoutedEventArgs e)
    {
        try { System.IO.File.AppendAllText(System.IO.Path.Combine(System.AppContext.BaseDirectory, "bp_click.log"), $"REMOVE sender={sender?.GetType().Name} btn_dc={((sender as Button)?.DataContext)?.GetType().Name} page_dc={DataContext?.GetType().Name}\n"); } catch { }
        if (sender is Button btn && btn.DataContext is BreakpointEntry entry &&
            DataContext is BreakpointPanelViewModel vm)
        {
            try { System.IO.File.AppendAllText(System.IO.Path.Combine(System.AppContext.BaseDirectory, "bp_click.log"), "EXECUTING REMOVE\n"); } catch { }
            vm.RemoveCommand.Execute(entry);
        }
    }
}
