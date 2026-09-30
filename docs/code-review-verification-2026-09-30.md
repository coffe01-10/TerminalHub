# GLM / Grok 审查复核汇总

后续状态：用户已先行修复大部分问题，本轮补齐剩余边界及性能项；详见 [修复与测量记录](<D:/ai tool/TerminalHub/docs/code-review-fixes-2026-09-30.md>)。以下内容保留为修复前的复核记录，行号也对应当时源码。

日期：2026-09-30。对象：本次工作区当前源码。两份审查按具体问题合并复核，不按模型之间的认领数量计数。没有修改产品实现。

结论：主要异常链、渲染越界和布局保存问题成立。发布对话框“正常关闭会抛异常”的说法未复现，应从已确认缺陷中移除；帧缓存“无上限累积”、同步输出“仅焦点视图重绘”和 SGR `4:3` 的语义需要修正。Linux 项可以确认代码问题，但不能声称已经在 Linux 实机复现。

**验证依据**

- 先检查 Git 状态，开始时没有已有未提交改动。
- 临时探针共运行 17 个用例，全部符合断言。这里的断言主要证明缺陷仍然存在，不代表修复通过。探针源码和它生成的数据已清除。
- 相关现有测试运行 101 个，全部通过：TerminalCoreTests、SplitPaneTests、WorkspaceRestoreTests、LineClassifierSummaryTests、CliInteractionTests。执行测试同时编译了 Core、Pty、App 和 Tests；没有另外宣称跑过全量测试或安装版验证。
- Windows 实机验证了 ConPTY 输出回调抛 IOException 后读任务结束、子进程仍存活。非法 UTF-8 使用直接喂入解析器验证；没有声称 ConPTY 原生输出会保留这些非法字节。
- Avalonia Headless 验证了真实行绘制函数、分屏关闭、模板保存和对话框关闭。未使用浏览器或 Playwright 替代原生验证。
- Linux 实机、系统输入法候选窗和同机性能基准本次未执行。NuGet 元数据访问出现 NU1900 警告，没有阻止本次编译和测试。

**优先修复：会停更、绘制抛错或丢失保存内容**

| 合并问题 | 复核结果与具体证据 | 定位 |
| --- | --- | --- |
| UTF-8 解出大于 U+10FFFF 的值 | 确认。`F4 90 80 80` 解出 U+110000，最终在 ConvertFromUtf32 抛 ArgumentOutOfRangeException。只检查 overlong 不足以保证 Unicode scalar 合法。 | [VtParser.cs](<D:/ai tool/TerminalHub/src/TerminalHub.Core/Terminal/VtParser.cs:162>)、[ScreenBuffer.cs](<D:/ai tool/TerminalHub/src/TerminalHub.Core/Terminal/ScreenBuffer.cs:243>) |
| Windows 输出回调异常结束整个读循环 | 确认并实机复现。订阅者抛 IOException 后，读任务为 RanToCompletion，而子进程 IsRunning 仍为 true。catch 在 while 外面。 | [ConPtySession.cs](<D:/ai tool/TerminalHub/src/TerminalHub.Pty/ConPtySession.cs:172>) |
| 文件日志失败传播到 PTY 读循环 | 源码链确认。AutoFlush 写入/轮转可能抛 I/O 异常；VM 的输出回调没有隔离该异常，最终进入上一行的停更路径。未通过填满真实磁盘复现。 | [SessionLogFile.cs](<D:/ai tool/TerminalHub/src/TerminalHub.Core/Logging/SessionLogFile.cs:41>)、[MainWindowViewModel.cs](<D:/ai tool/TerminalHub/src/TerminalHub.App/ViewModels/MainWindowViewModel.cs:1819>) |
| 关闭自动换行后的末列宽字 | 确认。4×2 屏幕最后一格写“中”后，IsWide 为 true、没有续格。Headless 调用实际 RenderRow，内部 DrawCells 抛 IndexOutOfRangeException。非最后行还可能把下一行首格带进当前绘制。 | [ScreenBuffer.cs](<D:/ai tool/TerminalHub/src/TerminalHub.Core/Terminal/ScreenBuffer.cs:344>)、[TerminalView.cs](<D:/ai tool/TerminalHub/src/TerminalHub.App/Controls/TerminalView.cs:636>) |
| 设置加载失败后覆盖原文件 | 确认。损坏 JSON 返回默认设置，后续 Save 会替换原文件；VM 退出没有区分“首次启动无文件”和“已有文件加载失败”。不是每次读失败都会立即成功覆盖，例如只读权限持续存在时写入也会失败。 | [SettingsStore.cs](<D:/ai tool/TerminalHub/src/TerminalHub.Core/Settings/SettingsStore.cs:40>)、[MainWindowViewModel.cs](<D:/ai tool/TerminalHub/src/TerminalHub.App/ViewModels/MainWindowViewModel.cs:2050>) |
| 打开模板保存早于卡片入架 | 确认。Mock PTY 同步启动路径中，OpenTemplateAsync 保存了 0 个会话；执行 UI 队列后实际已有 2 张卡片。正常退出可重新保存，但之前的异常退出会留下残缺工作区。 | [MainWindowViewModel.Workspaces.cs](<D:/ai tool/TerminalHub/src/TerminalHub.App/ViewModels/MainWindowViewModel.Workspaces.cs:78>)、[MainWindowViewModel.cs](<D:/ai tool/TerminalHub/src/TerminalHub.App/ViewModels/MainWindowViewModel.cs:1872>) |
| 分屏关闭后两侧绑定同一会话 | 确认。两个会话、右侧聚焦，关闭右侧后，IsSplit 仍为 true、LeftPane 与 RightPane 是同一对象。移除回调的回退之后，活动卡同步再次分配，AssignToPane 的 `?? s` 允许重复对象。实际两侧 PTY 尺寸抖动未单独测量。 | [MainWindowViewModel.cs](<D:/ai tool/TerminalHub/src/TerminalHub.App/ViewModels/MainWindowViewModel.cs:250>)、[MainWindowViewModel.cs](<D:/ai tool/TerminalHub/src/TerminalHub.App/ViewModels/MainWindowViewModel.cs:1899>) |

