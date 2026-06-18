using Avalonia.Controls;
using Avalonia.Interactivity;
using ElfLens.Core.ViewModels;

namespace ElfLens.Views;

public partial class StackPanelView : UserControl
{
    public StackPanelView()
    {
        InitializeComponent();
    }

    private void OnFrameToggleClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.DataContext is not StackFrameItem frame) return;
        if (DataContext is not StackPanelViewModel vm) return;
        _ = vm.ToggleFrameAsync(frame);
    }
}
