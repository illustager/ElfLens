# ElfLens Technical Report

> Version 0.1.0 — A Cross-Platform GDB Frontend for Remote Linux Binary Analysis

---

## 1. Project Overview

ElfLens is a desktop application providing a graphical frontend for the GNU Debugger (GDB) over SSH. It enables security researchers and reverse engineers to inspect ELF binaries and debug them remotely through a modern, dark-themed UI built on Avalonia. The application connects to a Linux host via SSH, presents disassembly with syntax highlighting, manages breakpoints visually, and provides real-time register and stack inspection — all without leaving the GUI.

**Target users**: Binary analysts, CTF participants, vulnerability researchers, and embedded systems developers who need a lightweight, cross-platform GDB interface.

---

## 2. Technology Stack

| Layer               | Technology                          |
|---------------------|-------------------------------------|
| UI Framework        | Avalonia 11 (Fluent Dark Theme)     |
| Runtime             | .NET 10                             |
| MVVM Framework      | CommunityToolkit.Mvvm 8.x           |
| SSH Library         | SSH.NET (Renci.SshNet) 2024.x       |
| Language            | C# 12                               |
| Build System        | .NET SDK / MSBuild                  |
| License             | GNU General Public License v3.0     |

**Runtime dependencies**: .NET 10 Runtime on the client machine. Remote host requires SSH server, GDB, GNU binutils (objdump, readelf), and optionally pwndbg for enhanced stack visualization.

---

## 3. Project Structure

### 3.1 Namespace Hierarchy

```
ElfLens (Presentation Layer)
├── Views                — Avalonia UserControl/Window, .axaml markup + code-behind
├── Converters           — IValueConverter implementations for data binding
├── App.axaml            — Application entry, Fluent Dark theme, ViewLocator registration
├── ViewLocator.cs       — IDataTemplate: ViewModel → View resolution by naming convention
└── Program.cs           — .NET application entry point

ElfLens.Core (Logic Layer)
├── Models               — Domain objects (SshConnectionInfo, ShellSession)
├── Services             — Service interfaces and implementations (ISshService, SshService)
├── ViewModels           — MVVM ViewModels, organized by panel and role
├── DisassemblyHighlighter.cs — Tokenizer for objdump/GDB disassembly output
├── StackHighlighter.cs       — Tokenizer for GDB stack/memory dump output
└── RegisterNames.cs          — Shared x86/x86_64 register name constants
```

### 3.2 Key Files

| File | Lines | Purpose |
|------|-------|---------|
| `MainViewModel.cs` | ~120 | Application shell, panel wiring, event routing |
| `GdbDisasmPanelViewModel.cs` | ~320 | GDB interactive debugging, disassembly, step control |
| `ShellSession.cs` | ~100 | SSH shell stream wrapper, async command/response |
| `DisassemblyPanelViewModel.cs` | ~150 | Static objdump disassembly with breakpoint marking |
| `BreakpointPanelViewModel.cs` | ~170 | Breakpoint CRUD, GDB command execution |
| `StackPanelViewModel.cs` | ~150 | Call stack frames with collapsible memory dump |
| `DisassemblyHighlighter.cs` | ~120 | Tokenizer for x86 disassembly lines |
| `StackHighlighter.cs` | ~180 | Tokenizer for pwndbg stack / GDB x/gx output |
| `SshService.cs` | ~130 | SSH.NET wrapper: connect, execute, shell creation |

---

## 4. Architecture

### 4.1 Layered Architecture

```
┌─────────────────────────────────────────┐
│  Presentation Layer (ElfLens)           │
│  Views (.axaml) ← Converters            │
│       ↕ Data Binding                    │
├─────────────────────────────────────────┤
│  ViewModel Layer (ElfLens.Core)         │
│  MainViewModel → Panel VMs              │
│       ↕ Method Calls                    │
├─────────────────────────────────────────┤
│  Service Layer (ElfLens.Core.Services)  │
│  ISshService → SshService               │
│       ↕ Stream I/O                      │
├─────────────────────────────────────────┤
│  Model Layer (ElfLens.Core.Models)      │
│  ShellSession, SshConnectionInfo        │
└─────────────────────────────────────────┘
```

Data flows downward (ViewModel → Service → Model) for commands, and upward (Model → Service → ViewModel → View) for responses via events and observable properties.

### 4.2 MVVM Pattern

ElfLens follows the Model-View-ViewModel pattern using CommunityToolkit.Mvvm:

- **Models**: `SshConnectionInfo`, `ShellSession` — encapsulate SSH state and I/O
- **ViewModels**: Subclasses of `ViewModelBase` (which extends `ObservableObject`) — expose bindable properties and commands via `[ObservableProperty]` and `[RelayCommand]` source generators
- **Views**: Avalonia `UserControl` subclasses — bind to ViewModels via `{Binding}` markup extensions

