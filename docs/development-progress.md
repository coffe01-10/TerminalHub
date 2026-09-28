# Development progress — Terminal Hub / 终端控制中心

> Autonomous build log. PRs merge to `main` when green.

## 2026-09-28

### PR #30 — Logs 时间戳相对/绝对切换 (`feat/logs-timestamp-toggle`)
- **规则（距现在 ago-from-now）**: 关=绝对本地时钟 `HH:mm:ss`；开=相对短标签
  `刚刚`（&lt;2s）/ `12s` / `3m` / `1h` / `昨天 HH:mm`；比昨天更早或异常 Time →
  回退绝对。未来/时钟偏差按 0 处理（显示 `刚刚`），不抛。
- **UI**: 级别 chip 旁 `相对` ToggleButton（`timechip` 样式，不挤占 `levelchip`）；
  `LogsList` DataTemplate 经 `LogTimestampConverter` MultiBinding
  （`Time` + `Logs.UseRelativeTimestamps`）显示所选格式。
- **持久化**: 全局 `AppSettings.LogsUseRelativeTimestamps`（默认 false）；与其它
  Logs 过滤 prefs 一样经 `PersistLogsFilters` 落盘；**不是** per-session 字段。
- **导出/复制**: `FormatLine` / `BuildVisibleText` 始终绝对时间，不随显示模式变。
- **验证**: 187 tests green（本切片 +4：formatter 各档 + 怪异 Time、导出仍绝对、
  SettingsStore 往返、MainWindow Toggle 绑定 + 列表相对标签）。worktree
  `/workspace/TerminalHub-logs2`；工具 Grok Build。

### PR #31 — Files「在此打开终端」+「复制路径」 (`feat/files-open-in-terminal`)
- **在此打开终端**: Files 工具行 ⇥ 按钮 + 条目右键菜单 —— 选中**目录** → 活动
  PTY 真实 `cd '<path>'`（与工具栏 CWD ←/→ 同一 `ApplyDisplayedCwd` 路径：
  `QuoteForShell` 引号转义、CWD 历史 push、路径栏与 Files 跟随）；选中**文件**
  → cd 其父目录。无选中 / 无活动会话时 CanExecute 禁用，会话切换实时刷新。
- **复制路径**: ⧉ 按钮 + 右键菜单把选中项绝对路径写入剪贴板（复用
  `CopyTextToClipboardAsync` best-effort）；`StatusText` 显示
  「终端已 cd → …」「已复制 …」。
- **解耦**: `FilesViewModel` 只持回调（`openTerminalAt`/`copyTextAsync`/
  `hasActiveSession`），由 `MainWindowViewModel` 注入真实 shell 动作。
- **验证**: 181 tests green（+5 `FilesOpenInTerminalTests`：VM 层 dir/file/
  禁用态/复制 spy + headless 端到端 mock PTY 缓冲含 `cd`、CWD chrome 同步、
  历史可回退）。DISPLAY=:7 实机：`/tmp/00-demo-open` ⇥ → 终端
  `cd '/tmp/00-demo-open'` 执行、提示符跟随、卡片缩略图同步；⧉ 后
  `xclip -o` 读到 `/tmp/00-demo-open`。
- 截图 `docs/screenshots/files-open-in-terminal.png`。

### PR #29 — 会话快捷键 + ••• 会话菜单 (`feat/session-shortcuts`)
- **Ctrl+W** 关当前会话、**Ctrl+Tab / Ctrl+Shift+Tab** 双向循环会话卡 —
  窗口级 Tunnel `KeyDown` handler 抢在 TerminalView 之前(否则 Ctrl+W 被
  shell 吃掉当 kill-word、Ctrl+Tab 被当 `\t` 补全),走正常激活路径
  (分屏时分配到聚焦窗格)。
- **••• 菜单**:`Button.Flyout` + `MenuFlyout` —— 关闭会话 / 下一个会话 /
  上一个会话 / 复制 CWD(`CopyActiveCwd` 写剪贴板 + Output 记 `ui` 行),
  菜单项命令绑定 VM 命令。
- **验证**:6 个新测 —— `KeyPress` 真输入管线 Ctrl+W 关会话 / Ctrl+Tab
  双向循环(含 wrap)、CycleSession 环绕、CloseActiveSession 杀 PTY+
  激活下一个、CopyActiveCwd 写 ui 日志、••• flyout 菜单项命令绑定。
  DISPLAY=:7 实机:Ctrl+W 关 T02(tab 消失)、Ctrl+Tab/Shift+Tab 双向、
  ••• 菜单点开 +「复制 CWD」→ Output `已复制 CWD: /home/box`。
- 截图 `session-shortcuts.png`。180+ tests green。

### PR #27 — Logs 点击跳到会话 (`feat/logs-click-jump-session`)
- **双击 / 「↗ 跳到会话」**: Logs 列表行的 `LogEntry.Source` 若是会话名，则激活对
  应 `SessionCard` / `ActiveSession`（经 `MainWindowViewModel.TryActivateSessionByName`）；
  **Logs 页保持打开**（不改 `SelectedRightTab`）。状态行 `已跳到「Terminal 03」`。
- **健壮**: Source 空 → `该行没有会话来源`；未知名（如 `deploy`）→ `未找到会话「…」`；
  无选中 → `没有选中的日志行`；不抛。优先双击，避免与上一条/下一条的单击选中冲突；
  按钮 / Enter 走 `JumpToSessionCommand`（选中行）。
