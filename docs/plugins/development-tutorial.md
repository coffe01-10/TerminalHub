# Terminal Hub 插件开发教程：从零写一个命令草稿工具

中文 · [English](development-tutorial.en.md)

这篇教程面向插件开发者。完成后，你会得到一个真正可以导入 Terminal Hub 的 .NET 插件：显示活动终端、编辑命令草稿、点击粘贴、保存配置，并在切换会话时更新界面。后半部分教你添加设置页、多语言和后台任务。

[准备环境](#prepare) · [创建工程](#project) · [编写清单](#manifest) · [实现插件](#implementation) · [理解代码](#explain) · [构建导入](#run) · [扩展功能](#extend) · [打包调试](#distribute) · [常见问题](#troubleshooting)

[配套完整工程](../../examples/plugins/CommandDraft) · [SDK API 参考](../plugin-sdk.md) · [官方插件源码](../../plugins)

<a id="prepare"></a>

## 1. 准备环境

你需要 .NET 8 SDK、一个代码编辑器，以及 Terminal Hub v0.4.0 或更新版本。Avalonia 依赖版本为 11.3.2，宿主 API 为 1。插件是类库，由宿主加载；不能用 `dotnet run` 单独启动插件。

在终端执行 `dotnet --list-sdks`，确认有 `8.0.xxx`。然后从 [v0.4.0 发行页面](https://github.com/coffe01-10/TerminalHub/releases/tag/v0.4.0) 下载 `TerminalHub-PluginSDK-1.zip` 并解压。包内 `sdk/` 目录包含 `TerminalHub.Extensibility.dll` 和 `TerminalHub.Core.dll`。

如果你已经克隆了仓库，也可以直接构建配套工程：

```powershell
dotnet build examples/plugins/CommandDraft/CommandDraft.csproj -c Release
```

下面采用独立 SDK 开发方式，不需要引用或编译 App 项目。

<a id="project"></a>

## 2. 创建工程并引用 SDK

在你自己的开发目录运行：

```powershell
dotnet new classlib -n CommandDraft -f net8.0
cd CommandDraft
```

删除模板生成的 `Class1.cs`。创建 `sdk` 目录，把发行 SDK 中上面两个 DLL 复制进来。用下面内容替换 `CommandDraft.csproj`：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <AssemblyName>TerminalHub.Example.CommandDraft</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Avalonia" Version="11.3.2" />
    <Reference Include="TerminalHub.Extensibility">
      <HintPath>sdk/TerminalHub.Extensibility.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="TerminalHub.Core">
      <HintPath>sdk/TerminalHub.Core.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <None Update="plugin.json" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
```

`Avalonia` 用于创建原生界面。`Extensibility` 定义插件接口；`Core` 提供屏幕帧、命令状态等接口涉及的类型。`Private=false` 表示不把宿主提供的两个 SDK DLL 重复复制到插件输出目录。`AssemblyName` 决定实际生成的 DLL 文件名。

工程最终应该是：

```text
CommandDraft/
  CommandDraft.csproj
  CommandDraftPlugin.cs
  plugin.json
  sdk/
    TerminalHub.Extensibility.dll
    TerminalHub.Core.dll
```

<a id="manifest"></a>

## 3. 编写插件清单

在工程根目录创建 `plugin.json`：

```json
{
  "id": "tutorial.command-draft",
  "name": "命令草稿（教程示例）",
  "version": "1.0.0",
  "entry": "TerminalHub.Example.CommandDraft.dll",
  "entryType": "TerminalHub.Example.CommandDraft.CommandDraftPlugin",
  "hostApi": 1
}
```

| 字段 | 这份示例的含义 |
| --- | --- |
| `id` | 插件唯一标识，也是安装目录名和配置命名空间；发布自己的插件时改成自己的 ID。 |
| `name` | 插件管理器显示的名称。 |
| `version` | 插件自身版本，与宿主版本独立。 |
| `entry` | 插件目录中的入口 DLL；必须与工程 `AssemblyName` 一致。 |
| `entryType` | 实现 `IWorkbenchPlugin` 的完整类名，包含 namespace。 |
| `hostApi` | 本教程使用宿主 API 1。 |

前一步的 `CopyToOutputDirectory` 会把清单放在 DLL 旁边；没有这条，构建可能成功，但导入输出目录时找不到清单。

<a id="implementation"></a>

## 4. 实现一个完整插件

创建 `CommandDraftPlugin.cs`，把下面代码完整复制进去。这里所有控件都用 C# 创建，因此无需另外创建 XAML 文件。

```csharp
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using TerminalHub.Extensibility;

namespace TerminalHub.Example.CommandDraft;

public sealed class CommandDraftPlugin : IWorkbenchPlugin
{
    public sealed record Options(string Draft = "echo hello");

    private IPluginContext _context = null!;
    private Options _options = new();
    private TextBlock? _sessionLabel;
    private TextBox? _editor;
    private Button? _pasteButton, _saveButton;

    public void Initialize(IPluginContext context)
    {
        _context = context;
        _options = context.ReadConfiguration<Options>() ?? new();

        context.RegisterView(new("draft", "命令草稿", ExtensionSurface.WorkspaceTools), CreateView);
        context.RegisterCommand(new("paste-draft", "粘贴命令草稿", () =>
        {
            PasteDraft();
            return Task.CompletedTask;
        }));
        context.Subscribe(e =>
        {
            if (e.Kind is WorkbenchEventKind.ActiveSessionChanged
                or WorkbenchEventKind.WorkspaceChanged
                or WorkbenchEventKind.SessionCreated
                or WorkbenchEventKind.SessionClosed)
                RefreshSession();
        });
    }

    private Control CreateView()
    {
        var panel = new StackPanel { Margin = new(20), Spacing = 12 };
        var title = new TextBlock { Text = "命令草稿", FontSize = 22 };
        title.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("UiInk"));
        panel.Children.Add(title);

        _sessionLabel = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        _sessionLabel.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("UiMuted"));
        panel.Children.Add(_sessionLabel);

        _editor = new TextBox { Text = _options.Draft, Watermark = "例如：echo hello" };
        _editor.Bind(TextBox.ForegroundProperty, new DynamicResourceExtension("UiInk"));
        _editor.Bind(TextBox.BackgroundProperty, new DynamicResourceExtension("UiInset"));
        _editor.Bind(TextBox.BorderBrushProperty, new DynamicResourceExtension("UiBorder"));
        panel.Children.Add(_editor);

        var actions = new WrapPanel();
        _pasteButton = ActionButton("粘贴到活动终端", PasteDraft);
        _saveButton = ActionButton("保存草稿", SaveDraft);
        actions.Children.Add(_pasteButton); actions.Children.Add(_saveButton);
        panel.Children.Add(actions);

        var hint = new TextBlock { Text = "粘贴不会发送回车。保存后，重新启用插件可恢复草稿。", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        hint.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("UiMuted"));
        panel.Children.Add(hint);
        RefreshSession();
        return panel;
    }

    private Button ActionButton(string title, Action action)
    {
        var button = new Button { Content = title, Padding = new(12, 8), Margin = new(0, 0, 8, 8), CornerRadius = new(6) };
        button.Bind(Button.ForegroundProperty, new DynamicResourceExtension("UiInk"));
        button.Bind(Button.BackgroundProperty, new DynamicResourceExtension("UiRaised"));
        button.Bind(Button.BorderBrushProperty, new DynamicResourceExtension("UiBorder"));
        button.Click += (_, _) =>
        {
            try { action(); }
            catch (Exception ex) { _context.ReportError(ex); }
        };
        return button;
    }

    private void RefreshSession()
    {
        var session = _context.Host.Sessions.FirstOrDefault(s => s.Id == _context.Host.ActiveSessionId);
        if (_sessionLabel is not null)
            _sessionLabel.Text = session is null ? "没有活动会话" : $"目标：{session.Name}\n{session.WorkingDirectory}";
        if (_pasteButton is not null) _pasteButton.IsEnabled = session?.Running == true;
    }

    private void PasteDraft()
    {
        var session = _context.Host.Sessions.FirstOrDefault(s => s.Id == _context.Host.ActiveSessionId);
        if (session?.Running != true) return;
        _context.Host.SendInput(session.Id, _editor?.Text ?? _options.Draft, submit: false);
    }

    private void SaveDraft()
    {
        _options = new(_editor?.Text ?? _options.Draft);
        _context.SaveConfiguration(_options);
    }

    public void Deactivate() { }
}
```

<a id="explain"></a>

## 5. 理解这段代码

### 初始化与视图创建

宿主启用插件时实例化 `CommandDraftPlugin`，然后调用 `Initialize(context)`。此时读取配置，注册模块、命令和订阅。

`RegisterView(..., CreateView)` 传入的是工厂函数，直到用户选择模块才创建控件。默认工具位置是 `WorkspaceTools`，对应独立工作区工具窗口的左侧导航。宿主缓存控件，因此关闭工具窗口、再打开，草稿仍在。

`Deactivate()` 表示插件停用，不是工具窗口关闭。宿主自动清理本次注册的视图、命令和订阅，示例无需逐个 Dispose。本示例没有自动保存未保存的编辑；只恢复你点过「保存草稿」的内容。

### 命令面板与按钮调用同一个操作

`RegisterCommand` 让「粘贴命令草稿」出现在命令面板和终端菜单。按钮和命令共同调用 `PasteDraft()`，因此它们使用相同目标和输入逻辑。即使你还没打开工具页，命令面板也能粘贴已保存的草稿。

`ActiveSessionId` 在执行时查询，避免把输入发到曾经选中的旧会话。没有运行中的活动会话时按钮禁用，命令直接返回。`SendInput(..., submit: false)` 不自动追加回车；此示例用单行编辑器，用户可在终端检查后自行回车。

这里使用 `submit: false` 不能理解成任意文本都不执行：如果你将编辑器改成多行，文本本身的换行可能被 Shell 解释为提交。多行输入需要单独设计用户操作和说明。

### 配置保存在宿主中

`Options` 是可以 JSON 序列化的配置类型。初始化用 `ReadConfiguration<Options>()` 恢复；点保存用 `SaveConfiguration(_options)` 写入。配置按清单 ID 隔离，不需要自己找 settings.json，不要把用户配置写进插件安装目录。

给配置类型新增带默认值的字段，可以让旧配置继续加载。升级时保留插件 ID，才能继续读取原配置；更换 ID 会成为另一个插件。

### 事件只刷新相关信息

切换会话、切换工作区、新建和关闭会话时，`Subscribe` 更新目标会话名称和目录。这些是宿主事件，不需要自己轮询。

示例不订阅输出，因为它不展示终端输出。需要展示时，订阅 `OutputBatch`，从 `e.Data is Guid[] ids` 取变化的会话，再按需 `Host.ReadFrame(id)`。不要每个字符重建整个界面。

### 动态资源让插件跟随主题

`DynamicResourceExtension("UiInk")` 等资源在主题变化后重新取色。文本、编辑器、按钮使用同一套宿主色彩；控件的字体和排版沿用所在工具窗口。

`UiInk` 是正文，`UiMuted` 是次要文字，`UiInset` 是编辑区域，`UiRaised` 是按钮背景，`UiBorder` 是边框。要创建强调按钮，可使用 `UiAccent` 和 `UiOnAccent`。新建自己的资源时，用插件 ID 作前缀，避免覆盖别人的资源。

<a id="run"></a>

## 6. 构建、导入并运行

在工程根目录执行：

```powershell
dotnet build -c Release
```

成功后至少应该看到：

```text
bin/Release/net8.0/
  plugin.json
  TerminalHub.Example.CommandDraft.dll
  TerminalHub.Example.CommandDraft.deps.json
