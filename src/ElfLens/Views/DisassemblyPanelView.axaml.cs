using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
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
                // Expand first, then scroll after layout updates
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
}