**Linux：源码确认，实机未验证**

| 问题 | 裁决 | 定位 |
| --- | --- | --- |
| read 缺少 SetLastError，n <= 0 就退出 | 成立。没有保留 errno，也没有区分 EINTR、EOF 和其他错误；补 SetLastError 之外还需要分支重试 EINTR。 | [LinuxPtySession.cs](<D:/ai tool/TerminalHub/src/TerminalHub.Pty/LinuxPtySession.cs:198>)、[声明](<D:/ai tool/TerminalHub/src/TerminalHub.Pty/LinuxPtySession.cs:365>) |
| 输出回调异常结束读任务 | 成立，但不同于 Windows。回调位于内层 native-call try 外，外层只捕获 ObjectDisposedException；一般异常会使读任务 faulted。 | [LinuxPtySession.cs](<D:/ai tool/TerminalHub/src/TerminalHub.Pty/LinuxPtySession.cs:207>) |
| fork 后、判断子进程前构造 SafeFileHandle | 成立。子进程也执行托管对象构造；与注释“子进程仅执行预热 libc 调用”不符。死锁等实际后果没有复现，不能写成已观测故障。 | [LinuxPtySession.cs](<D:/ai tool/TerminalHub/src/TerminalHub.Pty/LinuxPtySession.cs:57>) |
| chdir 失败仍继续 exec | 成立。返回值未检查，可能从继承的目录启动，而不是用户指定目录。 | [LinuxPtySession.cs](<D:/ai tool/TerminalHub/src/TerminalHub.Pty/LinuxPtySession.cs:71>) |

**协议与交互：合并为第二批修复**