```

打开 Terminal Hub 的「插件」→「导入插件」，选择这个 `net8.0` 目录。导入会复制文件并启用插件。随后打开「工作区工具」，在左侧选择「命令草稿」。

你应该看到标题、活动会话名与目录、一个草稿输入框和两个按钮：

1. 输入 `echo tutorial`，点「粘贴到活动终端」，终端输入区出现文字；自行回车后才执行。
2. 切换到另一个终端，目标标签更新，再点粘贴会发送到新的活动会话。
3. 点「保存草稿」，禁用再启用，输入框恢复 `echo tutorial`。
4. 正常退出并重开宿主，已启用的插件自动加载，已保存的草稿仍能恢复。

<a id="extend"></a>

## 7. 给你的插件增加功能

### 增加插件设置页

在 `Initialize` 中注册一个 Settings 模块。下面例子复用 `SaveDraft`，只演示设置页面的位置；真实设置可放 CheckBox、ComboBox 等控件。

```csharp
context.RegisterView(
    new("settings", "命令草稿设置", ExtensionSurface.Settings),
    () => ActionButton("保存当前草稿", SaveDraft));
```

打开插件管理器「插件设置」，这个模块就会出现。工具页与设置页必须创建各自的控件，不能把同一个 Button 同时挂在两个父容器里。

### 为命令添加快捷键

在原有 `RegisterCommand` 调用中增加命名参数：

```csharp
context.RegisterCommand(new("paste-draft", "粘贴命令草稿", () =>
{
    PasteDraft();
    return Task.CompletedTask;
}, Gesture: "Ctrl+Shift+F9"));
```

这是替换原注册，不要再用相同 ID 注册第二次。选快捷键时检查宿主常用键位和其他启用的插件，避免同一按键执行别的操作。

### 添加中英文

在注册视图和命令前加入语言资源：

```csharp
context.RegisterLocalization("zh-CN",
    new Dictionary<string, IReadOnlyDictionary<string, string>>
    {
        ["zh-CN"] = new Dictionary<string, string>
        {
            ["draft"] = "命令草稿",
            ["paste-draft"] = "粘贴命令草稿",
            ["paste-button"] = "粘贴到活动终端"
        },
        ["en"] = new Dictionary<string, string>
        {
            ["draft"] = "Command draft",
            ["paste-draft"] = "Paste command draft",
            ["paste-button"] = "Paste into active terminal"
        }
    });