The `ViewLocator` resolves ViewModels to Views by naming convention: `XxxViewModel` → `XxxView` (e.g., `RegistersPanelViewModel` → `RegistersPanelView`).

---

## 5. Class Hierarchy

### 5.1 ViewModel Inheritance Tree

```
CommunityToolkit.Mvvm.ComponentModel.ObservableObject
└── ViewModelBase                          (ElfLens.Core.ViewModels)
    ├── ConnectPageViewModel               SSH connection form logic
    │
    └── PanelViewModel (abstract)          Docking panel base
        │   Properties: Title, Zone
        │
        ├── SessionPanelViewModel (abstract)  GDB session-aware panel
        │   Properties: Session, HasSession
        │   Method: SetSession(ShellSession?)
        │   ├── BreakpointPanelViewModel   Breakpoint management
        │   ├── RegistersPanelViewModel    CPU register display
        │   └── StackPanelViewModel        Call stack with memory dump
        │
        ├── ShellPanelViewModel            Interactive terminal
        ├── FileInfoPanelViewModel         ELF file inspection
        ├── DisassemblyPanelViewModel      Static objdump disassembly
        └── GdbDisasmPanelViewModel        GDB interactive debugging

MainViewModel (composes all panel VMs, wires events)
```

### 5.2 Panel Hierarchy

```
PanelViewModel (abstract)
  ├── Title: string
  └── Zone: PanelZone { Left, Center, Right, Bottom }

MainViewModel
  ├── LeftPanels: ObservableCollection<PanelViewModel>
  ├── CenterPanels: ObservableCollection<PanelViewModel>
  ├── RightPanels: ObservableCollection<PanelViewModel>
  └── BottomPanels: ObservableCollection<PanelViewModel>
```

### 5.3 Data Records

```
Token(text, color, navigateTo?)
HighlightedLine(tokens, isCurrent?, isBreakpoint?, isBreakpointDisabled?)
FunctionItem(name, address, instructions) : ObservableObject
  └── IsExpanded, IsCurrent, ToggleCommand

RegisterEntry(name, hexValue, decValue)           (record)
StackFrameItem(frameNum, function, address, rawLine) : ObservableObject
  └── IsExpanded, IsLoading, MemoryLines
BreakpointEntry(location) : ObservableObject
  └── Enabled, GdbNum, ResolvedAddr, ResolvedFunc
```

---

## 6. Core Interfaces & Services

### 6.1 `ISshService` Interface

```csharp
public interface ISshService
{
    bool IsConnected { get; }
    SshConnectionInfo? ConnectionInfo { get; }

    Task<bool> ConnectAsync(SshConnectionInfo info);
    Task DisconnectAsync();
    Task<string> ExecuteCommandAsync(string command);     // Non-interactive
    Task<ShellSession?> CreateShellSessionAsync();         // Interactive shell
}
```

`SshService` implements `ISshService` and `IDisposable`. It wraps an SSH.NET `SshClient` and provides:
- Connection lifecycle management
- Single-command execution via SSH.NET `CreateCommand()` with 30s timeout
- Shell session creation via `CreateShellStream()` with terminal emulation (xterm-256color, 200×40)
- Automatic cleanup on disconnect

### 6.2 `ShellSession` Class

The core abstraction for GDB interaction:

```csharp
public class ShellSession : IDisposable
{
    public event Action<string>? OnOutput;              // Raw output chunks

    public async Task SendCommandAsync(string command);  // Write to shell
    public async Task<string> CaptureOutputAsync(        // Send + await response
        string command, int timeoutMs = 800,
        Func<string, bool>? stopPredicate = null);

    public void Dispose();                               // Cancel read loop, dispose stream
}
```

**`CaptureOutputAsync`** is the unified command/response pattern used by all panels. It:
1. Subscribes to `OnOutput`
2. Sends the command via `SendCommandAsync`
3. Waits until either: the stop predicate matches, the GDB prompt (`(gdb)`/`pwndbg>`) appears, output exceeds 8000 chars, or timeout expires
4. Unsubscribes and returns the accumulated output

This pattern eliminates ~100 lines of duplicated capture logic across the codebase.

### 6.3 `ViewModelBase` and `PanelViewModel`

```csharp
public abstract class ViewModelBase : ObservableObject { }

public abstract class PanelViewModel : ViewModelBase
{
    public abstract string Title { get; }
    public abstract PanelZone Zone { get; }
}

public abstract class SessionPanelViewModel : PanelViewModel
{
    public bool HasSession { get; }
    protected ShellSession? Session { get; }
    public virtual void SetSession(ShellSession? session);
}
```