| 问题 | 复核结果 | 定位 |
| --- | --- | --- |
| UTF-8 代理项 | `ED A0 80` 会存成孤立 U+D800，不抛异常。应与非法 scalar 一起替换为 U+FFFD，但不要把它单独描述为已证实的读循环崩溃。 | [ScreenBuffer.cs](<D:/ai tool/TerminalHub/src/TerminalHub.Core/Terminal/ScreenBuffer.cs:245>) |
| SGR 冒号子参数 | 回放确认：38:2:255:0:0 留下 `2:255:0:0m`，4:3 留下 `3m`。冒号触发退出 CSI。不能把冒号简单替换成分号；`4:3` 表示一种下划线样式，不是“下划线加斜体”。 | [VtParser.cs](<D:/ai tool/TerminalHub/src/TerminalHub.Core/Terminal/VtParser.cs:281>) |
| ESC = / ESC > | 回放确认误改 ApplicationCursorKeys。标准含义是应用/普通数字小键盘模式。 | [VtParser.cs](<D:/ai tool/TerminalHub/src/TerminalHub.Core/Terminal/VtParser.cs:228>) |
| CUU / CUD 滚动边距 | CUU 回放确认从滚动区顶部越出到屏幕顶部；CUD 的同类实现源码确认。应根据光标原位置决定约束，不能无条件把屏幕外区的光标拉回滚动区。 | [ScreenBuffer.cs](<D:/ai tool/TerminalHub/src/TerminalHub.Core/Terminal/ScreenBuffer.cs:514>) |
| 主/备用屏幕保存光标互相覆盖 | 回放确认。主屏保存 (row 4, col 5)，备用屏再 SaveCursor 到 (2,2)，1049 退出后恢复成 (2,2)。独立的 _primaryX/Y 没有保护这套 saved cursor 状态。 | [ScreenBuffer.cs](<D:/ai tool/TerminalHub/src/TerminalHub.Core/Terminal/ScreenBuffer.cs:534>) |
| 原点模式 CPR | 回放确认。滚动区从第 3 行开始，原点模式逻辑第 2 行，返回第 4 行的绝对坐标。 | [VtParser.cs](<D:/ai tool/TerminalHub/src/TerminalHub.Core/Terminal/VtParser.cs:387>) |
| RIS / DECSTR | 回放确认 RIS 后仍处备用屏；DECSTR 没有恢复 Bold 等状态。RIS 还没有重置 ApplicationCursorKeys。 | [VtParser.cs](<D:/ai tool/TerminalHub/src/TerminalHub.Core/Terminal/VtParser.cs:258>)、[CSI 派发](<D:/ai tool/TerminalHub/src/TerminalHub.Core/Terminal/VtParser.cs:358>) |
| 同步输出超时 | 回放确认 180ms 后能取得实时帧，但标志仍为 true。计时器据此持续请求重绘；条件不限定焦点，只排除了不可见视图。之后再次 enable 也不会重新捕获 held frame，直到应用明确 disable。 | [ScreenBuffer.cs](<D:/ai tool/TerminalHub/src/TerminalHub.Core/Terminal/ScreenBuffer.cs:27>)、[TerminalView.cs](<D:/ai tool/TerminalHub/src/TerminalHub.App/Controls/TerminalView.cs:146>) |
| 双击一个单格词 | 源码和选区探针确认。双击产生首尾相等的包容端点；GetSelectedText 已返回 null，抬起又清除。尚未使用完整鼠标双击事件复现。 | [TerminalView.cs](<D:/ai tool/TerminalHub/src/TerminalHub.App/Controls/TerminalView.cs:1190>)、[取选中文字](<D:/ai tool/TerminalHub/src/TerminalHub.App/Controls/TerminalView.cs:1337>) |
| 三击软换行整行 | 源码确认只选择当前物理行；ExtractText 只在选区跨行时合并软换行，不会自动扩大选区到下一行。优先级低于单格词。 | [TerminalView.cs](<D:/ai tool/TerminalHub/src/TerminalHub.App/Controls/TerminalView.cs:1171>) |
| AltGr 与字号快捷键 | 模拟 KeyEventArgs 确认 Ctrl+Alt+OemPlus 字号增加并 Handled。尚未使用实际 AltGr 键盘布局验证系统发出的事件。 | [MainWindow.axaml.cs](<D:/ai tool/TerminalHub/src/TerminalHub.App/Views/MainWindow.axaml.cs:552>) |
| 错误/警告正则 | 回放确认 `10 个错误`、`100 个错误`、`warnings` 都为 info；对照 `10 errors` 正确为 error。中文 lookbehind 没有数字边界。 | [LineClassifier.cs](<D:/ai tool/TerminalHub/src/TerminalHub.Core/Logging/LineClassifier.cs:12>) |
| PowerShell 的 cd 路径 | 源码确认 Windows 分支只加双引号，会展开路径中的 `$`。已有 ShellPathInput 对 PowerShell 用单引号，修复可复用其对应规则并按实际 Shell 选择。 | [MainWindowViewModel.cs](<D:/ai tool/TerminalHub/src/TerminalHub.App/ViewModels/MainWindowViewModel.cs:1807>)、[ShellPathInput.cs](<D:/ai tool/TerminalHub/src/TerminalHub.Core/Pty/ShellPathInput.cs:5>) |
| SSH 目标转义 | 确认参数构造不完整：目标 `a"b` 输出 `-p 22 a"b`，现有 Linux 参数拆分得到 `ab`。这是包含引号等异常字符时的问题；没有证据说明普通 user@host 会失败，也没有通过它发起真实 SSH 连接。 | [SshHost.cs](<D:/ai tool/TerminalHub/src/TerminalHub.Core/Ssh/SshHost.cs:27>) |
| 文件链接长匹配吞掉真实文件 | 实际临时文件回放确认：`real.cs:12` 能解析，`prefix/ text real.cs:12` 中点击 real.cs 返回 null。不能简单禁止空格，否则会破坏合法带空格路径；应处理不存在的宽匹配内的短候选。 | [TerminalContentLink.cs](<D:/ai tool/TerminalHub/src/TerminalHub.Core/Terminal/TerminalContentLink.cs:11>) |
| 日志轮转按字符数 | 实际写文件确认：写入一行包含“中文”的日志，文件增加 35 字节，计数只增加 31。16MiB 是名义阈值，不是实际字节阈值；标题也没有计入。 | [SessionLogFile.cs](<D:/ai tool/TerminalHub/src/TerminalHub.Core/Logging/SessionLogFile.cs:49>) |

