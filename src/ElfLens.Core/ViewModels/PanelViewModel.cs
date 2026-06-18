namespace ElfLens.Core.ViewModels;

/// <summary>
/// Base class for dockable panel view models.
/// </summary>
public abstract class PanelViewModel : ViewModelBase
{
    public abstract string Title { get; }
    public abstract PanelZone Zone { get; }
}

public enum PanelZone
{
    Left,
    Center,
    Right,
    Bottom
}
