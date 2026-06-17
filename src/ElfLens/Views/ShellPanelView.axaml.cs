using Avalonia.Controls;
using Avalonia.Input;

namespace ElfLens.Views;

public partial class ShellPanelView : UserControl
{
    public ShellPanelView()
    {
        InitializeComponent();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key == Key.Enter && DataContext is Core.ViewModels.ShellPanelViewModel vm)
        {
            if (vm.ExecuteCommandCommand.CanExecute(null))
                vm.ExecuteCommandCommand.Execute(null);
            e.Handled = true;
        }
    }
}