### 6.4 `IDataTemplate` (ViewLocator)

```csharp
public class ViewLocator : IDataTemplate
{
    public Control? Build(object? param);  // ElfLens.Core.ViewModels.XxxViewModel
                                           // → ElfLens.Views.XxxView
    public bool Match(object? data);       // true if data is ViewModelBase
}
```

---

## 7. Data Flow

### 7.1 Connection Flow

```
User → ConnectPageView (form) → ConnectPageViewModel.ConnectAsync()
  → SshService.ConnectAsync(info) → SSH.NET SshClient.Connect()
  → MainViewModel.OnConnectionSucceeded()
    → ShellPanel.InitializeAsync()           (general SSH shell)
    → FileInfoPanel.RefreshAsync()            (ELF inspection)
    → DisassemblyPanel.RefreshAsync()         (static objdump)
```

### 7.2 Debugging Flow

```
User → GdbDisasmPanelView → StartDebuggingCommand
  → SshService.CreateShellSessionAsync() → ShellSession (GDB)
  → ShellSession.SendCommandAsync("gdb -q <binary>")
  → ShellSession.SendCommandAsync("run")     (start execution)
  → SessionChanged event → MainViewModel
    → BreakpointPanel.SetSession(session)     (apply stored breakpoints)
    → RegistersPanel.SetSession(session)
    → StackPanel.SetSession(session)
    → BottomPanels.Add(gdbShellPanel)
    → SelectedBottomPanel = gdbShellPanel     (auto-switch to GDB tab)
```

### 7.3 Step Flow

```
User → Step Into / Step Over / Continue
  → GdbDisasmPanelViewModel.Step("stepi"/"nexti"/"continue")
  → ShellSession.SendCommandAsync(cmd)
  → RefreshAsync()
    → info registers pc  →  parse PC address
    → disassemble /r      →  parse GDB assembly  →  FunctionBlocks
    → UpdateHighlight     →  highlight current instruction
```

### 7.4 Breakpoint Flow

```
User → Add/Toggle/Remove button OR right-click "Set Breakpoint"
  → BreakpointPanelViewModel.{Add,Toggle,Remove}()
  → ShellSession.SendCommandAsync("break/enable/disable/delete")
  → ShellSession.CaptureOutputAsync → parse GDB response
  → NotifyChanged() → MainViewModel.ReMark()
    → BreakpointPanel.GetFuncBreakpoints()
    → MarkBreakpoints(Functions, bps)          (static method)
    → MarkBreakpoints(FunctionBlocks, bps)     (GDB panel)
```

### 7.5 Stack Memory Flow

```
User → Expand frame
  → StackPanelViewModel.ToggleFrameAsync(frame)
    → ShellSession.CaptureOutputAsync("frame N")    (switch context)
    → ShellSession.CaptureOutputAsync("info frame")  (get boundaries)
    → ShellSession.CaptureOutputAsync("stack N")     (pwndbg) or x/Ngx (fallback)
    → ShellSession.CaptureOutputAsync("frame 0")     (restore context)
    → StackHighlighter.Tokenize(lines) → MemoryLines
```

---

## 8. Panel Layout

```
┌──────────────────┬──────────────────────────────┬──────────────────┐
│   LEFT (220px)   │        CENTER (flex)          │   RIGHT (220px)  │
│                  │                               │                  │
│  ┌─ Registers ─┐ │  ┌─ Disassembly ───────────┐ │  ┌─ File Info ─┐ │
│  │ rax  0x...  │ │  │ 401000 <_start>:        │ │  │ ELF Headers │ │
│  │ rbx  0x...  │ │  │   endbr64               │ │  │ Security    │ │
│  │ rcx  0x...  │ │  │   push rbp              │ │  │ Sections    │ │
│  └─────────────┘ │  └─────────────────────────┘ │  └─────────────┘ │
│                  │                               │                  │
│  ┌─ Stack ─────┐ │  ┌─ GDB ───────────────────┐ │  ┌─ Breakpts ─┐ │
│  │ #0 main     │ │  │  [Step] [Over] [Cont]   │ │  │ *main      │ │
│  │ #1 libc     │ │  │  0x401150 <main+8>:    │ │  │ *func+4    │ │
│  │ #2 _start   │ │  │   mov eax, 0            │ │  │ Add: [___] │ │
│  └─────────────┘ │  └─────────────────────────┘ │  └─────────────┘ │
├──────────────────┴──────────────────────────────┴──────────────────┤
│   BOTTOM (200px)                                                   │
│  ┌─ Shell ───────────────────┐ ┌─ GDB ───────────────────────────┐ │
│  │ $ ls -la                  │ │ pwndbg> info registers          │ │
│  │ ...                       │ │ ...                             │ │
│  └───────────────────────────┘ └─────────────────────────────────┘ │
└────────────────────────────────────────────────────────────────────┘
```