- **接线**: `LogsViewModel` 注入 `Func<string, bool>? activateSession`；宿主只翻会话，
  不碰 Deploy/Files/SSH/PTY/toolbar。
- **验证**: 183 tests green（本 PR +3：已知 Source 激活 + 状态、缺失/未知软提示、
  MainWindow 端到端保持 Logs tab）。worktree `/workspace/TerminalHub-logs2`；
  工具 Claude Code · GLM-5.3。

### pending / next Deploy slice — 发布实时输出进 Output + 运行中可取消 (`feat/deploy-publish-stream-cancel`)
- **实时输出**: Publish 会话的 PTY stdout/stderr 原样进入底部 Output，`source` 为 `deploy`
  （不再用会话名 `Publish`）。不造进度条、百分比或假徽章。其它会话仍用会话名。
  开始推流时（首行）以及退出时聚焦 Output 页。开始打包 / 成功 / 失败 / 已在运行 /
  配置档 / 最近产物这些状态行保持不变。
- **取消**: 打包进行中 Deploy 坞右键「取消打包 Cancel」可用，按钮文案为「打包中」；
  空闲时该项禁用、文案回到 Deploy。取消会杀掉发布进程（Linux 上若子进程是会话/进程组
  leader，则 `SIGHUP` 再 `SIGKILL` 整个进程组，Windows 仍是 `entireProcessTree`）。
  退出记一条 `publish cancelled`（source `deploy`），`PublishBusy` 回到 false，可再次打包。
  空闲时取消只警告一次；重复取消不再多记。
- 配置档与最近产物（PR #22）行为不变。
- 实机截图：`docs/screenshots/deploy-stream-cancel.png`（Output `deploy` 流 + 右键「取消打包」）。

### PR #26 — Logs 按会话筛选记忆 (`feat/logs-pin-session-filters`)
- **per-session 记忆**: 会话下拉选中具名会话时，其筛选组合（FilterText /
  UseRegex / 级别 chip / 保留历史）按**会话名**记进
  `AppSettings.LogsSessionFilters`（新 JSON dict + `LogsSessionFilterState`）。
  切换会话恢复该会话上次的组合；未配置 → 默认（空文本 / 非正则 / 全部 / 不保留）。
  「全部会话」(index 0) 仍是全局 `LogsFilterText` 一族字段的落点 —— 即全局兜底。
- **写入时机**: 每次过滤变化即把当前组合写进当前选择的槽位（具名会话 → map
  条目；index 0 → VM 内的全局快照），`PersistLogsFilters` 落盘时同时写全局
  字段（「全部」最后组合，具名会话上的修改不污染它）与整份会话 map。
  刻意不在 `OnSessionFilterChanging` 按旧索引保存：`RefreshSessions` 先重绑
  名单再设索引，旧索引届时可能指向别的会话名会写错键；逐次写入无此竞态。
- **切换恢复**: `OnSessionFilterIndexChanged` → `RestoreFiltersFor`，恢复期间
  复用 `_restoringFilters` 抑制写回（无 load→save 循环）；恢复非默认组合时
  状态行提示 `已恢复「Terminal 01」筛选`（复用现有 `StatusText`，无新增
  chrome）。`RefreshSessions` 不清 map，重绑后索引变化同样恢复；JSON 里的
  null state / 空会话名跳过不炸。
- **验证**: 180 tests green（本 PR +4：切换恢复 / 未配置默认 / 提示、全局与
  各会话独立、快照 + `ApplySessionFilterMap` 跨 VM 往返、SettingsStore 落盘
  → 重启恢复（mock PTY 起启动会话））。worktree `/workspace/TerminalHub-logs2`；
  工具 Claude Code · GLM-5.3。


### PR #25 — Logs 搜索高亮 + 上一条/下一条 (`feat/logs-search-nav`)
- **高亮**: 扩展 `HighlightTextBlock`（`UseRegex`）；Logs 列表 Message / Source 在
  字面或正则过滤下黄粗高亮匹配段；过滤空或坏正则 → 无高亮。底栏 Search 仍走字面。
- **导航**: 「▲ 上一条」「▼ 下一条」在已过滤 `Entries` 间跳转；端点禁用不环绕；
  导航暂停 Follow；选中滚入视图；状态行 `匹配 N/M`。
- 级别 chip / 会话过滤 / Follow / 导出 / 过滤持久化保持。
- 176 tests green；worktree `/workspace/TerminalHub-logs2`；工具 Claude Code · GLM-5.3。

### pending / next Deploy slice — 多发布配置档 + 最近产物 (`feat/deploy-profiles-recent`)
- **配置档**: Deploy 坞按钮右键可保存 / 切换 / 删除命名发布配置。每条含名称、
  可选仓库根目录（空 = 仍从当前工作目录向上查找脚本和产物）、可选 RID
  （`linux-x64` → 已有 `scripts/publish-linux.sh`，`win-x64` → 已有
  `scripts/publish-windows.ps1`，空 = 当前系统）、备注、`CreatedAt` / `LastUsedAt`。
  写入真实 `settings.json`（`AppSettings.PublishProfiles` + `ActivePublishProfileId`），
  与 SSH hosts 同一 `SettingsStore`。切换或保存后，下一次坞按钮 Deploy
  （Ctrl+点击和「重新打包」同样走这条路径）使用该配置；调用方若传入显式起始目录
  （测试 / 编程调用）则跳过配置档，避免改变 PR #12 的显式路径语义。
  `LastUsedAt` 在切换和实际 Deploy 时更新，菜单按它从新到旧排列。
