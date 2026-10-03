# v0.4.0 工作台验收记录

2026-10-03，用户追加“验收还是要的”后执行。本轮验收覆盖全部七项工作台功能的相关场景，保留已有未提交修改，未替换用户安装版或结束已有终端。

> 后续用户要求恢复独立工具窗口并优化 UI，当前显示方式与新增验收见 [界面精修记录](ui-refinement-2026-10-03.md)。以下保留此前阶段的验收记录。

## 验收结果

| 范围 | 实际场景与结果 | 证据层次 |
| --- | --- | --- |
| 跨工作区拖动 | 固定/自动隐藏侧栏，悬停切换、取消、目标卡片前插入，撤销/重做保留同一 PTY；重启恢复归属与顺序 | 原生控件事件 Headless；迁移后真实 ConPTY 继续读写 |
| 任意分屏 | 六个嵌套窗格、边缘拖放、中央交换、分隔条拖动、内层比例、最大化/恢复、移除视图与撤销、序列化和工作区往返；新建窗格名称绑定更新 | Headless；真实 Windows 窗口与六个 ConPTY |
| MRU | 按键释放确认、Esc 取消、反向切换、自定义快捷键、跨工作区和独立窗口；预览不发送输入 | Headless 输入事件 |
| 插件 | 三个独立 DLL 同时启用；侧栏替换、样式、会话概览、设置保存、语言切换、新终端快捷键、工作区显隐、分别停用、重新启用 | Headless；真实 Windows 窗口加载 Release DLL |
| 插件生命周期 | 停用清理定时器、事件订阅、视图、命令、样式、资源；命令/初始化异常清理并能再次启用 | 实际 collectible DLL 加载回归 |
| 中英文 | 打开的菜单、工具、弹出窗口和设置即时更新；四种主题下英文导航可见；终端输出、路径与用户名称保持原文 | Headless |
| 布局历史 | 比例拖动为一次操作、交换/分屏/跨工作区迁移撤销与重做；关闭进程后清除历史，撤销移除视图保留进程 | Headless；真实 Windows 窗口 |
| 嵌入工具 | 五页均显示，切页、收起再打开及切换工作区后保留规则/任务编辑，组件卡片样式保留 | Headless；真实 Windows 窗口 |
| 光标/输入法坐标 | 分屏重排、选区、弹出回接、主题与工作区回归；PowerShell 7.6.6 中文左右移动、多行粘贴、40 列缩放与当前帧坐标 | Headless；真实 ConPTY 输出，非系统候选窗 |

最终各场景共 88 个相关回归通过：综合运行中的 87 个功能回归通过，剩余 PowerShell 编辑用例修正提示符判定后单独通过。综合结果文件保留了修正前的那一次失败，`shell-editing.trx` 记录最终通过结果；没有把失败或未执行场景描述为通过。

真实桌面入口在 `tools/TerminalHub.DesktopMeasurements/WorkbenchAcceptance.cs`。本机 Windows、单显示器、125% 缩放，11 个桌面场景通过，报告 `artifacts/v0.4.0/acceptance/native-workbench.json` 的 `Failure` 为 null。使用隔离配置和六个测试 ConPTY，仅操作新建窗口，结束时关闭测试会话并清理配置。截图已经查看并立即删除。桌面场景调用当前程序的 UI 命令，鼠标拖放事件由 Headless 单独覆盖。

## 验收发现并修复

- 关闭或迁出窗格会话时，树直接裁剪，即便存在备用终端也会合并布局。现在先填补未显示会话；不足时才合并。显式“移除窗格”仍只移除视图。退回单窗格时清理旧窗格引用。
- 自动隐藏侧栏覆盖终端区域时，拖到卡片上被解释为终端窗格落点。现在打开的侧栏优先接收卡片拖放。
- 工具页从原 UserControl 提取后丢失样式作用域，卡片圆角等样式失效。现在每页保留独立样式作用域与动态主题资源。
- 现有会话复用的固定分屏入口无需等待新卡片；只有实际创建终端时等待元数据绑定，保留原入口的即时切换行为。
- 旧测试定位隐藏的固定分屏或原窗口名称作用域，改为当前分屏宿主/组件。PowerShell 提示符实际已出现，但 `TailText` 裁掉行末空格，旧测试查找 `"> "` 超时；改为检查当前光标行的真实提示符。

## 重跑入口

```powershell
dotnet restore tests/TerminalHub.Tests/TerminalHub.Tests.csproj --disable-build-servers -m:1 -p:NuGetAudit=false
dotnet test tests/TerminalHub.Tests/TerminalHub.Tests.csproj --no-restore --disable-build-servers -m:1 -p:UseSharedCompilation=false --filter 'FullyQualifiedName~Workbench|FullyQualifiedName~SplitPane|FullyQualifiedName~SplitPopout|FullyQualifiedName~ProjectFeatures|FullyQualifiedName~ConfigurableSessionShortcut|FullyQualifiedName~TerminalImeTests|FullyQualifiedName~ThemeWorkspaceTests|FullyQualifiedName~WorkspaceTemplate|FullyQualifiedName~WindowsShellEditingTests'
dotnet build tools/TerminalHub.DesktopMeasurements/TerminalHub.DesktopMeasurements.csproj -c Release --disable-build-servers -m:1 -p:UseSharedCompilation=false
dotnet tools/TerminalHub.DesktopMeasurements/bin/Release/net8.0/TerminalHub.DesktopMeasurements.dll artifacts/v0.4.0/acceptance/native-workbench.json --workbench-acceptance
```

桌面入口要求先构建三个 Release 示例，见 [SDK 文档](plugin-sdk.md)。桌面入口生成同名 PNG，查看后立即删除。沙箱内 VSTest 无法连接 testhost，改在沙箱外以隔离测试配置执行成功。

## 未执行范围

系统输入法实际候选窗、多显示器、Linux 实机、真实 SFTP 远端，以及登录后的 Codex/Grok 编辑没有在本轮验收。Windows 安装包缺少 Inno Setup，未生成；便携包、Linux 跨平台构建和 SDK 包继续交付。没有提交、推送或发布 GitHub Release。