---

## 9. Event System

| Event | Source | Subscribers | Purpose |
|-------|--------|-------------|---------|
| `OnOutput` | `ShellSession` | Panel VMs | Raw shell output chunks |
| `SessionChanged` | `GdbDisasmPanelViewModel` | `MainViewModel` | GDB session lifecycle |
| `BreakpointRequested` | `DisassemblyPanelViewModel`, `GdbDisasmPanelViewModel` | `MainViewModel` | Right-click "Set Breakpoint" |
| `BlocksChanged` | `GdbDisasmPanelViewModel` | `MainViewModel` | New function block added |
| `NavigateToFunction` | `DisassemblyPanelViewModel` | View code-behind | Scroll to function |
| `ScrollToBlock` | `GdbDisasmPanelViewModel` | View code-behind | Scroll to GDB block |
| `OnChanged` (callback) | `BreakpointPanelViewModel` | `MainViewModel` | Breakpoints modified |

---

## 10. Rendering Pipeline

### 10.1 Disassembly Syntax Highlighting

1. Raw output from `objdump -d` or GDB `disassemble /r`
2. Split into lines → `DisassemblyHighlighter.Tokenize(line)` → `List<Token>`
3. Each `Token` has `Text`, `Color` (hex string), optional `NavigateTo`
4. `HighlightConverters.HexToBrush` converts hex colors to `SolidColorBrush`
5. UI renders as horizontal `StackPanel` of colored `TextBlock` elements

### 10.2 Stack Memory Highlighting

1. Raw output from pwndbg `stack N` or GDB `x/Ngx`
2. Split into lines → `StackHighlighter.Tokenize(line)` → `List<Token>`
3. Same token rendering pipeline as disassembly

### 10.3 Breakpoint Marking

1. `BreakpointPanelViewModel.GetFuncBreakpoints()` returns `(func, offset, enabled)` tuples
2. `FunctionItem.MarkBreakpoints()` iterates instructions, computes byte offset from function base
3. `HighlightedLine.IsBreakpoint` (true) + `IsBreakpointDisabled` (false) → red left border
4. `HighlightedLine.IsBreakpoint` (true) + `IsBreakpointDisabled` (true) → orange left border
5. `HighlightConverters.BreakpointBorder` accepts the `HighlightedLine` object and returns the appropriate color

---

## 11. Extension Guide

### 11.1 Adding a New Panel

1. Create ViewModel in `ElfLens.Core/ViewModels/`:
   ```csharp
   public partial class MyPanelViewModel : SessionPanelViewModel
   {
       public override string Title => "My Panel";
       public override PanelZone Zone => PanelZone.Right;
       // Add properties, commands, GDB interaction via Session.CaptureOutputAsync()
   }
   ```
2. Create View in `ElfLens/Views/`:
   ```xml
   <UserControl x:Class="ElfLens.Views.MyPanelView"
                x:DataType="vm:MyPanelViewModel">
       <!-- UI markup -->
   </UserControl>
   ```
3. Register in `MainViewModel` constructor:
   ```csharp
   MyPanel = new MyPanelViewModel();
   RightPanels.Add(MyPanel);  // or LeftPanels, CenterPanels, BottomPanels
   ```
4. Wire session in `SessionChanged` handler:
   ```csharp
   MyPanel.SetSession(session);
   ```

### 11.2 Adding a New GDB Query

```csharp
var output = await Session.CaptureOutputAsync("gdb-command");
// Parse output with Regex, populate ObservableCollection
```

### 11.3 Adding a New Tokenizer

Add a static class in `ElfLens.Core/` with a `Tokenize(string line)` method returning `List<Token>`. Each `Token` has text and a hex color string.

---

## 12. Build & Deployment

```bash
# Development build
dotnet build

# Release publish (self-contained optional)
dotnet publish src/ElfLens/ElfLens.csproj -c Release -o publish/

# Output: publish/ElfLens.exe + dependencies
# Requires .NET 10 Runtime on target machine
```

The published output is a framework-dependent deployment. Copy the entire `publish/` directory to any Windows, Linux, or macOS machine with .NET 10 Runtime installed. Run `ElfLens.exe` (Windows) or `dotnet ElfLens.dll` (Linux/macOS).

---

*Document generated 2026-06-18 for ElfLens v0.1.0*