- **最近产物**: 右键「最近产物」从 `artifacts/publish` 读真实目录（路径、RID 文件夹名、
  直接子文件的最新 mtime、这些文件的大小合计），按 mtime 从新到旧。点击条目沿用
  原来的文件管理器打开。菜单每次打开都重扫；打包成功（`ReportPublishExit` exit 0）
  再扫并把结果写进 Output。没有产物时显示「暂无产物」，没有示例行。
- `PublishPlanner` / `ArtifactLocator` 行为保持，配置和最近列表是组合层。
  脚本仍只发布它们写死的 RID，配置档只是在两个已有脚本之间选择。
- 单测覆盖配置档往返、切换 / 删除 / 根目录与 RID 生效、最近产物空目录与 mtime 排序，
  以及坞菜单接线。146 tests green。

### PR #21 — 工具栏 CWD 真动作 ⟳/←/→ (`feat/toolbar-cwd-nav`)
- **真实 CWD 链路**:`IPtySession.ProcessId`(Linux forkpty 子 pid /
  ConPTY process id / Mock null)+ `ProcessCwd.TryRead`(Linux 读
  `/proc/<pid>/cwd`,读不到回退会话记录值);同时解析 **OSC 7**
  (`VtParser.TryParseOsc7`:file://host/path 与裸绝对路径)→
  `ScreenBuffer.Cwd`/`CwdChanged` → 路径条 + 历史。
- **轮询**(本 PR 补):bash 默认不发 OSC 7 —— `OnSampled`(1s tick)里
  `PollCwdChanges` 对每个会话读 `/proc` CWD,变化即
  `OnSessionCwdReported`(规范化 + push 历史 + 活动会话刷新路径条)。
  历史栈访问全程 `_cwdLock`(UI/PTY/monitor 三线程都会碰)。
- **← / → / ⟳**:per-session `CwdHistory` 栈(浏览器语义:新路径截断
  forward);按钮绑定 `CwdBackCommand`/`CwdForwardCommand`/
  `RefreshCwdCommand`,`CanCwdBack/CanCwdForward` 驱动禁用态;←/→ 真往
  shell 发 `cd '<path>'`,⟳ 重读 /proc 同步路径条 + Files 面板
  (`NavigateTo`)——不写死假路径。
- **会话隔离**:history 按 session id 存,会话切换/关闭各自跟随;
  `cd` 后 ~1s 内路径条自动更新。
- **验证**:152 tests green(GLM +8:CwdHistory push/back/forward/规范化、
  OSC7→历史→Files 联动、⟳ 回退、按钮绑定;本 PR 修 1 个异步断言等待)。
  DISPLAY=:7 实机:`cd /tmp` → 路径条 `~/tmp` 自动变 → ← 发
  `cd '/home/box'` → → 发 `cd '/tmp'` → ⟳ 一致。截图
  `toolbar-cwd-nav.png`。

### PR #20 — 「↗ 在新窗口打开」会话弹出独立窗 (`feat/open-new-window`)
- **真弹出**: 工具栏空壳按钮接上 `OpenInNewWindowCommand` —— 把当前（或选中）
  会话 detach 出主列表,弹出为独立 Avalonia `SessionWindow`,含完整
  TerminalView（键盘输入/渲染/滚轮/光标闪烁照常;OSC title 实时进窗口标题）。
- **detach/reattach 语义**: `SessionManager.Detach` 移出 `Sessions` 并发
  `SessionRemoved`(Output 订阅、卡片、Logs 过滤、分屏窗格全部走既有清理路径)
  但**不杀 PTY、不 dispose**;子窗关闭(「↩ 收回」按钮 / Esc / 窗口 ✕)经
  `Reattach` 回列表并激活 —— 弹出期产生的输出原样带回,缩略图照旧。
  弹出/收回各写一条 Output(source `window`)。
- **生命周期兜底**: `DetachedSessions`/`Popouts` 簿记;主窗 Dispose 先杀弹出
  PTY 再关子窗,不留孤儿进程;无会话时按钮 CanExecute 禁用。弹窗层叠在主窗
  +160,+110。
- **与分屏兼容**: 弹出分屏窗格里的会话 → 窗格自动回退其它会话(复用其
  `OnSessionRemoved` 修正),收回后回卡片列表。
- **验证**: 149 tests green(+5 `PopoutWindowTests`:Core detach 保活/reattach
  激活/幂等,headless 开窗分离+TerminalView 绑同一 Emulator、关窗收回且
  其它会话不受影响、空列表禁用、主窗关闭连带处置弹出 PTY)。DISPLAY=:7
  实机:Terminal 03 弹出独立窗跑 `echo POPOUT_$((40+2))_OK`→`POPOUT_42_OK`,
  「↩ 收回」后卡片/tab 复活、缓冲含弹出期输出、Output 双行日志。
- 截图 `docs/screenshots/open-new-window.png`。

### PR #18 — 「◫ 分屏」真双会话并排 (`feat/split-pane`)
- **真分屏**:中部视口拆为 `GridSplitter` 左右双栏(可拖),各绑
  `LeftPane`/`RightPane` 的 Emulator —— 两个会话、两套 PTY、两套
  ANSI 解析器,输入零串扰;工具栏「◫ 分屏」接 `ToggleSplitCommand`,
  IsSplit 时按钮着色,再点回单视图。
- **会话选取**:进分屏 左=当前活动会话、右=下一个会话;仅一个会话时自动
  新建第二个(不多建);会话被关闭时所属窗格自动换成幸存会话,双空自动退出。
- **聚焦模型**:点窗格 → `FocusPane` → 该窗格会话成为 `ActiveSession`
  (中部输入行/Output/Logs/Search/Breadcrumb 自动跟随);分屏中点左侧
  会话卡 → 分配到聚焦的窗格,两侧保持不同会话;聚焦窗格霓虹描边。
- **验证**:140 tests green(+6 `SplitPaneTests`:进出/双会话独立
  Emulator/`LFT`/`RGT` 输入互不出现/聚焦切换/卡片分配/单会话自动补+
  反复开关无泄漏/右栏关闭回落)。DISPLAY=:7 实机:双栏各 `echo`
  独立标记不串扰、焦点描边与会话卡激活联动、退出回单视图。
- 截图 `split-pane.png`。

### PR 待定 — Logs 级别筛选条 + 过滤持久化 (`feat/logs-level-filter-persist`)
- **级别筛选条 (chip bar)**: 级别 ComboBox（All Levels/info/warn/error）换成与
  「.* / ⬇ 写文件 / 📌 保留历史」同款暗色 chip 的**互斥选中条**（`#1A2030` 底 +
  `#2A3142` 边，选中 → `#38BDF8` 字 / `#3B82F6` 边，样式集中在 Logs Grid.Styles 的
  `ToggleButton.levelchip`）。VM 侧新增 `LevelAllSelected` / `LevelInfoSelected` /
  `LevelWarnSelected` / `LevelErrorSelected` 四个镜像属性：getter 读
  `LevelFilterIndex`，setter 选中即设索引 —— 过滤逻辑零改动（仍是 0/1/2/3 索引）；
  点已选中的 chip 只会重新断言选中（`SelectLevelChip` 推回通知），条上**永远恰好
  一个选中**，不会出现全不选。会话下拉挪到 chip 条下一行整宽，不再和级别挤一行。
- **过滤持久化**: `AppSettings` 新增 `LogsFilterText` / `LogsUseRegex` /
  `LogsLevelFilterIndex` / `LogsRetainHistoryOnClear`（默认 ""/false/0/false）。
  `LogsViewModel` 加 `persistFilters` 回调（对齐 `persistFileLogging` 风格），
  FilterText/UseRegex/LevelFilterIndex/RetainHistoryOnClear 任一变化即回调 →
  MainWindowViewModel 写回 `_settings` + `SaveSettingsInternal()`（小 JSON，逐次
  保存，不做防抖）。启动时 `ApplyPersistedFilters(...)` 回放上次的选择，level 索引
  `Math.Clamp` 到 0..3；回放期间 `_restoringFilters` 抑制回调 —— **加载绝不触发
  保存**，无 load→save 循环。`SessionFilterIndex` 不持久化（会话名是动态的）。
- **验证**: 138 tests green（本 PR +4：chip 互斥/点击已选中不断言 + 过滤仍生效、
  回放 clamp 且回放不写回 + 变化逐次回调、SettingsStore 落盘→重启新 VM 恢复 +
  越界索引 clamp、真实 MainWindow 端到端 —— 4 个 `levelchip` ToggleButton 双向
  绑定驱动 `LevelFilterIndex` 并真过滤列表、会话 ComboBox 仍在）。UI e2e 结束时
  把过滤器复位，避免测试把共享 `~/.config/terminalhub/settings.json` 留脏。
  截图 `logs-level-chips.png`（「error」chip 选中蓝高亮，列表只剩 error 行）。

### PR #15 — 整窗 mockup 走查 + 缺口修复 (`feat/walkthrough-polish`)
- **缩略图再加密**: 12 行（原 10),`MinHeight` 80→92、行高收紧 —— 卡片更像
  mockup 的迷你终端，保留每行 ANSI 主色。