```

模块 ID `draft` 和命令 ID `paste-draft` 对应的标题由宿主读取。已经创建的自定义控件也要自己更新，例如将按钮创建文字改为 `context.Text("paste-button", "粘贴到活动终端")`，再在订阅中响应语言变化：

```csharp
if (e.Kind == WorkbenchEventKind.LanguageChanged && _pasteButton is not null)
    _pasteButton.Content = context.Text("paste-button", "粘贴到活动终端");
```

想让整个页面双语，就为标题、保存按钮、提示和空状态分别加 key。只翻译插件自己的界面文案，会话名、用户草稿、命令、路径保留原文。

### 创建新终端或调整布局

用公开 Host 操作，不要创建自己的 ConPTY 进程：

```csharp
var id = await context.Host.CreateSessionAsync(
    new NewSessionRequest(Name: "构建", WorkingDirectory: "/your/project"));
if (id is { } sessionId)
    context.Host.ActivateSession(sessionId);
```

路径换成所在系统的实际项目目录；Windows 可用 `D:/Projects/MyApp`。`SplitAsync(vertical: false, sessionId: id)` 在当前焦点窗格左右拆分，`vertical: true` 为上下拆分。移除窗格视图不等于结束会话进程。

### 增加定时刷新与异步任务

周期刷新用宿主管理的计时器：

```csharp
context.Schedule(TimeSpan.FromSeconds(1), RefreshSession);
```

它在 UI 线程运行，停用自动停止。已有宿主事件能满足需求时不必再加计时器。耗时操作使用 async，不要在计时器里阻塞 UI。

例如注册一个读取插件资源的异步命令：

```csharp
context.RegisterCommand(new("load-draft", "加载预置草稿", async () =>
{
    var path = Path.Combine(context.Directory, "default-command.txt");
    var text = await File.ReadAllTextAsync(path, context.Lifetime);
    context.Lifetime.ThrowIfCancellationRequested();
    _options = new(text.TrimEnd('\r', '\n'));
    if (_editor is not null) _editor.Text = _options.Draft;
}));
```

这里异步从 UI 命令开始，await 返回 UI 上下文。把 `default-command.txt` 加入工程并设置 `CopyToOutputDirectory`，才能随插件分发。单行编辑器的预置内容也应为单行。注册命令的异常由宿主处理；自己写 `async` Click 事件时捕获异常并 `ReportError(ex)`，停用取消应单独处理。自己启动后台线程时，用 `Avalonia.Threading.Dispatcher.UIThread` 回到 UI 线程后再操作控件和 Host。

<a id="distribute"></a>

## 8. 打包、更新和调试

插件包以目录为单位。把清单、入口 DLL、`.deps.json` 和自己的依赖/资源放进同一个目录，再压成 ZIP。宿主提供 Extensibility、Core、Avalonia；自己的额外第三方库要随包提供。

这个示例可以在工程根目录按明确文件列表打包：

```powershell
Compress-Archive -Path bin/Release/net8.0/plugin.json,bin/Release/net8.0/TerminalHub.Example.CommandDraft.dll,bin/Release/net8.0/TerminalHub.Example.CommandDraft.deps.json -DestinationPath CommandDraft-1.0.0.zip -Force
```

这条命令只适合本教程没有额外资源/依赖的版本。后续新增资源后，也把它们加入列表。用户解压 ZIP 后导入目录；插件管理器不直接导入 ZIP。不要包含用户 settings.json 或 SSH 记录。

修改代码后重新构建，再导入新输出目录。保留相同 ID、提高 version，即可更新原插件并继续使用配置。托管 DLL 从流加载，可以更新；带原生库的插件可能仍占用文件，需要正常退出宿主后替换。

在 Visual Studio 或 Rider 中附加到运行中的 `TerminalHub` 进程，在 `Initialize`、`CreateView`、`PasteDraft` 设置断点。Release 构建的 PDB 可留在自己的调试目录。重新导入触发新插件实例；单纯改源码不会更新当前加载的 DLL。

<a id="troubleshooting"></a>

## 9. 常见问题与下一步

| 现象 | 检查哪里 |
| --- | --- |
| 编译找不到 IPluginContext 或 TerminalFrame | csproj 两个 HintPath 是否指向实际 SDK DLL；Avalonia 是否为 11.3.2。 |
| 导入提示缺少 plugin.json | 选择 bin/Release/net8.0；检查 None Update 的复制设置。 |
| 找不到入口类型 | 清单 entryType 是否为包含 namespace 的完整类名。 |
| 插件已启用但看不到工具 | 打开独立工作区工具窗口；模块管理中检查全局和当前工作区显隐。 |
| 草稿没有恢复 | 是否点过保存；更新时是否保持相同插件 ID；是否误用了移除而不是禁用。 |
| 修改后仍是旧版本 | 重建并重新导入；如果修改宿主源码，正常退出旧单实例后运行新构建。 |
| 插件报错后自动停用 | 插件卡片显示具体错误；在 IDE 附加调试定位插件自己的代码。 |

想继续开发，可参考[工作区笔记](../../plugins/WorkspaceNotes/WorkspaceNotesPlugin.cs)的配置与工作区切换、[屏幕摘录](../../plugins/ScreenClips/ScreenClipsPlugin.cs)的稳定帧与导出、[命令看板](../../plugins/CommandWatch/CommandWatchPlugin.cs)的 Shell 事件。完整方法签名见[公开接口](../../src/TerminalHub.Extensibility/PluginApi.cs)，扩展位置与生命周期细节见[SDK 参考](../plugin-sdk.md)。

配套工程已分别使用仓库 SDK 引用与 v0.4.0 已发行 API 1 DLL 编译通过，0 警告、0 错误。实际 DLL 导入、单行粘贴不追加回车、切换目标会话及保存后重新启用恢复草稿已通过 Headless 验证，回归见 `PluginTutorialTests`。教程目录的章节跳转已在内置浏览器验证。尚未把新的教程和配套工程上传到已有 Release。
