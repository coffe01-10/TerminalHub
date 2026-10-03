# 命令草稿：插件开发教程配套工程

照着[完整教程](../../../docs/plugins/development-tutorial.md)从零创建同样的插件。

仓库内构建：

```powershell
dotnet build examples/plugins/CommandDraft/CommandDraft.csproj -c Release
```

用已经解压的发行 SDK 构建（将路径替换为 SDK 的实际位置）：

```powershell
dotnet build examples/plugins/CommandDraft/CommandDraft.csproj -c Release -p:TerminalHubSdkPath="D:/TerminalHub-PluginSDK-1/sdk"
```

导入 `bin/Release/net8.0`，独立工作区工具窗口中选择「命令草稿」。编辑单行命令、点击粘贴，仅送入当前终端输入区；保存后停用并重新启用，草稿恢复。未保存的编辑只保留到本次插件停用。

示例展示公开 SDK、清单、延迟视图、命令注册、配置持久化、宿主事件和动态主题。它不读取 App 私有字段，不自行管理 PTY。

English: [Plugin development tutorial](../../../docs/plugins/development-tutorial.en.md).
