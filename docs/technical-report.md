# ElfLens 技术报告

> 版本 0.1.0 — 跨平台 GDB 远程调试前端

---

## 1. 项目概述

ElfLens 是一款基于 Avalonia UI 的桌面端 GDB 图形化前端，通过 SSH 远程连接 Linux 主机，提供反汇编浏览、断点管理、寄存器查看、调用栈分析等功能。目标用户为二进制分析师、CTF 参赛者、漏洞研究人员及嵌入式系统开发者。

---

## 2. 技术栈

| 层次 | 技术 |
|------|------|
| UI 框架 | Avalonia 11（Fluent Dark 主题） |
| 运行时 | .NET 10 |
| MVVM 框架 | CommunityToolkit.Mvvm 8.x |
| SSH 库 | SSH.NET（Renci.SshNet）2024.x |
| 编程语言 | C# 12 |
| 构建系统 | .NET SDK / MSBuild |
| 许可证 | GNU General Public License v3.0 |

**运行依赖**：客户端需安装 .NET 10 Runtime。远程主机需开启 SSH 服务，安装 GDB、GNU binutils（objdump、readelf），可选安装 pwndbg 以获得增强的栈可视化效果。

---

## 3. 项目结构

### 3.1 命名空间层次

```
ElfLens（表现层）
├── Views                — Avalonia UserControl/Window，.axaml 标记 + code-behind
├── Converters           — 数据绑定用 IValueConverter 实现
├── App.axaml            — 应用入口，Fluent Dark 主题，ViewLocator 注册
├── ViewLocator.cs       — IDataTemplate：按命名约定将 ViewModel 映射到 View
└── Program.cs           — .NET 应用入口点

ElfLens.Core（逻辑层）
├── Models               — 领域对象（SshConnectionInfo、ShellSession）
├── Services             — 服务接口与实现（ISshService、SshService）
├── ViewModels           — MVVM 视图模型，按面板和角色组织
├── DisassemblyHighlighter.cs — objdump/GDB 反汇编输出分词器
├── StackHighlighter.cs       — GDB 栈/内存转储输出分词器
└── RegisterNames.cs          — 共享的 x86/x86_64 寄存器名称常量
```

### 3.2 核心文件

| 文件 | 行数 | 用途 |
|------|------|------|
| `MainViewModel.cs` | ~120 | 应用壳，面板装配，事件路由 |
| `GdbDisasmPanelViewModel.cs` | ~320 | GDB 交互调试、反汇编、步进控制 |
| `ShellSession.cs` | ~100 | SSH 壳流封装，异步命令/响应 |
| `DisassemblyPanelViewModel.cs` | ~150 | objdump 静态反汇编与断点标记 |
| `BreakpointPanelViewModel.cs` | ~170 | 断点增删改查，GDB 命令执行 |
| `StackPanelViewModel.cs` | ~150 | 调用栈帧，可折叠的内存转储 |
| `DisassemblyHighlighter.cs` | ~120 | x86 反汇编行的分词着色 |
| `StackHighlighter.cs` | ~180 | pwndbg stack / GDB x/gx 输出分词着色 |
| `SshService.cs` | ~130 | SSH.NET 封装：连接、执行、Shell 创建 |

---

## 4. 架构设计

### 4.1 分层架构

```
┌─────────────────────────────────────────┐
│  表现层 (ElfLens)                        │
│  Views (.axaml) ← Converters            │
│       ↕ 数据绑定                         │
├─────────────────────────────────────────┤
│  视图模型层 (ElfLens.Core.ViewModels)     │
│  MainViewModel → 各面板 ViewModel        │
│       ↕ 方法调用                         │
├─────────────────────────────────────────┤
│  服务层 (ElfLens.Core.Services)          │
│  ISshService → SshService               │
│       ↕ 流 I/O                          │
├─────────────────────────────────────────┤
│  模型层 (ElfLens.Core.Models)            │
│  ShellSession、SshConnectionInfo         │
└─────────────────────────────────────────┘
```

数据沿"视图模型→服务→模型"方向向下传递命令，沿"模型→服务→视图模型→视图"方向通过事件和可观察属性向上传递响应。

### 4.2 MVVM 模式

ElfLens 使用 CommunityToolkit.Mvvm 实现 Model-View-ViewModel 模式：

- **模型**：`SshConnectionInfo`、`ShellSession` — 封装 SSH 状态和 I/O
- **视图模型**：继承 `ViewModelBase`（扩展 `ObservableObject`）— 通过 `[ObservableProperty]` 和 `[RelayCommand]` 源代码生成器暴露可绑定属性和命令
- **视图**：Avalonia `UserControl` 子类 — 通过 `{Binding}` 标记扩展绑定到视图模型

