using System.IO;
using System.Reflection;
using System.Xml;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using Avalonia.Markup.Xaml;
using ElfLens.Core.ViewModels;
using ElfLens.Views;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;

namespace ElfLens;

public partial class App : Application
{
    public override void Initialize()
    {
        // Register x86 assembly syntax highlighting
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("ElfLens.Highlighting.Asm-x86.xshd");
        if (stream != null)
        {
            using var reader = new XmlTextReader(stream);
            var definition = HighlightingLoader.Load(reader, HighlightingManager.Instance);
            HighlightingManager.Instance.RegisterHighlighting("x86 Assembly", new[] { ".asm", ".s" }, definition);
        }

        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel(),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
