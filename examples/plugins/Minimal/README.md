# 最小插件

从仓库根目录构建：

```powershell
dotnet build examples/plugins/Minimal/Minimal.csproj -c Release
```

在应用的“插件 → 导入插件”选择 `examples/plugins/Minimal/bin/Release/net8.0`。插件会立即启用；同页可禁用或移除。源工程只引用公开 SDK，可把 ProjectReference 改成已构建 SDK DLL 的 Reference。
