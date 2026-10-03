# Terminal Hub 开发指引

这份文件帮助后续开发者和模型接续工作。以用户当前任务为范围；下面的路线图不是一次性执行所有功能的授权。现状说明整理于 2026-09-29，后续以当前代码和实际运行结果为准。

## 协作约定

- 默认用中文沟通，先说明结果、原因和下一步，少讲过程口号。
- 创建的临时脚本、探针、截图、临时配置用完立即清除；用户提供的文件和正式回归测试不是临时文件，不要误删。
- 未经用户另行指示，网页验证默认使用内置浏览器，不使用 Playwright。本项目主体是原生 Avalonia 应用，原生问题用原生运行或 Avalonia Headless 验证，浏览器不能代替原生终端。
- 不添加安全测试、扫描、哈希校验或审批流程。不要过度防御性编程。每个新增检查都要能说清楚它抓过什么实际 bug，或防住什么具体风险；说不清就不加。
- 缺失环境、凭据、工具时，说明哪些验证没做，继续完成不依赖它们的部分。不得把缺失环境变成永久挂起的阻塞项，也不得把未执行描述为通过。
- 先查看 `git status --short` 和相关代码，保留已有未提交改动。本仓库可能同时包含多项开发中的修改，不要顺手回滚、清理或整体覆盖。
- 日常修复直接推进，不反复请求确认；不把局部 bug 扩成全项目重构。提交、推送、发布和合并遵循当前任务授权，不从旧开发日志推导授权。
- 不提交或上传用户本地配置，尤其是 SSH 连接记录、用户名、地址和密钥路径；不把这些内容复制到发布包、截图、日志或回归样本。打包使用程序产物，排除 `settings.json`、`settings-*.json` 和 `.ssh`；保护用户已有配置，不为打包清理它们。

## 项目是什么

Terminal Hub 是 Windows 优先的原生多会话终端：Avalonia 11、.NET 8，Windows 使用 ConPTY，Linux 使用真实 PTY。不是网页终端，也不是浏览器中的 xterm.js。

已有主终端、会话缩略图、分屏与独立窗口、主题、文件/日志/进程/SSH 面板及打包入口。不要在未检查实现的情况下重新做一套已有能力。

| 要改什么 | 优先阅读 |
| --- | --- |
| 文字绘制、键盘、光标、输入法、滚动 | `src/TerminalHub.App/Controls/TerminalView.cs` |
| 默认颜色、反色、主题 | `src/TerminalHub.App/Controls/TerminalPalette.cs`、`ThemeManager.cs` |
| VT 转义序列、字符、屏幕与历史缓冲 | `src/TerminalHub.Core/Terminal/VtParser.cs`、`ScreenBuffer.cs`、`TerminalCell.cs` |
| 稳定屏幕快照、PTY 与缓冲连接 | `src/TerminalHub.Core/Terminal/TerminalFrame.cs`、`TerminalEmulator.cs` |
| Windows/Linux 终端进程 | `src/TerminalHub.Pty/ConPtySession.cs`、`LinuxPtySession.cs` |
| 终端环境、Shell 集成 | `src/TerminalHub.Core/Pty/PtyEnvironment.cs`、`ShellIntegration.cs` |
| 会话生命周期 | `src/TerminalHub.Core/Sessions/SessionManager.cs`、`TerminalSessionModel.cs` |
| 主窗口和布局 | `src/TerminalHub.App/Views/MainWindow.axaml`、对应 code-behind 与 ViewModel |
| 会话侧栏、缩略图、动画 | `src/TerminalHub.App/Controls/StageSurface.cs`、`StageCard.cs`、`StagePreview.cs` |
| 配置保存、启动会话 | `src/TerminalHub.Core/Settings/AppSettings.cs`、`SettingsStore.cs` |
| 已修复输入法问题的回归用例 | `tests/TerminalHub.Tests/TerminalImeTests.cs` |

`docs/PRODUCT.md` 和 `docs/development-progress.md` 用于理解历史背景。其中的旧状态、旧平台限制、测试数量和工作流描述不代表当前实现或本次任务要求。

## 已踩过的坑：修改前务必理解

### 1. Claude Code 的编辑光标不等于 VT 光标位置

已通过本机 Claude Code v2.1.281 的真实 ConPTY 输出确认：

- 它可以隐藏 VT 光标，并把 VT 位置停在状态栏或一次局部重绘的末尾。
- 输入区的实际编辑光标使用 SGR 7 反色字符绘制，SGR 27 结束反色。
- 行末光标可能是一个反色空格；中文光标占主字符格和宽字符续格。
- 输入 `ab中文cd` 后，实际捕获的光标列从末尾第 10 列，经三次左移到第 6 列的“文”，再右移到第 8 列的 `c`；这些列号从 0 开始。VT 光标位置并不同步表示插入点。

曾经出现的错误：只取输入文本末尾，导致方向键已经移动了实际插入点，但指示和输入法还停在末尾；空输入时退回隐藏 VT 光标，又把输入法放到了状态栏。