- **消空壳**: Codex 面板右上 ⚙ 接上设置抽屉（`DockSelectCommand 5`),
  不再是无动作装饰。
- **全 tab 实机走查**(DISPLAY=:7, 1280×800):Processes /proc 表、Files 面包屑
  +目录列表、Logs 过滤+级别+来源标签、SSH 表单+持久化 host、Codex 清单/建议/
  输入、Output 真流、Debug `␛[` 原始行、Problems `ls` 错误→徽章 1→清空、
  Search `nonexistent`→2 命中黄高亮 —— 全部为真实数据，无 stub 回潮。
- 标题栏/状态栏/中部进度行对照 ai-assistant ref 复核，均在位（OS 感知标签、
  工作空间·N 终端·M 运行中·CPU/内存 sparkline、N/M 任务 · x%)。
- 截图 `walkthrough-dashboard.png`、`walkthrough-ai.png`;127 tests green。

### PR #16 — Logs 跟随尾部 + 导出可见行 (`feat/logs-follow-export`)
- **自动跟随尾部 (Follow)**: `LogsViewModel.FollowTail`（默认开 = 列表钉在最新行）。
  视图侧取 ListBox 内层 `ScrollViewer`，在 `ScrollChanged` 里按事件语义分流：**只有位移
  变、extent 不变**的事件（滚轮/拖动/键盘）才算用户意图 —— 离开底部 →
  `UpdateFollowFromScroll(false)` 暂停跟随，回到底部自动恢复；**extent 增长**（新行
  到达）且正在跟随时在事件内同步 `ScrollToEnd()`（此刻 extent 已含新行，落在真实底部，
  不是陈旧 extent 上的假滚动）。暂停时列表右下角浮现「⬇ 跟随」悬浮按钮
  （`FollowPaused` 通知属性驱动 IsVisible；特意不用 `!` 反向绑定 —— headless 下反向
  绑定不随变更刷新）→ `ResumeFollowCommand` 恢复并滚到底。新行到来绝不悄悄恢复跟随。
