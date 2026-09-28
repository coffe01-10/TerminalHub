# Design — layout regions matching UI mockups

Visual references (copy of product mockups):

| File | View |
|------|------|
| [`ui-ref-dashboard.png`](ui-ref-dashboard.png) | Glass terminal hub: sessions · main terminal · process/system dashboard · bottom dock |
| [`ui-ref-ai-assistant.png`](ui-ref-ai-assistant.png) | 终端控制中心: tagged sessions · Codex/terminal · AI checklist · status bar |

## Visual language

- **Theme:** dark glassmorphism over deep navy/black (`#0A0D14` range)
- **Accents:** neon / cyan blue for selection, active dock items, progress
- **Surfaces:** frosted panels, thin borders, soft glow on focus
- **Type:** UI = Segoe UI / system sans; terminal = Cascadia Code / Consolas
- **Locale:** Chinese primary labels; English secondary OK

## Region map — Dashboard (`ui-ref-dashboard.png`)

```
┌─────────────────────────────────────────────────────────────────┐
│ Top chrome (optional OS-like status)                            │
├──────────┬──────────────────────────────────┬───────────────────┤
│ LEFT     │ CENTER                           │ RIGHT             │
│ Session  │ Main terminal viewport           │ Dashboard         │
│ thumbs   │  · title + cwd                   │  Processes/Files/ │
│ T01–T05  │  · ConPTY output                 │  Logs/SSH tabs   │
│ (glow on │  ├─────────────────────────────┤ │  Process table    │
│  active) │  │ Bottom panel: Output/Debug/ │ │  CPU Mem Disk Net │
│          │  │ Problems/Search             │ │  sparklines       │
├──────────┴──────────────────────────────────┴───────────────────┤
│ BOTTOM DOCK (floating): New · Monitor · SSH · Logs · Deploy · ⚙ │
└─────────────────────────────────────────────────────────────────┘
```

### Left — session thumbnails
- Vertical stack of mini live previews labeled Terminal 01…N
- Active session: thick glowing blue border
- Optional later: drag reorder

### Center — main terminal + bottom panel
- Session name + cwd path; window/pane controls
- Full terminal scrollback
- Tabs under viewport: **Output** (default), **Debug**, **Problems** (badge), **Search**
- Clear + level filter on Output

### Right — processes & system
- Tab strip: Processes · Files · Logs · SSH · +
- Process grid: PID | NAME | CPU% | MEM
- Widgets: CPU %, Memory used/total + sparkline, Disk used/total + bar, Network up/down + dual sparkline

### Bottom — action dock
- Floating translucent bar, six actions with icons
- **New Session** highlighted when primary
- Deploy may open stub dialog initially

## Region map — AI Assistant (`ui-ref-ai-assistant.png`)

```
┌─────────────────────────────────────────────────────────────────┐
│ Terminal Hub / 终端控制中心                          ⚙  _ □ ×   │
├──────────┬──────────────────────────────────┬───────────────────┤
│ LEFT     │ CENTER                           │ RIGHT             │
│ Sessions │ Tabs + breadcrumb C:>…           │ Codex | 文件 |    │
│ + tags   │ Terminal / Codex progress        │ 任务 | 搜索       │
│ 开发/测试│ Progress bar (e.g. 32%)          │ Checklist         │
│ /部署    │ Input: Ask Codex or command…     │ 建议任务 list     │
│ +新建终端│                                  │ NL: 描述你想做的… │
├──────────┴──────────────────────────────────┴───────────────────┤
│ Status: 工作空间 · N 个终端 · M 运行中 · CPU% · 内存 · sparkline │
└─────────────────────────────────────────────────────────────────┘
```

### Left — tagged sessions
- Cards with live thumb + status dot
- Tags: **开发环境** / **测试环境** / **部署控制**
- Button: **+ 新建终端** (Ctrl+N)

### Center — tabbed terminal / Codex stream
- Tab bar + breadcrumb
- Optional analysis progress + command input

### Right — AI assistant
- Status blurb + checklist (done / active / pending)
- Suggested tasks with icons
- Text area for natural-language tasks
- Backed by `IAiAssistant` stub in MVP

### Bottom status bar
- 工作空间 name · terminal counts · CPU · memory · mini sparkline

## Implementation notes

- Prefer acrylic/Mica/blur APIs on WinUI/WPF when available; approximate with semi-transparent brushes on Avalonia
- Sparklines: small in-memory ring buffers (e.g. last 60 samples)
- Session thumbnails: downsample recent terminal buffer to a WriteableBitmap / Skia surface periodically (throttle)
- Do not require pixel-perfect parity; layout regions and Chinese labels must be recognizable vs PNGs