当前处理：可见 VT 光标优先；隐藏光标时，在识别出的有上下分隔线的输入区内寻找单个反色字符作为编辑位置。跳过宽字符续格，不能把多个反色字符的选区当成一个光标。没有明确标记时才使用输入区末尾等回退。

这是针对观察到的 TUI 输出的兼容逻辑，不是通用终端协议。不要把所有应用的反色文本、任意 `>` 或隐藏 VT 位置一律当作编辑光标。遇到新样式先捕获实际输出，再调整识别。

### 2. 反色必须先解析颜色，再交换前景与背景

`TerminalColor.Default` 的实际颜色取决于它原本是前景还是背景。

错误做法：先交换两个 Default 标记，再分别解析；或者看到背景标记为 Default 就不绘制。这样会漏掉反色块光标的背景，浅色和深色主题都会出问题。

正确做法：先按原本角色解析出实际前景色和背景色，再交换；反色情况下即使原始颜色都是默认值，也要画背景。已有应用光标时，不再额外画一个停在末尾的合成指示。

### 3. 字体实际宽度不一定等于终端格宽

回退中文字体的字宽不保证恰好等于两个终端格。整段混排文字按自然宽度绘制、输入法却按格宽定位，会产生随中文数量累积的横向间隙。

当前绘制保留 ASCII 分段，非 ASCII 字符按终端格位置推进。后续改进字体或排版时，必须让文字绘制、光标、输入法和选区共享相同的格坐标约定，不要改回整段自然宽度定位。

当前格子使用 `Char` 加 `Tail` 保存完整簇，已处理代理对、组合字符、ZWJ emoji 和国旗对；格宽由 `GraphemeWidth` 共享。尚不能据此宣称完整支持所有 Unicode 字素规则。Unicode 改进需要同时考虑解析、缓冲、绘制及位置映射，不能只在显示层补一个偏移量。

### 4. 输入法可能在下一次 Render 前查询位置

输入法组合开始或 PTY 刚输出时，系统可能立即读取 `CursorRectangle`。只使用上次 Render 缓存的位置会产生旧坐标。

保留基于当前稳定帧求位置的能力；组合文字与候选窗使用同一套定位。IME 已消费的按键不能重复发给 Shell，尤其不要让选词 Enter 同时执行命令。Shift+Enter 的 CSI-u 仅在应用协商对应键盘协议后发送。

### 5. 预览、分屏和独立窗口共享会话内容

- 缩略图只能展示，不能为了适应自身尺寸去 resize 活动 PTY。
- 主视图判断是否 resize 时比较模拟器的真实尺寸，不依赖可能过期的“上次尺寸”缓存；其他视图可能已经改过尺寸。
- 渲染读取稳定的 `TerminalFrame`，不要在 PTY 后台改写时直接逐格读取活动缓冲。
- 从主窗口弹出会话时，区分“转移所有权”和“关闭进程”，不要为布局切换重启或杀掉会话。

### 6. ConPTY 在测试进程中的行为可能和 GUI 不同

标准句柄被测试运行器或命令行重定向时，子程序可能未正确附着控制台。实际遇到过 Claude 被判定为非交互输入，报需要 stdin/prompt，看起来像 CLI 本身启动失败。

先参考 `Program.cs` 的启动处理，以及 `WindowsStreamingTests.cs` 的真实 ConPTY 用例。探针若临时修改进程标准句柄，必须在 `finally` 中恢复，并沿用 `ProcessWide` 集合避免影响同进程其他测试。

捕获 CLI 输入问题时，只输入复现用文字和按键，不提交 AI 请求。新目录出现信任提示时，不自动确认；可使用已经信任的复现目录，或说明本次无法完成实机验证。

VT 查询响应应在释放缓冲锁后写回 PTY。现有实现这样做，是因为阻塞的 PTY 写入会卡住持锁的绘制路径，不要随意移回锁内。

## 后续开发顺序

2026-09-30 更新：拖选复制、搜索高亮与定位、工作区恢复、字号快捷键和绘制缓存已经存在，本节不应被理解为这些功能尚未实现。本轮已增加普通屏幕与历史软换行重排、保存光标映射、历史裁剪后的选区和搜索标记重定位、缺失 Shell 的启动选择入口，以及会话新输出/退出码提示。重排或主/备用屏幕切换会清除旧选区，防止旧格坐标复制到别的文字。性能测量与优化本轮未执行。

按用户本次选择推进，不自动把整个列表全部实现。

| 顺序 | 工作 | 具体交付 |
| --- | --- | --- |
| P0 | 光标与输入法定位收口 | 集中处理真实光标和应用反色光标，完善多行编辑、换行边界、缩放与分屏切换后的定位 |
| P0 | Unicode 与格宽完善 | 逐步支持完整字符和字素簇，统一缓冲、绘制、输入法、选区的位置映射 |
| P1 | 终端选中与复制 | 拖选、跨行复制、双击选词、三击选行、拖选滚动；使用 Ctrl+Shift+C，保留 Ctrl+C 中断 |
| P1 | 历史浏览和搜索 | 复用现有 `SearchLines` 和搜索面板，增加原文高亮、前后定位、稳定滚动位置及回到底部入口 |
| P1 | 多会话性能 | 先测空闲和连续输出，定位完整帧复制、文字排版、缩略图刷新开销，再做按变化刷新和排版缓存 |
| P2 | 工作区恢复 | 基于现有启动会话配置，保存顺序、名称、目录、Shell、分屏和活动会话；恢复布局不等于恢复原进程 |
| P2 | 常用操作与视觉 | 字体字号、快捷调整与重置、会话排序、活动窗格标识、浅色主题下的光标和选区对比度 |

