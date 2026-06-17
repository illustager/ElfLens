using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ElfLens.Views;

public partial class GdbDisasmPanelView : UserControl
{
    public GdbDisasmPanelView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is Core.ViewModels.GdbDisasmPanelViewModel vm)
        {
            vm.ScrollToBlock += block =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    var border = this.GetVisualDescendants()
                        .OfType<Border>()
                        .FirstOrDefault(b => b.DataContext == block);
                    border?.BringIntoView();
                }, DispatcherPriority.Background);
            };
        }
    }
}
