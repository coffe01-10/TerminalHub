# Terminal Hub Plugin Development Tutorial: Build a Command Draft Tool

[中文](development-tutorial.md) · English

This tutorial is for plugin developers. You will build a .NET plugin that loads into Terminal Hub, displays the active terminal, lets you edit and paste a command draft, saves configuration, and updates its target when you switch sessions. The later sections explain settings pages, localization, and background work.

[Requirements](#prepare) · [Project](#project) · [Manifest](#manifest) · [Implementation](#implementation) · [Code walkthrough](#explain) · [Build and import](#run) · [Extensions](#extend) · [Packaging and debugging](#distribute) · [Troubleshooting](#troubleshooting)

[Complete sample project](../../examples/plugins/CommandDraft) · [SDK reference, Chinese](../plugin-sdk.md) · [Official plugin source](../../plugins)

<a id="prepare"></a>

## 1. Prepare your environment

You need the .NET 8 SDK, a code editor, and Terminal Hub v0.4.0 or later. Use Avalonia 11.3.2 and host API 1. A plugin is a class library loaded by Terminal Hub; it does not run independently with `dotnet run`.

Run `dotnet --list-sdks` and check that an `8.0.xxx` SDK is installed. Download `TerminalHub-PluginSDK-1.zip` from the [v0.4.0 release](https://github.com/coffe01-10/TerminalHub/releases/tag/v0.4.0) and extract it. Its `sdk/` directory contains `TerminalHub.Extensibility.dll` and `TerminalHub.Core.dll`.

If you have cloned the repository, you can build the accompanying sample directly:

```powershell
dotnet build examples/plugins/CommandDraft/CommandDraft.csproj -c Release
```

The following steps use the standalone SDK. You do not need to reference or build the App project. The repository sample uses Chinese display labels; the complete code on this page uses English labels with the same IDs and behavior.

<a id="project"></a>

## 2. Create a project and reference the SDK

Run these commands in your development directory:

```powershell
dotnet new classlib -n CommandDraft -f net8.0
cd CommandDraft
```

Delete the generated `Class1.cs`. Create a `sdk` directory and copy the two DLLs listed above into it. Replace `CommandDraft.csproj` with:

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

`Avalonia` provides the native controls. `Extensibility` defines the plugin interfaces; `Core` supplies types used by those interfaces, including terminal frames and command state. `Private=false` prevents the two host-provided SDK DLLs from being copied into your plugin output. `AssemblyName` determines the generated DLL filename.

Your project should now look like this:

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

## 3. Write the plugin manifest

Create `plugin.json` at the project root:

```json
{
  "id": "tutorial.command-draft",
  "name": "Command Draft (tutorial sample)",
  "version": "1.0.0",
  "entry": "TerminalHub.Example.CommandDraft.dll",
  "entryType": "TerminalHub.Example.CommandDraft.CommandDraftPlugin",
  "hostApi": 1
}
```

| Field | Meaning in this example |
| --- | --- |
| `id` | Unique plugin ID, also used as its installation directory name and configuration namespace. Use your own ID when publishing your plugin. |
| `name` | Name shown in the plugin manager. |
| `version` | Your plugin's version, independent of the host version. |
| `entry` | Entry DLL inside the plugin directory; it must match the project's `AssemblyName`. |
| `entryType` | Fully qualified class name implementing `IWorkbenchPlugin`, including its namespace. |
| `hostApi` | This tutorial targets host API 1. |

The project's `CopyToOutputDirectory` setting places this manifest next to the DLL. Without it, compilation may succeed while importing the output directory fails because the manifest is missing.

<a id="implementation"></a>

## 4. Implement the complete plugin

Create `CommandDraftPlugin.cs` and copy all of the following code. It constructs controls in C#, so no separate XAML file is needed.

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

        context.RegisterView(new("draft", "Command draft", ExtensionSurface.WorkspaceTools), CreateView);
        context.RegisterCommand(new("paste-draft", "Paste command draft", () =>
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
        var title = new TextBlock { Text = "Command draft", FontSize = 22 };
        title.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("UiInk"));
        panel.Children.Add(title);

        _sessionLabel = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        _sessionLabel.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("UiMuted"));
        panel.Children.Add(_sessionLabel);

        _editor = new TextBox { Text = _options.Draft, Watermark = "For example: echo hello" };
        _editor.Bind(TextBox.ForegroundProperty, new DynamicResourceExtension("UiInk"));
        _editor.Bind(TextBox.BackgroundProperty, new DynamicResourceExtension("UiInset"));
        _editor.Bind(TextBox.BorderBrushProperty, new DynamicResourceExtension("UiBorder"));
        panel.Children.Add(_editor);

        var actions = new WrapPanel();
        _pasteButton = ActionButton("Paste into active terminal", PasteDraft);
        _saveButton = ActionButton("Save draft", SaveDraft);
        actions.Children.Add(_pasteButton); actions.Children.Add(_saveButton);
        panel.Children.Add(actions);

        var hint = new TextBlock { Text = "Pasting does not send Enter. Save the draft to restore it after re-enabling the plugin.", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
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
            _sessionLabel.Text = session is null ? "No active session" : $"Target: {session.Name}\n{session.WorkingDirectory}";
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

## 5. Walk through the code

### Initialization and view creation

When enabled, the host creates a `CommandDraftPlugin` instance and calls `Initialize(context)`. The plugin reads its configuration and registers a module, a command, and an event subscription.

`RegisterView(..., CreateView)` receives a factory. The controls are created when the user selects the module. `WorkspaceTools` places it in the independent workspace tools window's navigation. The host caches the view, so closing and reopening that window preserves the draft.

`Deactivate()` runs when the plugin is disabled. Closing the tools window does not deactivate it. The host removes registrations from that activation, so this sample does not dispose them individually. This sample restores drafts saved with **Save draft**; unsaved edits last only until the plugin is disabled.

### The button and command palette share an action

`RegisterCommand` adds **Paste command draft** to the command palette and terminal menu. Both the button and command call `PasteDraft()`, using the same target and input logic. The command can paste a saved draft even before you open the tools page.

`ActiveSessionId` is queried when the action runs, so input follows the currently active session. If there is no running active session, the button is disabled and the command returns. `SendInput(..., submit: false)` does not append Enter. The sample uses a single-line editor; the user can inspect the pasted command and press Enter in the terminal.

`submit: false` does not make arbitrary multiline text safe from execution. If you change the editor to accept multiple lines, newline characters in the text may submit commands to the shell. Design and explain that interaction separately.

### Configuration belongs to the host

`Options` is a JSON-serializable configuration type. `ReadConfiguration<Options>()` restores it during initialization; `SaveConfiguration(_options)` writes it when the user saves. Configuration is isolated by plugin ID. You do not need to locate settings.json yourself or write user configuration into the plugin's installation directory.

New configuration fields can have defaults so older saved values still load. Keep the same plugin ID during upgrades to retain access to existing configuration; changing the ID creates a separate plugin.

### Subscribe to relevant events

The subscription updates the target label when sessions or workspaces change, or when a session is created or closed. These host events avoid the need to poll.

This example does not display terminal output, so it does not subscribe to output events. For an output view, subscribe to `OutputBatch`, read changed session IDs from `e.Data is Guid[] ids`, and request `Host.ReadFrame(id)` only when needed. Avoid rebuilding the whole UI for every output character.

### Follow the host theme

`DynamicResourceExtension("UiInk")` and the other dynamic resources resolve again when the theme changes. Text, editors, and buttons use the host palette, while the tools window supplies their font and layout context.

`UiInk` is the main text color, `UiMuted` is secondary text, `UiInset` is an editor background, `UiRaised` is a button background, and `UiBorder` is the border color. An accent button can use `UiAccent` and `UiOnAccent`. Prefix your own resource keys with the plugin ID to avoid overriding another module's resources.

<a id="run"></a>

## 6. Build, import, and run

From the project root, run:

```powershell
dotnet build -c Release
```

The output should include:

```text
bin/Release/net8.0/
  plugin.json
  TerminalHub.Example.CommandDraft.dll
  TerminalHub.Example.CommandDraft.deps.json
```

In Terminal Hub, open **Plugins → Import plugin** and select this `net8.0` directory. Import copies the files and enables the plugin. Open **Workspace tools** and select **Command draft** in the navigation.

You should see a heading, the active session name and directory, a draft editor, and two buttons:

1. Enter `echo tutorial` and click **Paste into active terminal**. The text appears in the terminal input area; press Enter there to execute it.
2. Switch to another terminal. The target label updates, and the next paste goes to that session.
3. Click **Save draft**, disable the plugin, and enable it again. The editor restores `echo tutorial`.
4. Exit and reopen Terminal Hub normally. Enabled plugins load again, and the saved draft remains available.

If your host UI is Chinese, the import controls are 「插件」→「导入插件」 and the tools window is 「工作区工具」.

<a id="extend"></a>

## 7. Extend your plugin

### Add a settings page

Register a Settings module inside `Initialize`. This example reuses `SaveDraft` to demonstrate where the page appears; actual settings can use CheckBox, ComboBox, or other controls.

```csharp
context.RegisterView(
    new("settings", "Command draft settings", ExtensionSurface.Settings),
    () => ActionButton("Save current draft", SaveDraft));
```

The module appears under **Plugin settings** in the plugin manager. Create separate controls for the tools and settings pages: a Button cannot belong to two parent containers at once.

### Add a keyboard shortcut

Replace the original `RegisterCommand` call with one that supplies a gesture:

```csharp
context.RegisterCommand(new("paste-draft", "Paste command draft", () =>
{
    PasteDraft();
    return Task.CompletedTask;
}, Gesture: "Ctrl+Shift+F9"));
```

Do not register a second command with the same ID. Check the host's common shortcuts and enabled plugins when choosing a gesture, so the key does not trigger another action.

### Add English and Chinese localization

Register language resources before the views and commands:

```csharp
context.RegisterLocalization("en",
    new Dictionary<string, IReadOnlyDictionary<string, string>>
    {
        ["en"] = new Dictionary<string, string>
        {
            ["draft"] = "Command draft",
            ["paste-draft"] = "Paste command draft",
            ["paste-button"] = "Paste into active terminal"
        },
        ["zh-CN"] = new Dictionary<string, string>
        {
            ["draft"] = "命令草稿",
            ["paste-draft"] = "粘贴命令草稿",
            ["paste-button"] = "粘贴到活动终端"
        }
    });
```

The host reads titles using the module ID `draft` and command ID `paste-draft`. Your existing custom controls must also update. Create the paste button with `context.Text("paste-button", "Paste into active terminal")`, then handle language changes in the existing subscription:

```csharp
if (e.Kind == WorkbenchEventKind.LanguageChanged && _pasteButton is not null)
    _pasteButton.Content = context.Text("paste-button", "Paste into active terminal");
```

For a fully bilingual page, add keys for its heading, save button, hints, and empty states. Translate your UI messages; retain user drafts, session names, commands, and paths as entered.

### Create a terminal or adjust the layout

Use the public Host interface for terminal operations:

```csharp
var id = await context.Host.CreateSessionAsync(
    new NewSessionRequest(Name: "Build", WorkingDirectory: "/your/project"));
if (id is { } sessionId)
    context.Host.ActivateSession(sessionId);
```

Replace the directory with a real path on your system, such as `D:/Projects/MyApp` on Windows. `SplitAsync(vertical: false, sessionId: id)` splits the focused pane left/right; `vertical: true` splits top/bottom. Removing a pane view retains its session process.

### Add timers and asynchronous work

Use a host-managed timer for periodic updates:

```csharp
context.Schedule(TimeSpan.FromSeconds(1), RefreshSession);
```

It runs on the UI thread and stops when the plugin is disabled. If host events already provide the updates you need, a timer is unnecessary. Use async for slow work rather than blocking the UI inside the timer callback.

For example, register an asynchronous command that reads a bundled resource:

```csharp
context.RegisterCommand(new("load-draft", "Load preset draft", async () =>
{
    var path = Path.Combine(context.Directory, "default-command.txt");
    var text = await File.ReadAllTextAsync(path, context.Lifetime);
    context.Lifetime.ThrowIfCancellationRequested();
    _options = new(text.TrimEnd('\r', '\n'));
    if (_editor is not null) _editor.Text = _options.Draft;
}));
```

This command starts on the UI thread and resumes on its UI context after await. Add `default-command.txt` to the project with `CopyToOutputDirectory` so it ships with the plugin. Its content should be a single line for this editor. Registered command exceptions are handled by the host. For your own async Click handlers, catch exceptions and call `ReportError(ex)`, treating lifetime cancellation separately. Code running on a background thread must use `Avalonia.Threading.Dispatcher.UIThread` before changing controls or invoking Host operations.

<a id="distribute"></a>

## 8. Package, update, and debug

A plugin is distributed as a directory containing its manifest, entry DLL, `.deps.json`, and any additional dependencies or resources. The host provides Extensibility, Core, and Avalonia. Include your own extra third-party libraries with the plugin.

For this example without extra resources or dependencies, run the following from the project root:

```powershell
Compress-Archive -Path bin/Release/net8.0/plugin.json,bin/Release/net8.0/TerminalHub.Example.CommandDraft.dll,bin/Release/net8.0/TerminalHub.Example.CommandDraft.deps.json -DestinationPath CommandDraft-1.0.0.zip -Force
```

Add any resources or extra dependencies to that file list as you extend the plugin. Users extract the ZIP and import its directory; the plugin manager does not import ZIP files directly. Do not include user settings.json or SSH records.

After editing the code, rebuild and import the new output directory. Keep the same ID and increment the plugin version to update it while retaining saved configuration. Managed DLLs are loaded from streams and can be replaced. Native libraries may still hold files open, requiring a normal host exit before replacement.

In Visual Studio or Rider, attach to the running `TerminalHub` process and place breakpoints in `Initialize`, `CreateView`, or `PasteDraft`. Keep the Release PDB in your debugging directory. Reimport creates a new plugin instance; editing source files alone does not replace a loaded DLL.

<a id="troubleshooting"></a>

## 9. Troubleshooting and next steps

| Symptom | Where to look |
| --- | --- |
| IPluginContext or TerminalFrame is missing at compilation | Check both SDK HintPath values and the Avalonia 11.3.2 package reference. |
| Import cannot find plugin.json | Select bin/Release/net8.0 and check the manifest's None Update copy setting. |
| Entry type cannot be found | Use the complete class name, including its namespace, in entryType. |
| Plugin is enabled but its tool is missing | Open Workspace tools and check global/current-workspace visibility in module management. |
| Draft is not restored | Save it first, keep the same plugin ID on update, and use Disable rather than Remove. |
| An old version still appears | Rebuild and reimport. After modifying the host itself, exit the previous single instance normally before launching the new build. |
| Plugin reports an error and disables itself | Read the error on its plugin card and attach a debugger to inspect the plugin code. |

For further examples, see [Workspace Notes](../../plugins/WorkspaceNotes/WorkspaceNotesPlugin.cs) for workspace-scoped configuration, [Screen Clips](../../plugins/ScreenClips/ScreenClipsPlugin.cs) for stable frames and export, and [Command Watch](../../plugins/CommandWatch/CommandWatchPlugin.cs) for shell events. Full signatures are in the [public interfaces](../../src/TerminalHub.Extensibility/PluginApi.cs). The [SDK reference, Chinese](../plugin-sdk.md) describes extension surfaces and lifecycle details.

The accompanying sample has been built against the repository SDK and the released v0.4.0 API 1 DLLs. Its DLL import, single-line paste without an appended Enter, session targeting, and saved draft restoration have passed Headless verification in `PluginTutorialTests`. This English page translates its display strings; the complete English implementation above has also been compiled against the released SDK with no warnings or errors. The tutorial and sample are in the repository; they have not been added to the existing Release attachments.