首轮适合交付：光标定位整理、鼠标拖选与复制、多会话性能测量和一轮有数据支撑的优化。不要继续堆旁支面板功能而遗漏终端基础体验。

## 怎样验证才有用

- 用具体场景证明修改有效，不以测试数量作为完成标准。
- 输入法/光标问题优先回放真实 VT 输出，并核对格坐标和实际绘制；仅证明按键已经发送给 PTY 不足以证明光标显示正确。
- 修改相关行为时补对应回归：例如中文左移跨错格、反色空格不可见、跨行复制多出换行。不要为纯文案或低影响可逆修改堆测试。
- 性能优化比较同机、同会话数量、同输出负载下的数据；先测量再定目标，不承诺未经测量的 CPU 降幅。
- 跑与改动有关的检查，通过后继续交付；没有新改动或新疑点，不反复跑全量测试。
- 明确区分“编译通过”“模拟/回放验证”“真实 CLI 验证”“系统输入法候选窗验证”。前者不能替代后者。

常用命令（仓库根目录运行）：

```powershell
# 编译与启动
dotnet build TerminalHub.sln -c Debug
dotnet run --project src/TerminalHub.App

# 光标与输入法相关回归
dotnet test tests/TerminalHub.Tests/TerminalHub.Tests.csproj --filter FullyQualifiedName~TerminalImeTests

# 仅在修改同时涉及输入、终端缓冲、布局或主题时扩展到对应测试
dotnet test tests/TerminalHub.Tests/TerminalHub.Tests.csproj --filter 'FullyQualifiedName~TerminalImeTests|FullyQualifiedName~TerminalInputTests|FullyQualifiedName~TerminalCoreTests|FullyQualifiedName~TerminalStreamingTests|FullyQualifiedName~SplitPaneTests|FullyQualifiedName~ThemeWorkspaceTests'
```

已还原依赖时可以加 `--no-restore`。不要因本地缺少某个平台或外部服务，就阻塞其他可以验证的内容。

应用有单实例逻辑。新编译不代表当前运行的旧进程已更新；新进程立即退出时先检查是否已有实例，不要直接判定启动失败，也不要擅自结束用户正在使用的终端。

## 交付说明

简短交代：改了什么、解决哪个具体问题、做了哪些验证、还有哪项未验证。若只修改源码并编译成功，不要声称安装版或当前运行进程已经更新。


## 2026-10-03 工作台扩展接续

v0.4.0 七项功能源码已实现，发行上传与人工验收另见 `docs/TODO.md`。任意分屏优先阅读 `PaneLayout.cs`、`PaneNode.cs`、`MainWindowViewModel.PaneTree.cs` 和 `MainWindow.PaneTree.cs`；不能再以四个旧窗格属性作为完整布局。旧属性只兼容已有设置和固定布局入口，树节点记录完整结构。

插件公开契约位于 `src/TerminalHub.Extensibility`，加载/生命周期在 `App/Plugins/PluginManager.cs`，宿主操作在 `MainWindowViewModel.PluginHost.cs`，主窗口扩展展示在 `MainWindow.Plugins.cs`。用户于 2026-10-03 要求恢复原来的独立工具窗口。五个工具和插件仍从 `ProjectToolsView` 复用并共用模块管理：窗口可关闭重开，页面缓存保留，不要重新改成主窗口底部区域。界面精修记录见 `docs/ui-refinement-2026-10-03.md`。停用插件只清理它的注册，不能结束宿主会话。用户随后要求补做验收，相关回归与真实 Windows 原生窗口/ConPTY 验收已执行，结果见 `docs/acceptance-v0.4.0-2026-10-03.md`。系统 IME 实际候选窗、多显示器与 Linux 实机仍未执行，不能把格坐标回归当成系统候选窗验收。

官方插件位于 `plugins/WorkspaceNotes`、`ScreenClips`、`CommandWatch`，共享 UI 代码通过源码链接编入各自 DLL。构建 `scripts/build-official-plugins.ps1`，导入目录位于 `artifacts/official-plugins`。安装与开发见 `docs/plugins/README.md`、`docs/plugin-sdk.md`，离线跳转页 `docs/plugins/index.html` 随 App 输出与发行包携带。工作区笔记监听同步 Text 属性变化，不能改成排队的 TextChanged 后再保存，快速切换会漏存。ScreenClips 的 API 1 帧缺少软换行元数据，只能摘录物理屏幕行；跳过宽字符续格和隐藏字符。命令看板只认 Shell 集成事件，不猜终端输出。相关回归 `OfficialPluginTests`，Windows 实机模式 `--official-plugin-acceptance`，验收边界见 `docs/plugins/acceptance-2026-10-03.md`。
