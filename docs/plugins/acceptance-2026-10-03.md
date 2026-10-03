# 官方插件交付与验收（2026-10-03）

## 交付

- [使用与安装文档](README.md)、[离线手册](index.html)、[开发教程和 SDK 参考](../plugin-sdk.md)。离线页面有安装、官方插件、开发、API、常见问题的章节跳转；插件管理器增加「开发文档 ↗」「官方插件 ↗」入口。
- WorkspaceNotes、ScreenClips、CommandWatch 三个官方 API 1 插件，源码位于 `plugins/`，不引用 App 私有实现。构建脚本见 `scripts/build-official-plugins.ps1`。
- 可导入目录和 ZIP 位于 `artifacts/official-plugins/`，包含 DLL、依赖描述、清单、离线说明和许可证；打包仅收集明确列出的程序文件。
- 工具窗口保留独立窗口和左侧导航，导航固定为 184px，修正窄窗口英文名称碎行；插件页隐藏只服务于内置工具的保存按钮。
- 本轮源码和文档随本次 Git 提交交付；本地构建包尚未上传 Release。原有 v0.4.0 Release 未变更，用户正在运行的旧实例也未更新。

## 实际修复

笔记原本监听 Avalonia 排队发送的 `TextChanged`，立即切工作区时可能先加载下一工作区，再收到旧事件，漏存或误归属。改为同步监听 Text 属性变更；用立即切换、停用前最后输入和重新加载回归验证。

摘录跳过宽字符续格，保留完整字素簇；SGR 隐藏字符用占位空格，不将屏幕上不可见的字符暴露到摘录。列表重建前保留当前选择，防止自动清除选择导致刚抓的摘录不显示。

## 已执行

Release 三个插件和 Windows 原生验收工具均编译通过，0 警告、0 错误。

13 项相关 Headless 回归通过，包括 9 项官方插件场景及 4 项原有工具 UI 回归：

- 真实 DLL 导入、同时启用，停用和重新启用。
- 笔记工作区隔离、快速切换、停用前保存、配置恢复、中英文切换保留原文。
- 中文、组合字符、ZWJ emoji、国旗不重复，缩进与空行保留，隐藏字符不泄露。
- 摘录最新选择、20 份上限、历史选择、Headless 剪贴板复制、清空及停用清理。
- 用真实 VT 解析器回放 Shell 开始/完成标记，区分执行中、成功、失败、未知退出码和无标记；任意 error 文本不冒充失败。
- 看板跨工作区筛选及选项恢复。
- 四主题、英文和最小工具窗口布局；模块排序编辑及旧插件设置不失焦；文档文件随应用输出。

结果：`artifacts/official-plugins/acceptance/official-plugins.trx`。

实际 Windows Avalonia 窗口、打包后的三个插件与真实 ConPTY 共验证 11 项场景：

- 本机隔离会话启动，三个精简构建包实际加载，加入五个内置工具。
- 原生笔记自动保存、停用后重新加载恢复。
- 摘录真实 ConPTY 中文回显。
- Shell 实际输出 OSC 133 标记，退出码 7 到达看板；英文切换保留退出码。
- 关闭重开独立工具窗口保留缓存视图。
- 全部停用清理命令，原终端仍运行，无插件生命周期错误。

结果：`artifacts/official-plugins/acceptance/native.json`，`Failure: null`。验收使用隔离配置和独立终端，未结束用户现有会话。临时配置随工具退出清理。

内置浏览器加载离线手册的本机预览，点击开发和官方插件目录项确认 URL 分别跳转至 `#develop`、`#official`，检查页面排版。临时服务和预览页已关闭；插件主题截图查看后立即删除。

## 未执行的范围

系统文件保存选择器与真实系统剪贴板未做端到端人工操作；复制经过 Headless 剪贴板验证。离线文档入口的系统默认浏览器启动未做 OS 点击验收，页面章节跳转已在内置浏览器验证。Linux 实机未执行。IME 候选窗及多显示器不属于本轮插件验收，未新增验证结论。

## 重跑

```powershell
pwsh -File scripts/build-official-plugins.ps1
dotnet test tests/TerminalHub.Tests/TerminalHub.Tests.csproj --filter 'FullyQualifiedName~OfficialPluginTests|FullyQualifiedName~WorkbenchUiRefinementTests'
dotnet build tools/TerminalHub.DesktopMeasurements/TerminalHub.DesktopMeasurements.csproj -c Release
dotnet tools/TerminalHub.DesktopMeasurements/bin/Release/net8.0/TerminalHub.DesktopMeasurements.dll artifacts/official-plugins/acceptance/native.json --official-plugin-acceptance
```

Windows 原生验收依赖 `powershell.exe` 与 ConPTY；它用已知测试输入，不发送 AI 请求。

## 开发实操教程接续

新增[完整教程](development-tutorial.md)与 `examples/plugins/CommandDraft`。配套工程分别使用仓库 ProjectReference 和 v0.4.0 发行 SDK DLL 编译通过，均为 0 警告、0 错误。`PluginTutorialTests` 实际导入示例 DLL，核对输入字节未追加回车、激活另一个会话后的标签和输入目标、保存后停用重启的配置恢复，验证通过。结果 `artifacts/official-plugins/acceptance/plugin-tutorial.trx`。此示例本轮未做原生系统输入法或真实 Shell 操作验收。

离线教程在内置浏览器加载，完整实现章节跳转到 `#implementation`，全部源码、工程与清单块可阅读；应用「开发文档」入口改为打开 `development-tutorial.html#start`。

## 英文开发教程

补齐九章英文开发教程和离线 HTML，中英文页面可互相切换。插件管理器的开发文档入口按当前界面语言选择教程；应用和官方插件打包清单均包含两种语言。直接提取英文页的 csproj、清单与完整 C# 示例，引用已发布 v0.4.0 SDK 构建成功（0 警告、0 错误），临时工程已删除。App Release 构建通过（0 警告、0 错误），输出包含中英文离线教程；内置浏览器已核对英文页面、实现章节跳转、中英互切和首页排版。系统默认浏览器启动仍未做 OS 点击验收。现有发行附件尚未更新。
