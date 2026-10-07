# 插件开发与使用

[English](README.en.md) · **简体中文**

[安装](#安装官方插件) · [官方插件](#官方插件) · [插件开发教程](development-tutorial.md) · [API 参考](../plugin-sdk.md#命令事件和宿主操作) · [源码](../../plugins) · [离线页面](index.html)

开发教程提供[中文](development-tutorial.md)和[English](development-tutorial.en.md)，离线页面也可切换；「开发文档」入口随应用当前中英文界面打开对应版本。

Terminal Hub 插件是运行在原生应用内的 .NET 8 / Avalonia 11 类库。需要 Terminal Hub v0.4.0 或更新版本，当前宿主 API 为 1。官方插件与第三方插件使用同一套公开接口，没有依赖应用私有代码。

新版插件管理窗口提供「开发文档 ↗」和「官方插件 ↗」入口，打开随应用附带的离线页面，直接跳转到相应章节。源码和 API 参考外链需要联网。旧版 v0.4.0 没有这两个新按钮，但可以按下方步骤导入插件。

## 安装官方插件

新版主程序顶部打开「插件」进入市场，找到需要的官方扩展，点击「安装」，再点击「打开」。九个安装资源随程序附带，无需联网。内置基础工具已经可用，可以调整显示范围。详细说明见[插件市场、位置与启动设置](marketplace.md)。

源码开发、手动更新或使用旧版导入功能时，在仓库根目录执行：

```powershell
pwsh -File scripts/build-official-plugins.ps1
```

生成 `artifacts/official-plugins` 下九个可导入目录和各自的 ZIP：WorkspaceNotes、ScreenClips、CommandWatch、TerminalBroadcast、Snippets、ProjectNavigator、GitWorkbench、PortGuard、TaskRunner。ZIP 需先解压，插件管理器当前接受目录，不直接接受 ZIP。

项目导航和 Git 工作台需要本轮更新后的 Terminal Hub 主程序；此前的 API 1 主程序需要更新。现有五个插件继续兼容新版宿主。

工具可选独立窗口、现有底部工具栏入口、现有右侧面板标签。底部入口沿用监控／SSH／日志按钮的交互，在右侧打开插件页面。项目导航和 Git 工作台提供各自的设置页。

默认从独立窗口打开。已安装插件卡片的「打开位置」下拉框可直接切到原底部工具栏或右侧标签，立即生效；启动方式与隐藏快捷入口在「设置」中调整。

1. 主窗口顶部打开「插件」，点击「导入插件」。
2. 选择包含 `plugin.json` 的目录，例如 `artifacts/official-plugins/WorkspaceNotes`，导入后立即启用。
3. 打开独立「工作区工具」窗口，在左侧导航选择新模块。
4. 在「位置与启动」中调整位置、启动方式、快捷入口、排序和当前工作区范围。旧版界面为「模块管理」。

也可单独执行 `dotnet build plugins/WorkspaceNotes/WorkspaceNotes.csproj -c Release`，导入其 `bin/Release/net8.0` 目录。

更新时重新导入同 ID 的新构建目录。禁用只清理插件界面和订阅，不关闭终端；重新启用笔记恢复内容，看板和摘录从空状态开始。移除会删除安装目录和保存的插件配置，笔记也随之删除。

## 官方插件

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

### 输入广播与命令片段

TerminalBroadcast 向当前工作区所选范围发送输入；Snippets 保存常用命令，点击只粘贴到活动会话，不自动回车。配置保存在各自的插件命名空间。

### 项目导航

ID：`official.project-navigator` · [源码](../../plugins/ProjectNavigator/ProjectNavigatorPlugin.cs)

浏览本地目录、面包屑和返回历史；收藏项目支持名称、分组、排序、搜索与 JSON 导入导出，同时保留最近访问。目录浏览只改变插件的浏览位置；「终端切到此处」才发送切换命令，「在此新建终端」保留原会话。未知 Shell 状态可用「粘贴目录命令」，手动回车执行。

收藏与最近访问在本机共享，浏览位置和项目选择按工作区记录；跟随当前终端默认开启。SSH 会话不会把远端目录当成本地目录。打开文件夹使用系统文件管理器。详见[使用与验证说明](project-git-workbench.md)。

### Git 工作台与 GitHub

ID：`official.git-workbench` · [源码](../../plugins/GitWorkbench/GitWorkbenchPlugin.cs)

使用系统 Git，支持改动及差异、逐文件／全部暂存与取消暂存、提交、提交并推送、Fetch／Pull／Push、分支创建与切换、提交历史（默认 20 条，可设置）。远端与目标分支在「分支」页选择；操作输出保留命令、目录、退出码、stdout 与 stderr。冲突文件可打开处理，提交成功而推送失败时可单独重试推送。

GitHub 页使用系统 `gh` 及其已有登录，提供仓库主页、当前分支 PR、PR／Issue 列表与详情、检查状态、创建 PR 草稿、从 Issue 创建关联分支。登录按钮打开一次性终端。没有 GitHub 登录时，本地 Git 功能可继续使用。详见[使用与验证说明](project-git-workbench.md)。

### 端口看板

ID：`official.port-guard` · [源码](../../plugins/PortGuard/PortGuardPlugin.cs)

查看本机正在监听的 TCP 端口和 UDP 绑定。TCP 只保留状态为 `LISTENING`/`LISTEN` 的行，UDP 只保留对端为 `*:*` 的绑定（临时 UDP 会话不算监听，避免误杀）。IPv6 按方括号取端口。Windows 读取 `netstat -ano`，再用 `tasklist` 补进程名；Linux 依次尝试 `ss -tulnp` 和 `lsof -i -P -n`。解析按列位置而不是表头文本：`netstat`/`tasklist` 在中文 Windows 上的表头是中文，输出按 OEM 代码页（本机 936）解码而非 UTF-8；`ss` 的状态列是按 token 找的，`ss -o` 省略 Recv-Q/Send-Q 后仍能识别 LISTEN。可按端口、进程名或 PID 过滤，也可输入端口号查看占用。

结束进程前弹出确认框，只有点「结束」才算确认 —— 标题栏 X、取消、Esc 都视为取消，不会启动 `taskkill`/`kill`。只接受快照里出现的整数 PID，拒绝 0、4、当前进程和手填的其他值；`taskkill /F`/`kill` 结束的是整个进程，而不只是这一个端口，确认文案里写明这一点以及快照到点击之间 PID 可能被复用。诊断只在新终端里跑 `netstat`/`ss`/`lsof`/`tasklist` 展示输出，不修改系统。

### 任务面板

ID：`official.task-runner` · [源码](../../plugins/TaskRunner/TaskRunnerPlugin.cs)

从项目目录读取 `package.json` 的 scripts、`Makefile` 目标、`justfile` 配方和 `.vscode/tasks.json`。「跟随项目／活动终端」开关默认开启：依次取 `IProjectWorkbenchHost.SelectedProjectDirectory`（项目导航所选）和活动终端的工作目录，切换工作区时重新解析，空工作区会清空目录而不是保留上一个项目。关掉开关后「选择目录」把所选目录按工作区固定保存，切回该工作区仍用它。目录、粘贴方式和跟随开关都保存在本机插件配置。

点击「运行」时，勾选「粘贴到活动会话」则把命令写入活动会话且不按回车；取消勾选则新建一次性会话执行（Windows `cmd /d /s /c`、其他 `sh -c`），命令原样下发、不包外层引号 —— `cmd /s` 会剥掉整串首尾引号，若再包一层会让 `npm run "test app"` 这类带引号的命令失衡。`package.json` 的 scripts 生成 `npm run <名称>`，Makefile 生成 `make <目标>`，justfile 生成 `just <配方>`，不执行脚本正文；tasks.json 只收 `type=shell` 且 `command` 为字符串的任务，展开 `${workspaceFolder}` 并把相对 `options.cwd` 拼到项目目录。没有这四个文件或没有可运行任务时，页面说明原因。

## 开发自己的插件

从[完整插件开发教程](development-tutorial.md)开始，配套可运行工程是[命令草稿示例](../../examples/plugins/CommandDraft)。[SDK 参考](../plugin-sdk.md)提供 API 说明。官方项目只引用 `TerminalHub.Extensibility`，共享的[界面辅助代码](../../plugins/Shared/PluginUi.cs)编入各自 DLL，无额外共享 DLL 安装要求。

从一个工具模块开始：注册延迟创建的视图、一个命令、按插件 ID 保存配置。使用动态主题资源。视图会缓存，关闭工具窗口不等于停用。订阅和计时器用 `context.Subscribe` / `Schedule`，宿主停用时清理。异步按钮捕获错误调用 `ReportError`，文件选择完成后检查 `Lifetime`，跨线程先回到 Avalonia UI 线程。

`Host.SendInput(id, text)` 默认只粘贴，显式 `submit: true` 才发回车。查看、复制、切换模块不应触发执行。回归应证明工作区切换不串数据、禁用不结束会话、配置可恢复、宽字符不重复、未知命令状态不被误报。

## 构建与分发

打包脚本只复制插件 DLL、依赖描述、清单和说明，不扫描用户配置目录。九个官方插件无需另装第三方 DLL，宿主提供 SDK、Core 和 Avalonia；Git 工作台另需系统 Git，GitHub 功能另需 gh。端口看板使用系统自带的 netstat、ss 或 lsof。新插件如有额外依赖，把相关 DLL 与资源放入插件目录，并测试在干净目录导入。

本轮以源码与本地构建包交付，尚未上传发行附件。项目导航和 Git 工作台的验收记录见[使用与验证说明](project-git-workbench.md)。

最初三个官方插件的具体场景和未执行范围见 [2026-10-03 验收记录](acceptance-2026-10-03.md)。

## 维护英文离线手册

英文离线手册从 `README.en.md` 生成，复用本页对应中文 HTML 的样式。修改英文 Markdown 后，在仓库根目录运行：

```powershell
python -m pip install -r docs/plugins/requirements.txt
python -B docs/plugins/generate_manual.py
```

该 Python 依赖只用于生成文档，应用构建和阅读手册不需要它。现有开发教程仍同步维护 Markdown 与 HTML。
