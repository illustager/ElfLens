using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
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

    private void OnSetBreakpointClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not Core.ViewModels.GdbDisasmPanelViewModel vm) return;
        // DataContext inherits from the Border into the popup ContextMenu
        if ((sender as Control)?.DataContext is not Core.ViewModels.HighlightedLine line) return;

        // Find the parent FunctionItem by searching the VM's collection
        var fnItem = vm.FunctionBlocks.FirstOrDefault(f => f.Instructions.Contains(line));
        if (fnItem == null) return;

        // Calculate byte offset from function base
        var firstText = line.Tokens.FirstOrDefault()?.Text ?? "";
        var addrM = Regex.Match(firstText, @"([0-9a-fA-F]+)");
        if (!addrM.Success) return;
        if (!long.TryParse(addrM.Groups[1].Value, NumberStyles.HexNumber, null, out var instAddr)) return;
        if (!long.TryParse(fnItem.Address, NumberStyles.HexNumber, null, out var baseAddr)) return;
        var offset = (int)(instAddr - baseAddr);

        vm.RequestBreakpoint(fnItem.Name, offset);
    }
}
