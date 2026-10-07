# Command Draft: tutorial sample project

**English** · [简体中文](README.md) · [Complete tutorial](../../../docs/plugins/development-tutorial.en.md)

Follow the tutorial to build this plugin from scratch.

Build inside the repository:

```powershell
dotnet build examples/plugins/CommandDraft/CommandDraft.csproj -c Release
```

Build against an extracted release SDK (replace the path with its actual location):

```powershell
dotnet build examples/plugins/CommandDraft/CommandDraft.csproj -c Release -p:TerminalHubSdkPath="D:/TerminalHub-PluginSDK-1/sdk"
```

Import `bin/Release/net8.0`, then choose Command Draft in the independent tools window. Edit a single-line command and select Paste: it goes only to the active terminal's input without Enter. Save, disable, and re-enable to restore the saved draft. Unsaved edits last only until the current plugin activation ends.

The sample demonstrates the public SDK, manifest, lazy views, commands, persistent configuration, host events, and dynamic themes. It does not read private App fields or manage its own PTY. The bundled sample's original display strings are Chinese; the English tutorial includes an English implementation.
