# ElfLens

A cross-platform GDB frontend for remote Linux binary analysis. Connect to a Linux host via SSH, inspect ELF binaries, and debug with GDB — all through a modern dark-themed UI.

## Features

- **SSH Connection** — Password or key-based authentication to remote Linux hosts
- **ELF File Inspection** — Headers, sections, security attributes (NX, RELRO, PIE, Stack Canary)
- **Static Disassembly** — objdump-based disassembly with syntax highlighting and collapsible functions
- **GDB Debugging** — Interactive GDB session with step into/over/continue/restart/stop
- **Breakpoints** — Add, toggle, disable, and remove breakpoints; right-click on disassembly lines to set
- **Registers Panel** — View CPU register values with a single click
- **Stack Panel** — Collapsible call stack frames with memory dump (pwndbg `stack` or GDB `x/gx`)
- **Terminals** — Generic SSH shell and dedicated GDB interactive terminal, both dockable
- **Dark Theme** — Avalonia Fluent Dark theme with custom syntax highlighting

## Requirements

- **Runtime**: .NET 10 or later
- **Remote**: Linux host with SSH, GDB, objdump, readelf, and optionally pwndbg
- **Build**: .NET 10 SDK

## Quick Start

1. Launch `ElfLens.exe`
2. Enter SSH credentials and target binary path
3. Click **Connect**
4. Click **Refresh** on File Info and Disassembly panels
5. Click **Start Debugging** on the GDB panel to begin debugging
6. Add breakpoints, inspect registers and stack, step through code

## Build

```bash
dotnet build
dotnet publish src/ElfLens/ElfLens.csproj -c Release -o publish/
```

The published output is in `publish/`. Copy the entire directory to the target machine.

## Project Structure

```
ElfLens/           — Avalonia UI (Views, Converters, App)
ElfLens.Core/      — Logic layer (ViewModels, Models, Services, Tokenizers)
```

## License

GNU General Public License v3.0 — see [LICENSE](LICENSE).