`ViewLocator` 按命名约定解析视图模型到视图：`XxxViewModel` → `XxxView`（例如 `RegistersPanelViewModel` → `RegistersPanelView`）。

---

## 5. 类层次结构

### 5.1 视图模型继承树

```
CommunityToolkit.Mvvm.ComponentModel.ObservableObject
└── ViewModelBase                          (ElfLens.Core.ViewModels)
    ├── ConnectPageViewModel               SSH 连接表单逻辑
    │
    └── PanelViewModel (abstract)          可停靠面板基类
        │   属性: Title, Zone
        │
        ├── SessionPanelViewModel (abstract)  持有 GDB 会话的面板
        │   属性: Session, HasSession
        │   方法: SetSession(ShellSession?)
        │   ├── BreakpointPanelViewModel   断点管理
        │   ├── RegistersPanelViewModel    CPU 寄存器显示
        │   └── StackPanelViewModel        调用栈与内存转储
        │
        ├── ShellPanelViewModel            交互式终端
        ├── FileInfoPanelViewModel         ELF 文件检测
        ├── DisassemblyPanelViewModel      objdump 静态反汇编
        └── GdbDisasmPanelViewModel        GDB 交互式调试

MainViewModel (组合全部面板 VM，连接事件)
```

### 5.2 面板层次

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

### 5.3 数据记录类型

```
Token(Text, Color, NavigateTo?)               (record)
HighlightedLine(Tokens, IsCurrent?, IsBreakpoint?, IsBreakpointDisabled?)  (record)
FunctionItem(Name, Address, Instructions) : ObservableObject
  └── IsExpanded, IsCurrent, ToggleCommand

RegisterEntry(Name, HexValue, DecValue)       (record)
StackFrameItem(FrameNum, Function, Address, RawLine) : ObservableObject
  └── IsExpanded, IsLoading, MemoryLines
BreakpointEntry(Location) : ObservableObject
  └── Enabled, GdbNum, ResolvedAddr, ResolvedFunc
```

---

## 6. 核心接口与服务

### 6.1 `ISshService` 接口

```csharp
public interface ISshService
{
    bool IsConnected { get; }
    SshConnectionInfo? ConnectionInfo { get; }

    Task<bool> ConnectAsync(SshConnectionInfo info);     // 建立连接
    Task DisconnectAsync();                               // 断开连接
    Task<string> ExecuteCommandAsync(string command);     // 非交互式命令
    Task<ShellSession?> CreateShellSessionAsync();         // 交互式 Shell
}
```

`SshService` 实现了 `ISshService` 和 `IDisposable`。它封装了 SSH.NET 的 `SshClient`，提供：
- 连接生命周期管理
- 通过 SSH.NET `CreateCommand()` 执行单条命令，超时 30 秒
- 通过 `CreateShellStream()` 创建交互式 Shell 会话，启用终端模拟（xterm-256color，200×40）
- 断开时自动清理

### 6.2 `ShellSession` 类

与 GDB 交互的核心抽象：

```csharp
public class ShellSession : IDisposable
{
    public event Action<string>? OnOutput;              // 原始输出数据块

    public async Task SendCommandAsync(string command);  // 写入 Shell
    public async Task<string> CaptureOutputAsync(        // 发送并等待响应
        string command, int timeoutMs = 800,
        Func<string, bool>? stopPredicate = null);

    public void Dispose();                               // 取消读取循环，释放流
}
```

**`CaptureOutputAsync`** 是所有面板使用的统一命令/响应模式。其工作流程：
1. 订阅 `OnOutput` 事件
2. 通过 `SendCommandAsync` 发送命令
3. 等待以下任一条件满足：停止谓词匹配、GDB 提示符出现（`(gdb)`/`pwndbg>`）、输出超过 8000 字符、超时到期
4. 取消订阅，返回累积的输出字符串

该模式消除了代码库中约 100 行重复的捕获逻辑。

### 6.3 `ViewModelBase` 和 `PanelViewModel`

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

### 6.4 `IDataTemplate`（ViewLocator）

```csharp
public class ViewLocator : IDataTemplate
{
    public Control? Build(object? param);  // ElfLens.Core.ViewModels.XxxViewModel
                                           // → ElfLens.Views.XxxView
    public bool Match(object? data);       // data 是 ViewModelBase 时返回 true
}
```

---

## 7. 数据流

### 7.1 连接流程