- **导出可见行**: 工具行新增「⬇ 导出」，把当前过滤后的 `Entries` 一次性写出（与复制
  同格式 `HH:mm:ss [level] (source) msg`）。桌面走 StorageProvider SaveFileDialog
  （建议名 `export-<ts>.log`，可选 .log/.txt；取消 → 状态行「已取消导出」）；无窗口
  （headless/自动化）、picker 抛错或 10s 无响应（Linux X11 无 xdg-desktop-portal 时
  DBus 调用会永久挂起，实测）→ 回落 `~/.config/terminalhub/logs/export-<ts>.log`，
  状态行「已导出 N 行 → 路径」。与「⬇ 写文件」实时 sink（`terminalhub-*.log`）完全
  独立。工具行 StackPanel → WrapPanel（352px 右栏内 4 个按钮自动换行不裁切）。
- **验证**: 134 tests green（本 PR +7：Follow 状态机 VM 层默认/上滚暂停/新行不打扰/
  回底恢复、ResumeFollow、导出仅含过滤行/空列表提示/picker 路径与取消、真实
  MainWindow 端到端 ×2 —— 导出 e2e headless 走完整链路（按钮绑定 → 命令 → 落盘断言
  内容）；`ScrollChanged` 在 headless 平台不触发，滚动→暂停→恢复的视图接线在
  DISPLAY=:7 实机验证：上滚暂停、按钮浮现、再进 60 行视口纹丝不动、点「⬇ 跟随」跳回
  FOLLOW_120 底部、后续行自动钉底。截图 `logs-follow-paused.png`（暂停态 + 悬浮按钮）、
  `logs-follow-resumed.png`（恢复后钉在最新行）。备注：实机期间另一 worktree 实例
  共享 :7、完全叠窗，坐标点击会串窗 —— 端到端断言一律以 headless 测试为准。


### PR #14 — 视觉打磨对照 mockup (`feat/visual-polish`)
- 会话卡片：tag pill 改为着色底（`TagPillBrush` = 30% 透明度 tag 色，对照
  mockup 蓝/绿/粉药丸）；active 卡片加强霓虹（`#38BDF8` 边 + `0 0 22` 发光 +
  更亮底色 `#E61A2233`)；名称字号/间距微调贴近参考卡片。
- Tab chrome：右栏与底栏 tab 选中态改为 mockup 式蓝色 pill（`#3B82F6` 白字），
  与 ai-assistant 参考图 Codex/Output 选中态一致；中央会话 tab 保持浮起卡片。
