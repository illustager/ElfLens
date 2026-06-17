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
        System.Diagnostics.Debug.WriteLine("[BP] Toggle clicked");
        if (sender is Button btn && btn.DataContext is BreakpointEntry entry &&
            DataContext is BreakpointPanelViewModel vm)
        {
            vm.ToggleCommand.Execute(entry);
        }
    }

    private void OnRemoveClick(object? sender, RoutedEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine("[BP] Remove clicked");
        if (sender is Button btn && btn.DataContext is BreakpointEntry entry &&
            DataContext is BreakpointPanelViewModel vm)
        {
            vm.RemoveCommand.Execute(entry);
        }
    }
}