```
用户 → ConnectPageView（表单）→ ConnectPageViewModel.ConnectAsync()
  → SshService.ConnectAsync(info) → SSH.NET SshClient.Connect()
  → MainViewModel.OnConnectionSucceeded()
    → ShellPanel.InitializeAsync()           （通用 SSH Shell）
    → FileInfoPanel.RefreshAsync()            （ELF 文件检测）
    → DisassemblyPanel.RefreshAsync()         （静态反汇编）
```

### 7.2 调试启动流程

```
用户 → GdbDisasmPanelView → StartDebuggingCommand
  → SshService.CreateShellSessionAsync() → ShellSession（GDB）
  → ShellSession.SendCommandAsync("gdb -q <binary>")
  → ShellSession.SendCommandAsync("run")     （开始执行）
  → SessionChanged 事件 → MainViewModel
    → BreakpointPanel.SetSession(session)     （应用已存储的断点）
    → RegistersPanel.SetSession(session)
    → StackPanel.SetSession(session)
    → BottomPanels.Add(gdbShellPanel)
    → SelectedBottomPanel = gdbShellPanel     （自动切到 GDB 标签页）
```

### 7.3 单步执行流程

```
用户 → Step Into / Step Over / Continue
  → GdbDisasmPanelViewModel.Step("stepi"/"nexti"/"continue")
  → ShellSession.SendCommandAsync(cmd)
  → RefreshAsync()
    → info registers pc  →  解析 PC 地址
    → disassemble /r      →  解析 GDB 汇编 → FunctionBlocks
    → UpdateHighlight     →  高亮当前指令
```

### 7.4 断点流程

```
用户 → Add/Toggle/Remove 按钮 或 右键 "Set Breakpoint"
  → BreakpointPanelViewModel.{Add,Toggle,Remove}()
  → ShellSession.SendCommandAsync("break/enable/disable/delete")
  → ShellSession.CaptureOutputAsync → 解析 GDB 响应
  → NotifyChanged() → MainViewModel.ReMark()
    → BreakpointPanel.GetFuncBreakpoints()
    → MarkBreakpoints(Functions, bps)          （静态面板）
    → MarkBreakpoints(FunctionBlocks, bps)     （GDB 面板）
```

### 7.5 栈内存查看流程

```
用户 → 展开栈帧
  → StackPanelViewModel.ToggleFrameAsync(frame)
    → ShellSession.CaptureOutputAsync("frame N")     （切换上下文）
    → ShellSession.CaptureOutputAsync("info frame")   （获取帧边界）
    → ShellSession.CaptureOutputAsync("stack N")      （pwndbg）或 x/Ngx（回退）
    → ShellSession.CaptureOutputAsync("frame 0")      （恢复上下文）
    → StackHighlighter.Tokenize(lines) → MemoryLines
```

---

## 8. 面板布局

```
┌──────────────────┬──────────────────────────────┬──────────────────┐
│   左侧 (220px)    │        中部 (自适应)          │   右侧 (220px)    │
│                  │                               │                  │
│  ┌─ Registers ─┐ │  ┌─ Disassembly ───────────┐ │  ┌─ File Info ─┐ │
│  │ rax  0x...  │ │  │ 401000 <_start>:        │ │  │ ELF 头部    │ │
│  │ rbx  0x...  │ │  │   endbr64               │ │  │ 安全属性    │ │
│  │ rcx  0x...  │ │  │   push rbp              │ │  │ 节区信息    │ │
│  └─────────────┘ │  └─────────────────────────┘ │  └─────────────┘ │
│                  │                               │                  │
│  ┌─ Stack ─────┐ │  ┌─ GDB ───────────────────┐ │  ┌─ Breakpts ─┐ │
│  │ #0 main     │ │  │  [Step] [Over] [Cont]   │ │  │ *main      │ │
│  │ #1 libc     │ │  │  0x401150 <main+8>:    │ │  │ *func+4    │ │
│  │ #2 _start   │ │  │   mov eax, 0            │ │  │ Add: [___] │ │
│  └─────────────┘ │  └─────────────────────────┘ │  └─────────────┘ │
├──────────────────┴──────────────────────────────┴──────────────────┤
│   底部 (200px)                                                      │
│  ┌─ Shell ───────────────────┐ ┌─ GDB ───────────────────────────┐ │
│  │ $ ls -la                  │ │ pwndbg> info registers          │ │
│  │ ...                       │ │ ...                             │ │
│  └───────────────────────────┘ └─────────────────────────────────┘ │
└────────────────────────────────────────────────────────────────────┘
```

