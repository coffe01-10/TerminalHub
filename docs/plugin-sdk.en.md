# Terminal Hub plugin SDK (host API 1)

**English** · [简体中文](plugin-sdk.md)

[Tutorial](plugins/development-tutorial.en.md) · [Installation and usage](plugins/README.en.md) · [Official plugins](plugins/README.en.md#official-plugins) · [Build and import](#build-and-import) · [Lifecycle](#lifecycle-and-configuration) · [Host API](#commands-events-and-host-operations)

## Build your first plugin

Follow the [step-by-step tutorial](plugins/development-tutorial.en.md) for a complete implementation and sample project. The code below illustrates the minimal API.

Development requires the .NET 8 SDK; plugin users need only the host. Clone and modify `examples/plugins/Minimal`, or download the [API 1 SDK](https://github.com/coffe01-10/TerminalHub/releases/tag/v0.4.0) and create a standalone library as shown below.

1. Create a .NET 8 library referencing Extensibility, Core, and Avalonia 11.3.2. Do not reference App.
2. Add `plugin.json` and copy it to build output. `entryType` is the fully qualified class name, including its namespace.
3. Implement `IWorkbenchPlugin` and register views/commands in `Initialize`.
4. Run `dotnet build -c Release`, then import the `bin/Release/net8.0` directory containing the manifest.
5. Tool modules appear in the independent workspace tools window by default; users can choose bottom/sidebar placement. Commands appear in the command palette.
6. Disable/re-enable to check registration cleanup and configuration restoration, then distribute the build directory. Extract ZIPs before import.

```csharp
using Avalonia.Controls;
using TerminalHub.Extensibility;

namespace MyPlugin;
public sealed class Plugin : IWorkbenchPlugin
{
    public void Initialize(IPluginContext context)
    {
        context.RegisterView(new("hello", "My tool"),
            () => new TextBlock { Text = "Hello Terminal Hub" });
        context.RegisterCommand(new("paste", "Paste sample text", () =>
        {
            if (context.Host.ActiveSessionId is { } id)
                context.Host.SendInput(id, "echo hello"); // Paste without Enter.
            return Task.CompletedTask;
        }));
    }
    public void Deactivate() { }
}
```

Use `entryType: "MyPlugin.Plugin"` in this class's manifest, and set `entry` to the generated DLL filename. Views are cached by the host; do not re-register them for every OutputBatch. This example neither creates nor terminates terminals.

The public contract is in `src/TerminalHub.Extensibility`. Plugins are independent .NET 8 / Avalonia 11.3.2 libraries and need neither App references nor their own terminal process implementation. They execute inside the host process and are intended for local extensions you write or trust. There is no isolation sandbox or remote community marketplace.

## Build and import

Three small independent examples are available:

```powershell
dotnet build examples/plugins/Minimal/Minimal.csproj -c Release
dotnet build examples/plugins/CompactSidebar/CompactSidebar.csproj -c Release
dotnet build examples/plugins/SessionPanel/SessionPanel.csproj -c Release
```

Open Plugins in the main toolbar (older builds call it Manage plugins and modules), select Import plugin, and choose the example's `bin/Release/net8.0` directory. Its root must contain `plugin.json` and the entry DLL. Import copies the build directory and enables it immediately. Open plugin directory shows the local installation location, normally `plugins` under the user configuration directory. Alternatively, place a plugin there and select Refresh, then Enable.

Enabled plugins load again at startup. Disabling removes views, commands, shortcuts, subscriptions, timers, and tracked resources registered during that activation. Removing also deletes the installation directory and saved configuration. None of these operations terminates host terminals.

Example manifest:

```json
{
  "id": "example.minimal",
  "name": "Minimal plugin",
  "version": "1.0.0",
  "entry": "TerminalHub.Example.Minimal.dll",
  "entryType": "MinimalPlugin",
  "hostApi": 1
}
```

`id` is both the installation-directory name and configuration namespace. `entry` is relative to the plugin directory. Without `entryType`, the first nonabstract type implementing `IWorkbenchPlugin` is used. API mismatch, missing DLLs, type errors, or initialization errors display the plugin name and reason, with registrations cleaned up.

For development outside this repository, reference the released SDK DLLs:

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

## Lifecycle and configuration

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

Every `Register*`, `Subscribe`, and `Schedule` returns an `IDisposable` for early disposal and is automatically tracked for the current activation. You need not individually dispose them again in `Deactivate`. Track your own watchers/resources with `context.Track(resource)` and cancel background operations with `context.Lifetime`. Dispose manually created subscriptions along with their resources. Do not create global refresh loops that cannot be disabled.

`ReadConfiguration<T>()` / `SaveConfiguration<T>(value)` stores JSON under the plugin ID in host configuration. `Directory` reads installation resources; configuration is stored separately from code. Register configuration pages on `ExtensionSurface.Settings`, displayed in the manager's plugin settings page.

Errors in registered commands, subscriptions, timers, view factories, and UI operations attributable to a plugin are reported and disable that plugin. Catch errors in async UI handlers you create and call `ReportError(ex)`. The host cannot take over every behavior of arbitrary plugin background threads.

## UI extensions

`RegisterView(ModuleDefinition, Func<Control>)` creates controls lazily and caches them. Closing the tools window, switching modules, and hiding views preserve state; disabling removes the cached control. Use dynamic `UiInk`, `UiMuted`, `UiPanel`, `UiBorder`, `UiAccent`, and related resources for DarkGlass / Black / White / Paper themes.

| Surface | Host location |
| --- | --- |
| Toolbar | Terminal toolbar, alongside existing operations |
| WorkspaceTabs | Scrollable workspace tabs area |
| Sidebar | Session sidebar |
| StatusBar | Main window status area |
| Menu | Current terminal menu |
| SidePanel | Right extension area |
| BottomPanel | Bottom extension area |
| WorkspaceTools | Independent tools window by default, with selectable bottom/sidebar placement |
| Settings | Plugin manager settings page |
| ToolWindow | Independent tool window provided by the host |

Toolbar / WorkspaceTabs / Sidebar / StatusBar can replace the default display with `Replace: true`. If several replacements exist, the last after ordering wins. Disabling restores the default. Replacement components use `context.Host` to query/activate sessions; they do not take ownership of PTYs or close them themselves. Windows/panels also use host interfaces.

`RegisterStyles(IStyle)` attaches styles to the main window and removes them on disable, supporting control templates and animation. `RegisterResources(IResourceProvider)` merges dictionaries into application resources. Prefix custom names with the plugin ID and reuse existing theme resources dynamically. Build Avalonia XAML libraries or define controls/styles in C# like the examples.

The SDK's `SvgIcon` reads monochrome path SVG and inherits the dynamic foreground. `ModuleDefinition.IconSvg` accepts SVG text or a file path in the plugin directory for tool navigation. This lightweight control expects a `viewBox` and `<path d="…">` with transforms already expanded; it does not provide full SVG support for gradients, text, or external references.

Placement and startup manages the five built-in tools and plugin components: global visibility, active-workspace enablement, order, location, startup, and shortcut entry. Older builds call it Modules. Hiding a view does not stop tasks, transfers, or recording; Disable stops plugin activity.

## Commands, events, and host operations

`RegisterCommand` adds an entry to the command palette, terminal menu, and optional `KeyGesture` shortcut. `ShowInMenu: false` hides only the menu entry. Use public host methods for input, workspace moves, and splits; the workbench retains terminal ownership.

| IWorkbenchHost | Purpose |
| --- | --- |
| Sessions / Workspaces / ActiveSessionId / ActiveWorkspaceId | Query session/workspace information |
| ReadFrame(id, historyOffset) | Stable snapshot with independent cell storage, so plugins do not alter host rendering buffers |
| CreateSessionAsync(request) / ActivateSession(id) | Create or locate a terminal; popped-out sessions activate their existing window |
| CreateWorkspaceAsync(name) / SwitchWorkspace(id) / MoveSession(id, workspaceId) | Create/switch workspaces and move the original session |
| SplitAsync(vertical, sessionId, before) / RemoveFocusedPane() | Split/remove a focused pane while preserving the original process |
| SendInput(id, text, submit: false) | Paste into a specified terminal; only explicit submit sends Enter |

Call host operations and UI registration from the UI thread. Background tasks must return through Avalonia Dispatcher. Use your own async flow and `Lifetime` for long operations.

The October 5 marketplace/docking update lets users place WorkspaceTools pages in an independent window, the original floating toolbar, or the original right sidebar, with manual/startup/restore behavior. Bottom entries open the original right content area; sidebar placement adds tabs beside Processes / Files / Logs / SSH / Commands / AI. The host moves/reuses cached views, so plugins do not need one page per location. Use responsive layouts and scrolling rather than fixed minimum widths that push tabs out. Register settings on Settings; save text configuration synchronously on Text property changes because a queued TextChanged can arrive after a page switch. Bundled resources in `official-plugins` are enabled only on requested installation.

`Subscribe` receives SessionCreated / SessionClosed / ActiveSessionChanged / WorkspaceChanged / CommandStarted / CommandCompleted / OutputBatch / LanguageChanged. Command events come from shell markers, not guessed output. Pop-out, moves, and view removal do not masquerade as process closure.

### Project-directory capabilities added on October 5

`SessionInfo.IsRemote` identifies remote sessions. `CanChangeDirectory` indicates whether the host currently permits a directory command. `SessionCwdChanged.Data` is the cwd string reported by the shell; `ProjectDirectoryChanged.Data` is the local folder selected by Project Navigator. Project selection is isolated per workspace and remains in memory.

Use the optional `context.Host as IProjectWorkbenchHost` capability:

- `SelectProjectDirectory(path)` publishes project selection.
- `SelectedProjectDirectory` queries selection for the current workspace.
- `ChangeSessionDirectory(id, path)` targets a local session and returns whether a command was sent.

Existing shell quoting is reused; PowerShell uses `Set-Location -LiteralPath` so bracketed names are not wildcards. Directory metadata, history, and breadcrumbs update only after the shell reports the new directory. Running commands, AI sessions, alternate screens, and unknown prompts prevent automatic directory commands. Offer paste-only or new-terminal actions instead.

`NewSessionRequest.Transient = true` creates a one-time session excluded from restart restoration, useful for `gh auth login`. These additions preserve original API 1 constructors and `IWorkbenchHost`; existing plugins continue to work. New capabilities require the updated host/SDK.

Git Workbench starts cancellable Git/gh child processes, passes filenames as argument-list entries, and reads output/exit codes. Its Operation output page displays results. Disable cancels only its child processes, leaving host terminals running. Authentication failures show the original error; interactive GitHub login uses a transient host terminal.

`OutputBatch.Data` is the changed `Guid[]` of sessions, coalesced every 100 ms. Read frames as needed rather than rebuilding UI per character. CommandCompleted.Data is `ShellCommandState`.

`RegisterLocalization(defaultLanguage, resources)` registers language resources. `Language` reports the current language; `Text(key, fallback)` resolves text. Module titles use module IDs as keys; commands use command IDs. LanguageChanged lets you update existing controls. Missing entries fall back to the plugin's default language, then the supplied text.

## Examples and local debugging

[CompactSidebar](../examples/plugins/CompactSidebar/README.en.md) styles tags and replaces the session shelf with a compact list. [SessionPanel](../examples/plugins/SessionPanel/README.en.md) adds a session overview, settings, a new-session command, and shortcut. They use separate IDs, surfaces, configuration, and cleanup scopes and can be loaded together and disabled separately. [Minimal](../examples/plugins/Minimal/README.en.md) is the smallest entry project. [CommandDraft](../examples/plugins/CommandDraft/README.en.md) accompanies the full tutorial.

Rebuild, disable the old plugin, and import the new directory, or replace installed files and re-enable. Managed DLLs load from streams for easier replacement. Native dependencies may keep files open; update at the next launch if needed. Attach an IDE to the TerminalHub process to debug.

The original three examples were built and enabled together, then disabled separately, in a real Windows Avalonia window. Headless regressions also covered configuration, re-enable, language, shortcuts, workspace visibility, and error cleanup. See the [2026-10-03 acceptance record (Chinese)](acceptance-v0.4.0-2026-10-03.md). That round did not verify actual system IME candidate windows, multiple displays, or physical Linux use.
