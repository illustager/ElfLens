using Avalonia.Controls;
using Avalonia.Input;

namespace ElfLens.Views;

public partial class ShellPanelView : UserControl
{
    public ShellPanelView()
    {
        InitializeComponent();
        OutputBox.TextChanged += (_, _) =>
        {
            OutputBox.CaretIndex = int.MaxValue;
        };
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Enter && DataContext is Core.ViewModels.ShellPanelViewModel vm)
        {
            if (vm.SendCommandCommand.CanExecute(null))
                vm.SendCommandCommand.Execute(null);
            e.Handled = true;
        }
    }
}
