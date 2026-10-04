# 插件生态第二轮 — 设计

目标：不写 C# 也能装插件（脚本/声明式）、改代码不用重启应用（热重载）、官方插件再补两个真正省事的。

## 1. 声明式脚本插件

`plugin.json` 不指定 `entry`、而是给出 `commands` 数组时，宿主用内置 `ManifestPlugin`（实现 `IWorkbenchPlugin`）把它当成一个正常插件启停：

```json
{
  "id": "my.deploy",
  "name": "部署脚本",
  "hostApi": 1,
  "commands": [
    { "id": "deploy", "title": "部署当前项目", "run": "pwsh -File deploy.ps1", "description": "在新会话跑 deploy.ps1" },
    { "id": "status", "title": "看 git 状态", "run": "git status" }
  ]
}
```

- `run` 在点击时展开占位符：`{cwd}` 活动会话工作目录、`{session}` 活动会话名、`{workspace}` 当前工作区名、`{dir}` 插件目录。
- 执行方式 = `Host.CreateSessionAsync`：Windows `cmd.exe /d /s /c "<run>"`，Linux `sh -c '<run>'`，cwd 用 `{cwd}` 展开前的活动会话目录。输出可见、可交互、可复跑，不引入后台隐藏进程。
- `gesture`、`description` 直通 `PluginCommand`；`submit`/`session` 预留但不实现，避免 manifest 膨胀。
- 只有 `entry` 为空且 `commands` 非空才走此路径；两种字段都填则报错（避免歧义）。脚本插件没有 DLL，`Loader` 为空，`IsScript` 标记。
- 安全边界：脚本只由用户点击/快捷键触发；占位符展开只发生在执行时；`run` 不做第二个参数化模板。

## 2. 热重载

collectible ALC 已在位（`PluginLoadContext(isCollectible: true)`），缺的是入口：

- `PluginManager.Reload(plugin)` = Disable + Enable，保留 `Enabled` 偏好；供 UI 和 watcher 共用。
- 插件管理器每张卡片加「重载」按钮（仅启用中的 DLL 插件显示；脚本插件无 DLL 不重载，重跑命令本身就是刷新）。
- 可选自动重载：`PluginSettings.AutoReload`（默认关），卡片上加「改动自动重载」勾选；勾选后对插件目录挂 `FileSystemWatcher`（`*.dll` 变更，400ms 去抖）→ 调 `Reload`。开发插件的人勾一次即可；不勾不引入监听开销和意外重载。

## 3. 新官方插件 ×2

只依赖现有 `IWorkbenchHost` API，无新宿主能力：

- **Terminal Broadcast · 输入广播**(`official.terminal-broadcast` WorkspaceTools 模块）：输入框 + 「发送到工作区全部会话」，用 `Host.Sessions` 过滤当前工作区 + `Running`，逐个 `SendInput(text, submit)`。列表显示会送达的会话，方便确认范围。不发 SSH/detached？—— 发：Detached 会话也是用户终端；SSH 远程会话同样是 SendInput 目标，语义一致（输入进 SSH 终端）。
- **Command Snippets · 命令片段**(`official.snippets`):「添加片段」存 name+command（持久化到插件配置）；列表点「应用」→ `SendInput(activeSession, cmd, submit: false)` 把文本贴进活动会话等回车（不自动执行，避免误跑）；支持删除。价值 = 把常用命令变成一键粘贴。

## 4. 验收

- `ManifestPlugin` 单测：manifest 校验（空 entry + 无 commands 报错、两字段都填报错、重复 id)、命令注册、占位符展开、`CreateSessionAsync` 收到的 shell/arguments/cwd。
- Reload:Enable→改 → Disable/Enable 路径已有测试覆盖，新增 `Reload` 等价调用测试 + watcher debounce 用注入时钟或直接调内部 `OnFileChanged`。
- 官方插件走 `Import` 现有 harness：广播对 2 个 MockSession `SendInput` 断言 PasteText；片段保存/应用/删除、配置往返。
- 手工验收照旧记 `docs/plugins/acceptance-*.md`。
