# Product Requirements — Terminal Hub / 终端控制中心

**Name:** Terminal Hub / 终端控制中心  
**Owner:** 小陈 (coffe01-10 / Jinhong Chen)  
**Platform:** Windows 10/11 first-class — complete installable Windows desktop tool  
**Visual refs:** `docs/design/ui-ref-dashboard.png`, `docs/design/ui-ref-ai-assistant.png`

## Goals

Ship a usable Windows terminal control center MVP+ that closely matches the glassmorphic mockups: multi-session ConPTY host, process/system dashboard, bottom action dock, settings, installer script, and an AI assistant panel (stub/hook OK; no paid AI calls required).

## Core features (build iteratively until complete)

### 1. Multi-session terminal host
- ConPTY on Windows for real shells (pwsh / cmd / WSL configurable)
- Live session list with **mini previews** (thumbnail of recent output)
- Session tags: 开发 / 测试 / 部署 (as in AI-assistant mockup)
- Create / switch / close sessions; keyboard shortcut Ctrl+N for 新建终端

### 2. Main terminal viewport
- Tabbed sessions in center pane
- CWD breadcrumb (e.g. `C: > MangaFlow`)
- Split pane later if time; toolbar: refresh, back/forward, 分屏, 在新窗口打开
- Monospace terminal rendering with dark theme

### 3. Integrated bottom panel (under main terminal)
- Tabs: **Output** / **Debug** / **Problems** / **Search**
- Log levels, Clear, filter dropdown
- Problems badge count when issues exist

### 4. Right dashboard
- Tabs: **Processes** / **Files** / **Logs** / **SSH** (+ optional)
- Process table: PID · Name · CPU% · Mem
- System widgets with sparklines: **CPU** · **Mem** · **Disk** · **Network** (up/down)

### 5. Bottom action dock
- Floating glass dock: **New Session** · **Monitor** · **SSH** · **Logs** · **Deploy** · **Settings**
- Deploy may be stub initially
- Active item neon-blue glow

### 6. AI assistant side panel (Codex-like)
- Task checklist with progress states
- Suggested tasks list (建议任务)
- NL input: 「描述你想做的任务…」 / 「Ask Codex or type a command…」
- Start as UI + hook interface (`IAiAssistant`); real AI optional via local/CLI later
- **No paid provider calls** for MVP / CI

### 7. Visual design
- Dark glassmorphism, neon blue accents
- Acrylic / blur where platform allows
- Chinese primary UI labels (新建终端, 工作空间, 文件, 任务, …); English OK in code identifiers
- Match mockups closely (recognizable, not pixel-perfect)

### 8. Settings
- Shell path: pwsh / cmd / WSL
- Font family & size
- Theme (dark glass default)
- Startup sessions

### 9. Packaging & lifecycle
- MSIX **or** Inno Setup / NSIS installer script
- Single-instance guard
- Clean exit (dispose PTY, save settings)

## Non-goals (MVP)

- Paid cloud AI API calls
- Full remote SSH client feature-parity (basic SSH tab/stub OK)
- Cross-platform GUI polish on Linux/macOS (Linux = build/test/mock PTY only)

## Tech guidance

| Preference | Notes |
|---|---|
| WinUI 3 (Windows App SDK) | Best native Windows look |
| WPF (.NET 8 windows) | Solid ConPTY ecosystem |
| Avalonia 11 net8 | Fallback if WinUI/WPF blocked on Linux iteration; still ship Windows packaging |

- Abstract PTY: `IPtySession` — Windows ConPTY impl, Linux mock
- Commit often; open PRs to `main`; merge when green
- Autonomous iteration until MVP+ is usable

## Success criteria (MVP+)

- [x] Sessions create/switch/close with ConPTY (or documented Windows-only path + Linux mock)
- [x] Process monitor + CPU/Mem/Disk/Net widgets update live
- [x] Bottom dock + settings persist
- [x] AI panel UI + interface stub (no paid calls)
- [x] Installer script (Inno/NSIS or MSIX) checked in
- [x] README with Windows build/run/package steps
- [x] UI recognizable vs both design PNGs
