using System;
using Avalonia.Controls;

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
            vm.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(vm.OutputText))
                    Editor.Text = vm.OutputText;
            };
            Editor.Text = vm.OutputText;
        }
    }
}
