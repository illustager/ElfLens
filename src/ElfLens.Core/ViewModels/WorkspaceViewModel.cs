using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace ElfLens.Core.ViewModels;

/// <summary>
/// ViewModel for the main workspace area with docking panels.
/// </summary>
public partial class WorkspaceViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _title = "Workspace";

    /// <summary>
    /// Dockable panels currently in the workspace.
    /// Populated as panels are added in later steps.
    /// </summary>
    public ObservableCollection<ViewModelBase> Panels { get; } = new();
}
