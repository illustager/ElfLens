using System;
using Avalonia.Controls;
using Avalonia.Threading;

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
                    // Find the Border for this function and scroll to it
                    for (int i = 0; i < vm.Functions.Count; i++)
                    {
                        if (vm.Functions[i] == fn)
                        {
                            var container = Scroller; // not exact — ItemsControl
                            // Use BringIntoView on a known element
                            break;
                        }
                    }
                });
            };
        }
    }
}
