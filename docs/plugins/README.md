# 插件开发与使用

[安装](#安装官方插件) · [官方插件](#三个官方插件) · [插件开发教程](development-tutorial.md) · [API 参考](../plugin-sdk.md#命令事件和宿主操作) · [源码](../../plugins) · [离线页面](index.html)

开发教程提供[中文](development-tutorial.md)和[English](development-tutorial.en.md)，离线页面也可切换；「开发文档」入口随应用当前中英文界面打开对应版本。

Terminal Hub 插件是运行在原生应用内的 .NET 8 / Avalonia 11 类库。需要 Terminal Hub v0.4.0 或更新版本，当前宿主 API 为 1。官方插件与第三方插件使用同一套公开接口，没有依赖应用私有代码。

新版插件管理窗口提供「开发文档 ↗」和「官方插件 ↗」入口，打开随应用附带的离线页面，直接跳转到相应章节。源码和 API 参考外链需要联网。旧版 v0.4.0 没有这两个新按钮，但可以按下方步骤导入插件。

## 安装官方插件

在仓库根目录执行：

```powershell
pwsh -File scripts/build-official-plugins.ps1
```

生成 `artifacts/official-plugins/WorkspaceNotes`、`ScreenClips`、`CommandWatch` 三个可导入目录和各自的 ZIP。ZIP 需先解压，插件管理器当前接受目录，不直接接受 ZIP。

1. 主窗口顶部打开「插件」，点击「导入插件」。
2. 选择包含 `plugin.json` 的目录，例如 `artifacts/official-plugins/WorkspaceNotes`，导入后立即启用。
3. 打开独立「工作区工具」窗口，在左侧导航选择新模块。
4. 在「模块管理」中调整显隐、排序和是否显示在当前工作区。

也可单独执行 `dotnet build plugins/WorkspaceNotes/WorkspaceNotes.csproj -c Release`，导入其 `bin/Release/net8.0` 目录。

更新时重新导入同 ID 的新构建目录。禁用只清理插件界面和订阅，不关闭终端；重新启用笔记恢复内容，看板和摘录从空状态开始。移除会删除安装目录和保存的插件配置，笔记也随之删除。

## 三个官方插件

### 工作区笔记

ID：`official.workspace-notes` · [源码](../../plugins/WorkspaceNotes/WorkspaceNotesPlugin.cs)

填写交接信息、项目待办、排查结论。顶部显示当前工作区名称，切换工作区显示各自的笔记。每秒检查并保存待保存内容；「保存笔记」、切换工作区、停用插件时也会保存。关闭工具窗口保留编辑。

笔记写入本机用户配置的插件命名空间。语言切换只改变界面文字，不翻译笔记。没有 Markdown 渲染、同步或上传。

### 屏幕摘录

ID：`official.screen-clips` · [源码](../../plugins/ScreenClips/ScreenClipsPlugin.cs)

切到目标终端，点击「摘录当前屏幕」，或从命令面板执行同名命令。每份保留会话名、时间和纯文本；列表切换查看，支持复制和导出 UTF-8 `.txt`。

抓取当前屏幕缓冲整页，从第一个屏幕行开始，不包含主视图正在浏览的历史位置。每个物理屏幕行保留一个换行，去掉行末空格和末尾空行，保留中文、组合字符和 emoji。API 1 未提供软换行标记，不能重建原始逻辑行。不抓取历史全文、选区、颜色或 ANSI 序列。

最近 20 份只保存在内存；清空、停用或退出应用后消失。导出前可检查文本，终端内容可能包含项目私密信息。复制/导出只由用户点击触发。

### 命令看板

ID：`official.command-watch` · [源码](../../plugins/CommandWatch/CommandWatchPlugin.cs)

多个终端跑构建、测试或服务时，显示各自最近一次被观察到的命令状态、耗时和退出码。默认仅当前工作区，取消勾选可看所有工作区，筛选选项保存在本机。点击卡片激活对应终端，弹出会话则激活其窗口。

依赖 Shell 集成开始/完成事件，无标记显示「尚未收到命令标记」。退出码 0 显示完成，非 0 显示失败，缺失退出码明确显示未知。不会推断 AI 完成状态，不发命令，不保存输出。启用前的命令不补录，停用后清空状态。某些 Shell 或 TUI 不发集成标记时，没有命令状态可显示。

## 开发自己的插件

从[完整插件开发教程](development-tutorial.md)开始，配套可运行工程是[命令草稿示例](../../examples/plugins/CommandDraft)。[SDK 参考](../plugin-sdk.md)提供 API 说明。官方项目只引用 `TerminalHub.Extensibility`，共享的[界面辅助代码](../../plugins/Shared/PluginUi.cs)编入各自 DLL，无额外共享 DLL 安装要求。

从一个工具模块开始：注册延迟创建的视图、一个命令、按插件 ID 保存配置。使用动态主题资源。视图会缓存，关闭工具窗口不等于停用。订阅和计时器用 `context.Subscribe` / `Schedule`，宿主停用时清理。异步按钮捕获错误调用 `ReportError`，文件选择完成后检查 `Lifetime`，跨线程先回到 Avalonia UI 线程。

`Host.SendInput(id, text)` 默认只粘贴，显式 `submit: true` 才发回车。查看、复制、切换模块不应触发执行。回归应证明工作区切换不串数据、禁用不结束会话、配置可恢复、宽字符不重复、未知命令状态不被误报。

## 构建与分发

打包脚本只复制插件 DLL、依赖描述、清单和说明，不扫描用户配置目录。三个插件无额外第三方依赖，宿主提供 SDK、Core 和 Avalonia。新插件如有额外依赖，把相关 DLL 与资源放入插件目录，并测试在干净目录导入。

本轮以源码与本地构建包交付，尚未上传到已有 v0.4.0 Release。

具体场景和未执行范围见[本轮验收记录](acceptance-2026-10-03.md)。
