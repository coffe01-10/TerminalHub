# Terminal Hub 插件 SDK（宿主 API 1）

[插件开发教程](plugins/development-tutorial.md) · [English tutorial](plugins/development-tutorial.en.md) · [安装与使用](plugins/README.md) · [官方插件](plugins/README.md#三个官方插件) · [清单与工程](#构建和导入) · [生命周期](#生命周期和配置) · [宿主 API](#命令事件和宿主操作)

## 从零开发一个插件

逐步操作、完整源码和配套工程见[插件开发实操教程](plugins/development-tutorial.md)。下面仅为最小接口示意。

需要 .NET 8 SDK；安装用户只需宿主，不需要 SDK。可以克隆仓库修改 `examples/plugins/Minimal`，或下载 [API 1 SDK](https://github.com/coffe01-10/TerminalHub/releases/tag/v0.4.0)，按下方「构建和导入」创建独立类库。

1. 创建 .NET 8 类库，引用 Extensibility、Core 与 Avalonia 11.3.2。不要引用 App。
2. 增加 `plugin.json`，并复制到构建输出目录；`entryType` 填完整类名（含命名空间）。
3. 实现 `IWorkbenchPlugin`，在 `Initialize` 注册视图和命令。
4. `dotnet build -c Release` 后，导入包含清单的 `bin/Release/net8.0`。
5. 工具模块在独立工作区工具窗口左侧显示；命令在命令面板显示。
6. 禁用、重新启用并检查注册清理与配置恢复，再分发构建目录。ZIP 解压后导入。

```csharp
using Avalonia.Controls;
using TerminalHub.Extensibility;

namespace MyPlugin;
public sealed class Plugin : IWorkbenchPlugin
{
    public void Initialize(IPluginContext context)
    {
        context.RegisterView(new("hello", "我的工具"),
            () => new TextBlock { Text = "Hello Terminal Hub" });
        context.RegisterCommand(new("paste", "粘贴示例文本", () =>
        {
            if (context.Host.ActiveSessionId is { } id)
                context.Host.SendInput(id, "echo hello"); // 仅粘贴，不回车
            return Task.CompletedTask;
        }));
    }
    public void Deactivate() { }
}
```

该类的清单应使用 `entryType: "MyPlugin.Plugin"`，`entry` 与生成的 DLL 文件名相同。视图由宿主缓存，不要在每个 OutputBatch 里重新注册。示例不会创建或结束终端进程。

首版插件是独立的 .NET 8 / Avalonia 11.3.2 类库。公开契约位于 `src/TerminalHub.Extensibility`，不需要引用 App 项目，也不需要复制或创建终端进程实现。插件运行在宿主进程中；适合自己编写或信任的本地扩展，首版不提供脚本隔离和在线插件市场。

## 构建和导入

仓库提供三个独立工程：

```powershell
dotnet build examples/plugins/Minimal/Minimal.csproj -c Release
dotnet build examples/plugins/CompactSidebar/CompactSidebar.csproj -c Release
dotnet build examples/plugins/SessionPanel/SessionPanel.csproj -c Release
```

在顶部“插件”或命令面板“管理插件和模块”中，点击“导入插件”，选择对应的 `bin/Release/net8.0` 目录。目录根部必须包含 `plugin.json` 和入口 DLL。导入复制整个构建目录并立即启用；“打开插件目录”可查看本地安装位置。默认安装位置为用户配置目录下的 `plugins`，与工作区配置一样按用户保存。也可以直接放入目录，再点击“刷新”和“启用”。

已启用插件会在下次启动时重新加载。禁用移除此次启用注册的界面、命令、快捷键、订阅、定时器和资源；移除再删除该插件的安装目录。以上操作均不结束宿主终端。

描述文件：

```json
{
  "id": "example.minimal",
  "name": "最小插件",
  "version": "1.0.0",
  "entry": "TerminalHub.Example.Minimal.dll",
  "entryType": "MinimalPlugin",
  "hostApi": 1
}
```

`id` 同时作为安装目录名和配置命名空间。`entry` 相对于插件目录；不指定 `entryType` 时采用第一个实现 `IWorkbenchPlugin` 的非抽象类型。宿主 API 不匹配、DLL 缺失、类型或初始化失败会显示插件名称和具体错误，并清理已注册内容。

独立于仓库开发时，可使用发布 SDK 目录中的 DLL：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Avalonia" Version="11.3.2" />
    <Reference Include="TerminalHub.Extensibility"><HintPath>sdk/TerminalHub.Extensibility.dll</HintPath></Reference>
    <Reference Include="TerminalHub.Core"><HintPath>sdk/TerminalHub.Core.dll</HintPath></Reference>
    <None Update="plugin.json" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
```

## 生命周期和配置

```csharp
public sealed class MyPlugin : IWorkbenchPlugin
{
    public void Initialize(IPluginContext context)
    {
        context.RegisterView(new("hello", "Hello"),
            () => new TextBlock { Text = "Hello Terminal Hub" });
        context.RegisterCommand(new("create", "New terminal", async () =>
        { await context.Host.CreateSessionAsync(new()); }, "Ctrl+Shift+F8"));
    }
    public void Deactivate() { }
}
```

所有 `Register*`、`Subscribe`、`Schedule` 返回可提前释放的 `IDisposable`，同时自动加入此次启用的清理范围。不必在 `Deactivate` 再逐一释放它们。自己的文件监视器、资源等使用 `context.Track(resource)`；自己的后台操作使用 `context.Lifetime` 取消，自己创建的事件订阅需要随资源一起释放。不要绕开这些接口创建无法停用的全局刷新循环。

`ReadConfiguration<T>()` / `SaveConfiguration<T>(value)` 在宿主配置内按插件 ID 保存 JSON。SDK 的 `Directory` 用于读取安装资源；配置与插件代码分开保存。自定义配置页面注册到 `ExtensionSurface.Settings`，展示在插件管理器的“插件设置”页。

已注册命令、订阅、定时器、视图创建和可归因到插件的 UI 异常会显示错误并自动停用该插件。插件自己创建的异步 UI 事件应捕获异常并调用 `ReportError(ex)`；宿主无法接管插件自己创建的后台线程的所有行为。

## 界面扩展

`RegisterView(ModuleDefinition, Func<Control>)` 延迟创建并缓存控件。关闭工作区工具窗口、切换模块或隐藏视图保留控件状态，禁用插件才移除缓存。控件可使用现有 `UiInk`、`UiMuted`、`UiPanel`、`UiBorder`、`UiAccent` 等动态资源，随 DarkGlass / Black / White / Paper 切换。

| Surface | 宿主入口 |
| --- | --- |
| Toolbar | 终端工具栏，保留现有终端操作 |
| WorkspaceTabs | 工作区标签滚动区域 |
| Sidebar | 会话侧栏 |
| StatusBar | 主窗口底部状态区 |
| Menu | 当前终端菜单 |
| SidePanel | 主窗口右侧扩展区域 |
| BottomPanel | 主窗口底部扩展区域 |
| WorkspaceTools | 独立工作区工具窗口的左侧模块导航 |
| Settings | 插件管理器的设置页 |
| ToolWindow | 宿主提供的独立工具窗口 |

Toolbar / WorkspaceTabs / Sidebar / StatusBar 可通过 `Replace: true` 替换默认展示。多个替换同时存在时采用排序后的最后一个；停用后恢复默认组件。替换组件应通过 `context.Host` 查询和激活会话，不能自行接管或关闭 PTY。窗口和面板同样使用宿主接口。

`RegisterStyles(IStyle)` 将样式挂到主窗口，停用后移除，可提供控件模板及动效。`RegisterResources(IResourceProvider)` 将资源字典合并到应用资源；建议用插件 ID 作为自定义资源名前缀，既有主题资源通过动态引用复用。可以构建 Avalonia XAML 类库，也可像示例一样使用 C# 定义控件与样式。

SDK 的 `SvgIcon` 可读取单色 path SVG，继承动态前景色；`ModuleDefinition.IconSvg` 接受 SVG 原文或插件目录中的文件路径，用于工作区工具导航。首版图标约定使用 `viewBox` 和已展开变换的 `<path d="…">`；渐变、文本、外部引用等完整 SVG 绘图功能不在这个轻量图标控件的支持范围。

“模块”页统一管理内置五个工具和插件组件：全局显隐、当前工作区启用状态、排序数字。隐藏工具视图不会停止任务、传输或录制；需要完全停止插件活动时使用“禁用”。

## 命令、事件和宿主操作

`RegisterCommand` 同时加入命令面板、终端菜单和可选 `KeyGesture` 快捷键。`ShowInMenu: false` 仅隐藏菜单入口。输入、工作区迁移与拆分都通过公开宿主方法完成，既有终端仍由工作台持有。

| IWorkbenchHost | 用途 |
| --- | --- |
| Sessions / Workspaces / ActiveSessionId / ActiveWorkspaceId | 查询当前会话与工作区信息 |
| ReadFrame(id, historyOffset) | 稳定屏幕快照；返回独立 cell 数组，避免插件改写宿主绘制缓存 |
| CreateSessionAsync(request) / ActivateSession(id) | 新建或定位终端，弹出终端激活原窗口 |
| CreateWorkspaceAsync(name) / SwitchWorkspace(id) / MoveSession(id, workspaceId) | 新建、切换工作区与迁移原会话 |
| SplitAsync(vertical, sessionId, before) / RemoveFocusedPane() | 当前窗格继续拆分或移除视图，保留原进程 |
| SendInput(id, text, submit: false) | 明确向指定终端粘贴输入；只有显式 submit 才发送回车 |

宿主操作及 UI 注册从 UI 线程调用。后台任务需要通过 Avalonia Dispatcher 回到 UI 线程；长任务使用自己的异步流程和 `Lifetime`。

`Subscribe` 接收 SessionCreated / SessionClosed / ActiveSessionChanged / WorkspaceChanged / CommandStarted / CommandCompleted / OutputBatch / LanguageChanged。Shell 命令事件来自现有 Shell 集成标记，不从输出文本猜测命令完成。弹出、迁移和移除视图不冒充进程关闭。

OutputBatch 的 `Data` 是发生输出变化的 `Guid[]`，每 100 ms 合并消费；按需调用 `ReadFrame`，避免逐字符重建 UI。命令完成事件的 Data 是 `ShellCommandState`。

`RegisterLocalization(defaultLanguage, resources)` 注册语言资源，`Language` 查询当前语言，`Text(key, fallback)` 获取文案。模块标题使用模块 ID 对应的语言 key，命令标题使用命令 ID 对应的 key。语言变化事件可更新已经创建的控件；缺项回退到插件默认语言，再回退到传入文案。

## 示例与本地调试

CompactSidebar 调整标签样式并以紧凑列表替换会话侧栏；SessionPanel 添加会话概览、设置页、新建终端命令和快捷键。二者使用独立 ID、不同扩展位置和各自的配置/注册范围，设计上可以同时加载并单独停用。Minimal 是最小入口工程。

修改后重新构建，禁用旧插件，再导入新构建目录或覆盖安装目录中的构建文件并重新启用。托管 DLL 从流加载，便于本地替换；依赖原生库的插件若遇到文件占用，应在下一次启动时更新。可在 IDE 中附加到 TerminalHub 进程调试插件代码。

三个示例均已编译并在真实 Windows Avalonia 窗口同时启用、分别停用；配置保存、重新启用、语言切换、快捷键、工作区显隐及异常清理另有 Headless 回归。详见 [验收记录](acceptance-v0.4.0-2026-10-03.md)。系统 IME 实际候选窗、多显示器与 Linux 实机未验收。
