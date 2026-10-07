# Minimal plugin

**English** · [简体中文](README.md) · [Plugin SDK](../../../docs/plugin-sdk.en.md)

Build from the repository root:

```powershell
dotnet build examples/plugins/Minimal/Minimal.csproj -c Release
```

In Plugins → Import plugin, select `examples/plugins/Minimal/bin/Release/net8.0`. Import immediately enables it; disable/remove it from the same page. The project uses only the public SDK. For independent development, replace ProjectReference entries with references to built SDK DLLs.