---

## 9. 事件系统

| 事件 | 来源 | 订阅者 | 用途 |
|------|------|--------|------|
| `OnOutput` | `ShellSession` | 各面板 VM | 原始 Shell 输出数据块 |
| `SessionChanged` | `GdbDisasmPanelViewModel` | `MainViewModel` | GDB 会话生命周期 |
| `BreakpointRequested` | `DisassemblyPanelViewModel`、`GdbDisasmPanelViewModel` | `MainViewModel` | 右键"设置断点" |
| `BlocksChanged` | `GdbDisasmPanelViewModel` | `MainViewModel` | 新函数块已添加 |
| `NavigateToFunction` | `DisassemblyPanelViewModel` | View code-behind | 滚动到指定函数 |
| `ScrollToBlock` | `GdbDisasmPanelViewModel` | View code-behind | 滚动到 GDB 块 |
| `OnChanged`（回调） | `BreakpointPanelViewModel` | `MainViewModel` | 断点已修改 |

---

## 10. 渲染管线

### 10.1 反汇编语法高亮

1. `objdump -d` 或 GDB `disassemble /r` 的原始输出
2. 按行切分 → `DisassemblyHighlighter.Tokenize(line)` → `List<Token>`
3. 每个 `Token` 包含 `Text`（文本）、`Color`（十六进制颜色字符串）、可选的 `NavigateTo`（导航目标）
4. `HighlightConverters.HexToBrush` 将十六进制颜色转换为 `SolidColorBrush`
5. UI 以横向 `StackPanel` 渲染，内含多个彩色 `TextBlock` 元素

### 10.2 栈内存高亮

1. pwndbg `stack N` 或 GDB `x/Ngx` 的原始输出
2. 按行切分 → `StackHighlighter.Tokenize(line)` → `List<Token>`
3. 使用与反汇编相同的 token 渲染管线

### 10.3 断点标记

1. `BreakpointPanelViewModel.GetFuncBreakpoints()` 返回 `(func, offset, enabled)` 元组
2. `FunctionItem.MarkBreakpoints()` 遍历指令，从函数基址计算字节偏移
3. `HighlightedLine.IsBreakpoint`（true）+ `IsBreakpointDisabled`（false）→ 红色左边框
4. `HighlightedLine.IsBreakpoint`（true）+ `IsBreakpointDisabled`（true）→ 橙色左边框
5. `HighlightConverters.BreakpointBorder` 接收整个 `HighlightedLine` 对象并返回对应颜色

---

## 11. 扩展指南

### 11.1 新增面板

1. 在 `ElfLens.Core/ViewModels/` 中创建视图模型：
   ```csharp
   public partial class MyPanelViewModel : SessionPanelViewModel
   {
       public override string Title => "我的面板";
       public override PanelZone Zone => PanelZone.Right;
       // 通过 Session.CaptureOutputAsync() 交互 GDB
   }
   ```
2. 在 `ElfLens/Views/` 中创建视图：
   ```xml
   <UserControl x:Class="ElfLens.Views.MyPanelView"
                x:DataType="vm:MyPanelViewModel">
       <!-- UI 标记 -->
   </UserControl>
   ```
3. 在 `MainViewModel` 构造函数中注册：
   ```csharp
   MyPanel = new MyPanelViewModel();
   RightPanels.Add(MyPanel);  // 或 LeftPanels、CenterPanels、BottomPanels
   ```
4. 在 `SessionChanged` 处理器中连接会话：
   ```csharp
   MyPanel.SetSession(session);
   ```

### 11.2 新增 GDB 查询

```csharp
var output = await Session.CaptureOutputAsync("gdb-command");
// 用正则解析 output，填充 ObservableCollection
```

### 11.3 新增分词器

在 `ElfLens.Core/` 中新增静态类，包含 `Tokenize(string line)` 方法，返回 `List<Token>`。每个 `Token` 包含文本和十六进制颜色字符串。

---

## 12. 构建与部署

```bash
# 开发构建
dotnet build

# 发布构建
dotnet publish src/ElfLens/ElfLens.csproj -c Release -o release/

# 输出：release/ElfLens.exe + 依赖项
# 目标机器需安装 .NET 10 Runtime
```

发布输出为框架依赖部署。将整个 `release/` 目录复制到安装了 .NET 10 Runtime 的 Windows、Linux 或 macOS 机器上。Windows 下运行 `ElfLens.exe`，Linux/macOS 下运行 `dotnet ElfLens.dll`。

---

*文档生成于 2026-06-18，适用于 ElfLens v0.1.0*
