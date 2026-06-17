using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ElfLens.Views;

public partial class ShellPanelView : UserControl
{
    public ShellPanelView()
    {
        InitializeComponent();
        OutputBox.TextChanged += (_, _) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                var scrollViewer = OutputBox.FindDescendantOfType<ScrollViewer>();
                scrollViewer?.ScrollToEnd();
            }, DispatcherPriority.Background);
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
