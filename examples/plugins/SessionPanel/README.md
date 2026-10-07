# 会话面板

[English](README.en.md) · **简体中文**

从仓库根目录构建：

```powershell
dotnet build examples/plugins/SessionPanel/SessionPanel.csproj -c Release
```

在应用的“插件 → 导入插件”选择 `examples/plugins/SessionPanel/bin/Release/net8.0`。插件会立即启用；同页可禁用或移除。源工程只引用公开 SDK，可把 ProjectReference 改成已构建 SDK DLL 的 Reference。
