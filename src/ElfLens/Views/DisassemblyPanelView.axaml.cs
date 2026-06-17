using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ElfLens.Views;

public partial class DisassemblyPanelView : UserControl
{
    public DisassemblyPanelView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is Core.ViewModels.DisassemblyPanelViewModel vm)
        {
            vm.NavigateToFunction += fn =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    var border = Scroller.GetVisualDescendants()
                        .OfType<Border>()
                        .FirstOrDefault(b => b.DataContext == fn);
                    border?.BringIntoView();
                }, DispatcherPriority.Background);
            };
        }
    }

    private void OnLineClicked(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control ctrl) return;
        // Find the HighlightedLine DataContext
        var line = ctrl.DataContext as Core.ViewModels.HighlightedLine;
        if (line == null) return;
        var token = line.Tokens.FirstOrDefault(t => t.NavigateTo != null);
        if (token?.NavigateTo != null && DataContext is Core.ViewModels.DisassemblyPanelViewModel vm)
        {
            vm.NavigateCommand.Execute(token.NavigateTo);
        }
    }
}