协议含义对照了 [xterm 官方控制序列说明](https://invisible-island.net/xterm/ctlseqs/ctlseqs.html)。边距、分屏保存光标和 CPR 原点处理也对照了 [Microsoft Terminal 的实现](https://github.com/microsoft/terminal/blob/main/src/terminal/adapter/adaptDispatch.cpp)。

**需要更正或移出缺陷清单的结论**

- 发布配置对话框：缺 catch 是源码事实，但正常 X 关闭和关闭父窗口，ShowDialog<bool> 都正常完成，没有异常。不可见父窗口调用 ShowDialog 会抛 InvalidOperationException，属于另一个触发条件；本次没有找到用户正常点击该菜单会触发它的证据。不能与停更/绘制越界并列为已确定 P1。定位：[MainWindow.axaml.cs](<D:/ai tool/TerminalHub/src/TerminalHub.App/Views/MainWindow.axaml.cs:532>)。
- ConPTY Dispose 后句柄复用：两位模型收回正确。ReadFile/WriteFile 参数为 SafeFileHandle，P/Invoke 会保护调用期间的引用计数；Dispose 顺序与注释不一致可以整理，但不是已证实的句柄复用故障。参见 [Microsoft SafeHandle 文档](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.safehandle?view=net-8.0)。
- 日志等值行 IndexOf：没有复现出用户可见差别，也没有看到当前列表顺序会导致这里错误淘汰的证据。不要把“按引用查找”自动列成必须修复的独立缺陷。线性查找开销可以纳入性能测量。
- 帧缓存“无上限”：不准确。offset 被限制在 ScrollbackCount 内，历史默认上限为 2000 行、裁剪有 128 行余量；解析器批次/resize 改版本，下一次 CaptureFrame 就清空旧缓存。确认的是静止版本下滚动浏览会保存多个完整帧，可能明显增加内存。探针 98 行历史产生 99 个缓存项，下次 Feed 后变回 1 个。
- “没有 P0”“主路径稳”“质量高于平均”属于整体评价。本次可给具体问题排优先级，不能据这次局部复核证明全库不存在更严重问题。
- GLM 提到但未提供内容的“照单 P3 杂项”没有可验证的具体断言，不计入本次结果。

**性能与资源增长：现象成立，代价未测量**

| 项目 | 源码能证明什么 | 下一步需要的数据 |
| --- | --- | --- |
| 16ms 定时器 | 可见视图每 tick 进行坐标同步并加锁；焦点视图查询 IME 位置。 | 空闲时不同会话/窗格数量的 CPU、锁等待和帧请求次数。 |
| 画刷分配 | Render、DrawCells 等路径仍 new SolidColorBrush；已有文本排版缓存并未消除这些分配。 | 固定输出负载的分配率和 GC 数据。 |
| 滚动帧缓存 | 每个浏览过的 offset 保存完整 Cells 数组，同版本内没有淘汰。 | 固定历史和屏幕尺寸下，滚动前后的保留内存。 |
| Logs 重计数 | RefreshLevelCounts 对整个深缓冲扫描。虽然方法按 CollectionChanged 事件调用一次，Dashboard 的 FlushOutput 实际逐行 OutputLog.Add，所以当前输入链仍每行触发一次扫描。 | 固定容量、输出速率、过滤设置下的 UI 时间。 |
| 缩略图刷新 | 监视器每秒 RefreshCounts，对每张卡 RefreshPreview，重新取 TailLines、创建预览列表。 | 空闲及连续输出下多会话的 CPU/分配差异。 |
| 旧日志文件 | 会轮转，但没有目录保留或清理策略。 | 用户需要的保留天数/空间预算；这属于存储策略，不自动等价于程序崩溃。 |

**合并后的修复顺序**

1. 先处理 Unicode scalar 校验和输出链异常隔离，包括日志失败的处理；随后修末列宽字绘制越界。隔离一个 chunk 的异常只能避免整个 reader 死亡，不能替代修复解析器，也不能保证同一批剩余字节或后续事件订阅者继续执行。
2. 修配置加载失败回写、模板保存时序和分屏重复会话。针对已复现的具体场景增加正式回归，不需要额外全仓门禁。
3. Linux read/回调和 fork/chdir 单独修复，并明确实机验证状态，不让本机缺 Linux 阻塞其他修复。
4. 协议、选择、快捷键、正则、Shell 路径和日志字节计数按上表分小批处理。发布对话框不放入已确认 P1。
5. 同机测量后再决定常态性能优化和日志保留策略；同步输出超时标志残留是已复现的状态 bug，可以先修，不必等全面基准。

这次交付仅增加复核文档，没有修复上述缺陷，没有更新当前运行进程或安装版。
