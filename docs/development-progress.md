# Development progress — Terminal Hub / 终端控制中心

> Autonomous build log. PRs merge to `main` when green.

## 2026-09-28

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
