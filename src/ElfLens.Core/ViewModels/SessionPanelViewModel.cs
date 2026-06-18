using CommunityToolkit.Mvvm.ComponentModel;
using ElfLens.Core.Models;

namespace ElfLens.Core.ViewModels;

/// <summary>
/// Base class for panels that receive a GDB shell session.
/// </summary>
public abstract partial class SessionPanelViewModel : PanelViewModel
{
    [ObservableProperty] private bool _hasSession;

    protected ShellSession? Session;

    public virtual void SetSession(ShellSession? session)
    {
        Session = session;
        HasSession = session != null;
    }
}
