# Development progress — Terminal Hub / 终端控制中心

> Autonomous build log. PRs merge to `main` when green.

## 2026-09-28

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
- Session thumbnails render text preview (not bitmap) — DESIGN.md allows this.
- Deploy + SSH + Files tabs are stubs per MVP scope.