- 中部：进度行改为「N / M 任务 · x%」(`Assistant.ProgressLabel`，仍由真实
  清单驱动）；输入框与发送键圆角加大、蓝色发送钮贴近参考。
- 半透明面板保持 `#66/#CC` 玻璃感；非 Windows 仍关闭 Acrylic（无黑屏回归）。
- 实机截图：`dashboard-vs-ref.png`(tag pills + 霓虹 active + 蓝 pill tabs +
  67% 进度）、`codex-layout-vs-ref.png`(Codex tab 蓝 pill + 清单/建议/输入）。
- 127 tests green,无新增假数据/占位。

### PR #13 — Codex 本地助手 + Problems 真实计数 (`feat/codex-problems`)
- **LocalAiAssistant** 替换 MockAiAssistant（删掉定时器假进度）：确定性规则
  回复、无网络/付费 API。清单是真实可变状态源 —— `ProgressPercent =
  done/total` 直接驱动中部进度条；`Changed` 事件刷新 UI。
- **任务清单三态**: done/active/pending；点击行即可 Done↔Pending 勾选；
  无 active 时新加入项自动激活。中部进度条（`Assistant.ProgressPercent`）
  与清单实时联动。
- **建议任务**: 卡片整卡可点 → `RunSuggestionAsync` 追加清单项（pending，
  空档时激活）+ Output 记录 `Codex: 已将「…」加入任务清单`。
- **NL 输入双入口**: 右栏「描述你想做的任务…」+ 中部「输入命令，或 ? 开头
  向 Codex 提问…」(`?`/`ai:` 前缀走助手，其余仍是 shell 命令）。回复追加到
  Codex 面板消息区（你/Codex 行）与 Output（`Codex 收到任务` + `Codex:` 行），
  关键词命中本地模板（test/deploy/ssh/日志/文件/重构），否则按标点拆成 ≤4 步
  加入清单。
- **Problems 真计数**: `LineClassifier`(Core.GeneratedRegex）启发式分类：
  error/exception/fatal/failed/command not found/permission denied/no such
  file/`exit code 2`/`exit_code_1`/`exited with 3`/中文错误词 → error;
  warn/deprecated → warn。Problems 只收 error 级，徽章=真实条数、0 时隐藏；
  Problems 面板有计数文案 + 「清空」按钮（`ClearProblems`）+ 空态提示。
- 验证（DISPLAY=:7 实机）: Codex 建议点击→清单+1+消息；右栏输入
  "deploy the app"→清单+1+模板回复；中部 `? list files`→清单+1；勾选
  「读取项目结构」done→pending；`ls /nonexistent` → Problems 徽章=1 →
  清空归零隐藏。截图 `codex-panel.png`、`problems-list.png`、
  `problems-cleared.png`。
- 110 tests green（+23:LocalAiAssistant 提交/拆解/建议/激活/勾选进度、
  LineClassifier 13 例、headless Problems 计数清空、Codex 提交/建议/勾选、
  `?` 路由）。

### PR #1 — Solution skeleton (`feat/solution-skeleton`) ✅ merged
- Stack chosen: **Avalonia 11** (`net8.0`) — WinUI 3/WPF cannot compile on the
  Linux dev box; Avalonia still ships as a normal Windows desktop app with a
  Windows packaging path (PRODUCT.md fallback option).
- Projects: `TerminalHub.Core` (contracts/logic), `TerminalHub.Pty`
  (ConPTY + forkpty), `TerminalHub.App` (Avalonia UI), `TerminalHub.Tests`.
- `IPtySession` abstraction; `MockPtySession`; `AppSettings`/`SettingsStore`
  (JSON in `%APPDATA%/TerminalHub` / `~/.config/terminalhub`); `SystemMonitor`
  (CPU/Mem/Disk/Net + top processes — Windows P/Invoke + Linux /proc);
  `SessionManager` with 开发/测试/部署/Codex tags; `IAiAssistant` + mock.
- Single-instance mutex; `--mock` flag.
- Tests: 6 smoke tests green.

### PR #2 — Terminal emulation core (`feat/terminal-core`) ✅ merged
- `VtParser`: CSI/OSC/DCS state machine — SGR 16/256/truecolor, DECSTBM scroll
  regions, alt screen (1049), DEC line-drawing charset, incremental UTF-8,
  DA/DSR responses, origin/insert modes, pending-wrap.
- `ScreenBuffer`: scrollback, CJK double-width cells, dirty tracking, resize,
  `TailText`/`RowText` for thumbnails.
- `TerminalEmulator`: IPtySession ↔ buffer, resize plumbing, input.
- `LinuxPtySession` rewritten for post-fork safety (pre-allocated argv/envp,
  pre-warmed stubs, child calls only chdir/execve/kill) — fixed test-host
  deadlock/crash.
- Tests: 34 green incl. real forkpty echo/cwd.

### PR #3 — Shell chrome (`feat/dashboard-ui`) ✅ merged
- Full window matching both mockups; verified via headless-rendered PNGs:
  - Title bar: Terminal Hub / 终端控制中心 + ⚙ — ▢ ✕, "Windows System"
  - Left: session cards (status dot, tag pills, live previews, active glow), + 新建终端 Ctrl N
  - Center: traffic lights + tab strip, toolbar (⟳ → breadcrumb, ◫ 分屏,
    ↗ 在新窗口打开, ⋯), TerminalView, assistant progress bar,
    "Ask Codex or type a command…" input, Output/Debug/Problems(2)/Search panel
  - Right: Processes/Files/Logs/SSH/Codex tabs; live PID/NAME/CPU/MEM table;
    System Monitor (CPU/Mem sparklines, Disk bar, Net ↓↑ dual sparkline);
    Codex checklist (done/active/pending) + 建议任务 + 描述你想做的任务…
  - Floating dock: New Session(glow)/Monitor/SSH/Logs/Deploy(stub)/Settings
  - Status bar: 工作空间 · N 个终端 · M 运行中 · CPU · 内存 · sparkline
- Headless UI test renders both dashboard + Codex frames to PNG.
- Real-PTY end-to-end UI test: bash echo → screen buffer (36 tests green).

### PR #10 — Logs deep session buffer + live session filter (`feat/logs-session-buffer`)
- **深会话缓冲**: `LogsViewModel` 维护自己的环形缓冲
  (`DefaultBufferCapacity = 2000`，构造参数可调)，不再依赖 Output 面板 500 行
  展示上限；所有过滤（文本/级别/会话/正则）都在缓冲上重放 —— Output 已淘汰
  的行在 Logs 仍可搜到（测试：追加 520 行后过滤首行命中，而 OutputLog 已无该行）。
  缓冲淘汰最老条目时同步修剪显示列表。
- **Output 清空策略**（明确并已测）：默认跟随清空（Logs 缓冲与视图一并清空，
  清空后重放不复活旧行）；Logs 工具行新增「📌 保留历史」开关，开启后 Output
  Clear 时 Logs 保留缓冲历史，后续行继续追加。Logs 自身的「清空结果」不受该开关影响。
- **会话列表实时刷新**: `SessionCards.CollectionChanged → Logs.RefreshSessions()`，
  新建/关闭终端即时更新会话下拉（此前仅切到 Logs tab 时刷新）；新增
  `RenameSessionCommand`（改名同步 Output 来源名 `_sessionNames` 映射 + Logs 下拉；
  tab 条重命名 UI 留待后续，命令路径已可用并测试）。
- **UX**: 「⧉ 复制可见行」（格式 `HH:mm:ss [level] (source) msg`，剪贴板经
  `MainWindowViewModel` 注入，完成显示「已复制 N 行」）、「✕ 清空结果」（从缓冲
  移除当前匹配行，重放不再复活）、「.*」正则开关（忽略大小写 + 250ms 匹配超时
  防灾难回溯；坏正则红色提示、零匹配、不崩溃）。
- 验证（DISPLAY=:7 实机 + xdotool）：正则 `item-\d+` 过滤 20 行输出即刻生效；
  面板布局如截图。截图 `docs/screenshots/logs-deep-buffer.png`。
- 79 tests green（LogsPanelTests 14 个：深缓冲/容量/重放/清空两策略/正则好坏模式/
  复制/清空/实时会话名增改删）。

### PR #12 — Deploy dock one-click publish (`feat/deploy-run-publish`)
- **Deploy dock** runs the platform publish script, in addition to opening artifacts:
  - Plain click, artifacts present → same as PR #9 (list files, open the folder in
    the file manager) plus an Output hint for how to republish.
  - Plain click, nothing published → new terminal session named `Publish`, tagged
    `部署控制` (`SessionTag.Deploy`), running `./scripts/publish-linux.sh`
    (Linux/macOS, via `bash`) or `pwsh`/`powershell -File scripts\publish-windows.ps1`
    (Windows; pwsh preferred when it is on PATH).
  - **重新打包**: hold Ctrl and click Deploy, or right-click the button →
    「重新打包 Republish」. That always starts the publish session, even when
    artifacts already exist. Stale outputs are not auto-detected — use this path
    to rebuild. A second click while that session is still running does not
    spawn another one.
- Output lines use source `deploy`: 开始打包 / publish start, 打包成功 / publish
  succeeded, 打包失败 / publish failed (exit code). The bottom panel switches to
  the Output tab for these.
- `PublishPlanner` in Core decides open-vs-publish and locates the script by
  walking up from the working directory. `ArtifactLocator` is unchanged.
- Tooltip on the dock button states the same click / Ctrl / right-click rules.
- 实机验证（DISPLAY=:7): 无产物点击 → `Publish` 会话（部署控制 tag）真实运行
  `./scripts/publish-linux.sh`(restore/编译/输出 86MB TerminalHub）；产物出现后
  点击 → 打开目录不重复起会话；publish 运行中二次点击不产生第二个会话；
  Ctrl+点击 → 再起一个 Publish 会话。截图 `deploy-publish-run.png`、
  `deploy-republish.png`。

### PR #9 — Deploy action + colored thumbnails (`feat/deploy-thumbnails`)
- **Deploy dock button** is real now (was stub):
  `ArtifactLocator` walks up from CWD to find `artifacts/publish/<rid>/`
  with files → lists each artifact (name + size) in Output and opens the
  folder in the OS file manager (`UseShellExecute`); when nothing is
  published it prints the publish commands for both platforms instead.
- **Session-card thumbnails** now render per-line dominant foreground
  colors: `ScreenBuffer.TailLines` + `TerminalColor.ToRgbHex`
  (xterm 256-palette + truecolor) → each preview line gets its own brush —
  much closer to the mockup's colored mini-terminals.
- Verified live (DISPLAY=:7): `echo -e '\e[32m…\e[35m…'` + `ls --color`
  show green/magenta/blue preview lines in the card. Deploy path verified
  via headless test driving `DockSelectCommand` (artifacts dir exists here).
- 70 tests green. Note: on this 1280x800 box the 1440x900 window's bottom
  ~100px clips under xfwm4 — dock stays usable on normal displays.
- `docs/screenshots/deploy-thumbnails.png`.

### PR #8 — SSH panel: saved hosts + ssh sessions (`feat/ssh-panel`)
- Right-rail **SSH** tab: 新建/编辑连接 form (名称/用户/主机/端口 + validation)
  + saved-host list (显示 `ssh -p port user@host` command line) + per-row
  连接 / ✕ delete; selecting a row refills the form for editing.
- Hosts persist in `settings.json` (`AppSettings.SshHosts`, derived props
  `[JsonIgnore]`ed). Add-or-update dedups on same name or same target;
  different user on same host is a separate entry by design.
- **连接** spawns a real session: `SessionManager.CreateAsync` with
  `Shell="ssh"` + `Arguments="-p <port> <user@host>"`, tagged orange `SSH`.
  Verified live: `ssh -p 2222 tester@127.0.0.1` → real
  `ssh: connect to host 127.0.0.1 port 2222: Connection refused` in-terminal.
- Graceful degrade when `ssh` isn't on PATH (`SshLocator` → status text).
- Limits documented in-panel: auth (password/keys) happens inside the
  terminal itself; use ssh-agent / ~/.ssh/config for keys.
- `docs/screenshots/ssh-panel.png`. 61 tests green.
- Installed `openssh-client` on this box for real-binary verification.

### PR #7 — Logs panel + file sink (`feat/logs-panel`)
- Right-rail **Logs** tab over the real session stream: text filter + level
  filter (info/warn/error) + per-session filter (rebuilt on tab open);
  `LogEntry.Source` carries the session name, shown as `(Terminal 03)`.
- `⬇ 写文件` toggle → `SessionLogFile` writes
  `~/.config/terminalhub/logs/terminalhub-<ts>.log` (`HH:mm:ss.fff [level] (src) msg`),
  persisted via `AppSettings.SessionLogToFile`; status line shows the path.
- warn/error lines also fan out to the bottom **Problems** badge (verified:
  `warn_me`→warn, `ls: write error`→error, badge=3).
- Verified on DISPLAY=:7: typed `echo LOGTEST_99; ls /tmp|head -3; echo warn_me`,
  filtered "LOGTEST" → 2 entries; toggle wrote real file with subsequent lines.
- `docs/screenshots/logs-panel.png`. 51 tests green.

### PR #6 — Files panel + real Output stream (`feat/files-panel`)
- Right-rail **Files** tab is a real local browser: `↑` + clickable breadcrumb
  (`/ › home › box › .config`), dirs-first sorted listing (dirs blue `▸`,
  sizes via `BytesConverter`), hover/selected styling matching the monitor cards.
- **Double-click** dir → navigate; double-click file → preview pane
  (title + size/mtime meta + mono text); binary / >2 MB show explicit reasons
  (`PreviewKind.Text|Binary|TooLarge`, full `File.ReadAllBytes` read).
- First visit lands in the active session's cwd (⌂ returns anytime).
- `Core/Files/LocalFileBrowser` — pure-BCL service, unit tested.
- **Real Output stream**: `Utf8LineDecoder` (CRLF/lone-CR/progress-redraw +
  pending-CR across chunks) + `AnsiText.Strip` (OSC/CSI/charset) feed the
  bottom Output tab from live `IPtySession.OutputReceived`; warn/error lines
  fan out to Problems badge; fake Next.js seed log removed.
- Title bar shows real OS (`Linux System` on this box).
- Verified on DISPLAY=:7: browse `.config`, preview `mimeapps.list`,
  `echo OUT_OK_42` appears in terminal, thumbnail, AND Output tab.
- `docs/screenshots/files-panel.png` committed. 46 tests green.

### PR #5 — Linux-runnable build (`feat/linux-run`)
- **Verified real GUI on this box** (X.Org `:7`): `dotnet run` opens the window;
  three `forkpty` bash sessions; `xdotool` typed `echo HELLO_FROM_LINUX_$((40+2))`
  → `HELLO_FROM_LINUX_42` executed; thumbnails updated live; process table +
  CPU/Mem/Disk/Net widgets all real data.
- `scripts/run-linux.sh` — dep check (X11 libs) + `dotnet run`; `--headless`
  runs under Xvfb; `--mock` forces mock PTY.
- `scripts/publish-linux.sh` — self-contained `linux-x64` single-file publish
  (86 MB); **published binary verified launching its own GUI instance**.
- `docs/local-debugging.md` — 本机调试: deps, Xvfb/xdotool automation,
  headless frames, platform matrix.
- `docs/screenshots/` — real X11 captures committed (terminal + Codex views).
- Installed on box: `x11-apps` (xwd), `imagemagick` for capture; existing
  `xdotool`/`xvfb` used.

### PR #11 — dock/chrome polish + Debug/Search (`feat/dock-debug-search`)
- **Dock click fix (real bug)**: `DockSelectCommand` was `RelayCommand<int>` while
  XAML passes `CommandParameter="2"` as *string* → silent no-op since the dock
  shipped; every dock button was dead. Signature now `DockSelect(object?)` +
  `int.TryParse`. Added `ZIndex=100` on the pill for layering safety.
- Dock polish: `Button.dock` style + `:pointerover` + `dock-active` class bound to
  `DockHighlight` (neon-blue pill outline on the active surface); Monitor/SSH/Logs
  map to right tabs, Settings toggles the drawer (all verified clickable on
  DISPLAY=:7 — Settings opened, SSH switched, persisted hosts still there).
- **Debug tab** (was stub): `Utf8LineDecoder.RawLineReceived` emits pre-strip
  lines → `AnsiText.DebugEscape` (ESC→␛, BEL→␇, C0→^X, tab→⇥) → `DebugLog`
  (cap 300) with session source + ms timestamps.
- **Search tab** (was stub box): `SearchQuery` filters active session's
  scrollback+screen via `ScreenBuffer.SearchLines` (case-insensitive, capped 200,
  global line numbers); hits rendered with `HighlightTextBlock` (yellow-bold
  matches) + "N 处匹配 / 无匹配" status.
- Thumbnails denser: 10 lines, 6.5pt font, tighter line-height — keeps per-line
  ANSI dominant color.
- Window: `FitToScreen` clamps to working area at open; `AcrylicBlur` kept for
  Windows but forced off on non-Windows at runtime (no compositor → renders
  black).
- Verified live on DISPLAY=:7: SSH dock click → SSH tab (persisted host list),
  Debug shows `␛[01;34m…` raw lines incl. `MARKER_X7_21`, Search "MARKER" →
  2 hits highlighted. `dock-debug-search.png`, `debug-panel.png`.
- 78 tests green (+8: SearchLines, DebugEscape, RawLineReceived, dock nav incl.
  string param, Debug log, Search VM, dense preview).

### PR #4 — Packaging + docs (`feat/packaging-docs`)
- `packaging/TerminalHub.iss` — Inno Setup (x64, zh+en, desktop icon,
  single-instance-friendly uninstall/taskkill)
- `scripts/publish-windows.ps1` — `dotnet publish win-x64` single-file +
  iscc invocation
- README refreshed; PRODUCT.md checkboxes updated.

## Known gaps / Windows-only items
- `ConPtySession` compiles on Linux but only *runs* on Windows — needs a
  Windows smoke pass (`dotnet run --project src/TerminalHub.App`).
- Acrylic/Mica: approximated with translucent brushes; native Mica is a
  WinUI-only path (out of scope for Avalonia).
- Session thumbnails render text preview (not bitmap) — DESIGN.md allows this;
  now colored per-line dominant foreground + 10 lines.
- Codex 助手现为本地规则实现（LocalAiAssistant），无 LLM；接入真实模型
  需实现 IAiAssistant 并替换构造处（MainWindowViewModel）。
- Window bottom may clip ~80px under compositor-less X11/Xvfb at 800px screen
  height (status bar row hidden); dock remains usable, normal desktops unaffected.
