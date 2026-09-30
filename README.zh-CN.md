<p align="center">
  <img src="docs/readme/hero-zh.svg" width="1200" alt="Terminal Hub：多个终端，一个工作空间。支持实时预览、并排分屏和四种主题的原生终端工作空间。">
</p>

<p align="center"><a href="README.md">English</a> / <strong>简体中文</strong></p>

<p align="center">
  <a href="src/TerminalHub.App/TerminalHub.App.csproj"><img src="docs/readme/runtime.svg" height="24" alt=".NET 8"></a>
  <a href="src/TerminalHub.App"><img src="docs/readme/ui.svg" height="24" alt="Avalonia 11"></a>
  <a href="scripts/run-linux.sh"><img src="docs/readme/platform.svg" height="24" alt="Windows / Linux"></a>
  <a href="LICENSE"><img src="docs/readme/license.svg" height="24" alt="MIT License"></a>
</p>

<p align="center">
  <a href="#quick-start"><strong>开始使用 →</strong></a> &nbsp; · &nbsp;
  <a href="#workspace">探索工作空间</a> &nbsp; · &nbsp;
  <a href="#themes">选择你的主题</a> &nbsp; · &nbsp;
  <a href="#docs">开发文档</a>
</p>

开发服务、构建任务、随手备用的 Shell，**都在视线之内。** Terminal Hub 是一款 Windows 优先的原生终端，把实时会话预览、分屏与常用工具放进同一个工作空间。基于 Avalonia 与 .NET，Windows 和 Linux 均通过真实 PTY 运行 Shell。

<a href="docs/readme/workspace.png"><img src="docs/readme/workspace.png" width="1440" alt="深蓝玻璃主题：左侧实时会话缩略图、中央活动终端和底部操作 Dock"></a>

<p align="center"><sub>工作空间 · 深蓝玻璃<br>当前原生界面的 Avalonia Headless 渲染，使用 Mock PTY 与示例输出。<a href="docs/readme/README.md">素材来源</a></sub></p>

<a id="workspace"></a>

## 01 / 每个会话，都有自己的位置

切换之前，先看到它在做什么。需要操作时让它来到前台，暂时离开时留在侧栏，进程继续运行。

<table>
  <tr>
    <td width="50%" valign="top">
      <h3>看见正在发生的事</h3>
      <p>缩略图实时呈现终端内容。拖动卡片调整顺序，相邻会话平滑让位，松手后落入新位置。</p>
    </td>
    <td width="50%" valign="top">
      <h3>让当前任务来到前台</h3>
      <p>受 Dock 启发的展开过渡连接缩略图与主区域。需要更多空间时，也可以把会话弹出为独立窗口。</p>
    </td>
  </tr>
  <tr>
    <td width="50%" valign="top">
      <h3>工具留在手边</h3>
      <p>文件、日志、进程、SSH 和输出面板按需展开。底部 Dock 支持自动隐藏、始终显示和完全隐藏。</p>
    </td>
    <td width="50%" valign="top">
      <h3>回到熟悉的布局</h3>
      <p>记住会话顺序、名称、目录、Shell 与分屏状态。下次启动时恢复工作布局，并重新创建 Shell 进程。</p>
    </td>
  </tr>
</table>

### 两个终端，一个明确的焦点

点击 **「分屏」**，让两个会话并排工作。每个窗格都有名称和独立的输入目标。先点窗格，再点左侧缩略图，即可为它切换会话。左右切换时主区域保持静止，侧栏跟随焦点更新。

<a href="docs/readme/split.png"><img src="docs/readme/split.png" width="1440" alt="亮白主题分屏：两个具名终端并排显示，高亮边框标示当前窗格"></a>

<p align="center"><sub>并排工作 · 亮白<br>会话各自独立，名称始终可见，输入焦点一目了然。</sub></p>

<a id="themes"></a>

## 02 / 给工作空间，换一种心情

四种配色，从终端延伸到缩略图、菜单和设置。玻璃带着高光，深黑保留细描边，亮白拥有柔和阴影，纸张露出层叠的页边。

<table>
  <tr>
    <td width="50%"><a href="docs/readme/workspace.png"><img src="docs/readme/theme-glass.svg" width="580" alt="深蓝玻璃：高光与层次"></a><p align="center"><strong>深蓝玻璃</strong></p></td>
    <td width="50%"><a href="docs/readme/black.png"><img src="docs/readme/theme-black.svg" width="580" alt="深黑：克制与专注"></a><p align="center"><strong>深黑</strong></p></td>
  </tr>
  <tr>
    <td width="50%"><a href="docs/readme/split.png"><img src="docs/readme/theme-white.svg" width="580" alt="亮白：轻盈与留白"></a><p align="center"><strong>亮白</strong></p></td>
    <td width="50%"><a href="docs/readme/settings.png"><img src="docs/readme/theme-paper.svg" width="580" alt="纸张：暖色与叠页"></a><p align="center"><strong>纸张</strong></p></td>
  </tr>
</table>

<p align="center"><sub>主题配色示意 · 点击卡片查看对应的原生界面图。</sub></p>

选择喜欢的等宽字体、调整字号，再决定哪些面板常驻。分组设置把外观与工作空间选项放在一起，随手就能找到。

<details>
<summary><strong>走进设置</strong> — 展开纸张主题预览</summary>

![纸张主题设置：外观、工作空间、Dock 和字体组成统一的暖色分组面板](docs/readme/settings.png)

</details>

### 熟悉的按键，真实的 Shell

拖选后用 `Ctrl+Shift+C` 复制，`Ctrl+C` 留给程序中断。支持中文输入法定位、历史滚动、文本搜索和多行粘贴。应用启用鼠标协议时，按住 `Shift` 仍可使用终端自身的选择与滚动。

Claude Code、Codex CLI 等工具可以在 Shell 内单独安装和运行。已验证的协议与具体交互范围，见 [CLI 交互适配](docs/cli-compatibility.md)。

<a id="quick-start"></a>

## 03 / 从源码，到第一个会话

<p align="center">
  <img src="docs/readme/typing.svg" width="470" alt="逐字输入还原、构建与启动命令的终端示意动画">
</p>

> **v0.1.0 初版。** Windows 可[下载便携 ZIP](https://github.com/coffe01-10/TerminalHub/releases/tag/v0.1.0)，或按下面步骤从源码运行。程序尚未签名；Windows 是首要平台，Linux 日常验证相对较少，macOS 尚不是已验证目标。详见[版本说明](docs/releases/v0.1.0.md)。

### Windows 便携预览

开发者运行 `pwsh -File scripts/publish-windows.ps1 -SkipInstaller`，即可生成 `artifacts/TerminalHub-windows-x64-preview.zip`。解压整个文件夹后启动 `TerminalHub.exe`，无需安装 .NET SDK；包内附使用说明和许可证。缺少默认 Shell 时，应用会直接列出本机可用的 Shell 供选择。

会话卡会提示后台新输出，并显示终端进程的退出码；打开对应会话后清除新输出提示。分屏中可见的两侧均视为正在查看。

### Windows

需要 **Windows 10 1809 或更高版本 / Windows 11**、Git 和 **.NET 8 SDK**。默认启动 Shell 为 PowerShell 7，请确保 `pwsh` 在 PATH 中。

```powershell
git clone https://github.com/coffe01-10/TerminalHub.git
cd TerminalHub
dotnet restore TerminalHub.sln
dotnet run --project src/TerminalHub.App
```

没有安装 `pwsh` 时，首次启动直接选择 Windows PowerShell 或命令提示符，再点击「开始使用」。WSL、自定义 Shell 和 SSH 连接需要相应程序已在本机安装，WSL 还需配置 Linux 发行版。

### Linux

需要 **.NET 8 SDK**、Git、可用的 X11 显示环境及字体依赖。Debian / Ubuntu 上的界面依赖可以用以下命令安装：

```bash
sudo apt-get install libx11-6 libxcb1 libfontconfig1 libice6 libsm6 fonts-noto-cjk
git clone https://github.com/coffe01-10/TerminalHub.git
cd TerminalHub
bash scripts/run-linux.sh
```

默认会话同样使用 `pwsh`；若只安装了 Bash，首次启动直接选择 Bash。无实体显示器时，安装 Xvfb 后可运行 `bash scripts/run-linux.sh --headless`。

### 第一次使用

1. 点击一个终端，在提示符后执行 `echo Hello Terminal Hub`，应在当前终端及其缩略图中看到输出。
2. 使用 `Ctrl+Shift+N` 新建会话，点击缩略图切换；原会话中的进程继续运行。
3. 点击「分屏」，尝试在两侧分别输入；通过窗格名称与高亮边框确认当前会话。

工作区恢复会创建新进程，不会恢复上次程序的运行进度。应用采用单实例运行。重新编译后，请先正常退出旧实例，再启动新版本。

<a id="shortcuts"></a>

## 04 / 让手留在键盘上

| 操作 | 快捷键 |
| --- | --- |
| 新建 / 关闭当前会话 | `Ctrl+Shift+N` / `Ctrl+Shift+W` |
| 下一个 / 上一个会话 | `Ctrl+Tab` / `Ctrl+Shift+Tab` |
| 显示或隐藏工具栏 / 输出区 | `Ctrl+Shift+B` / `Ctrl+Shift+J` |
| 复制选中文字 | `Ctrl+Shift+C` |
| 粘贴 | `Ctrl+V`、`Ctrl+Shift+V` 或 `Shift+Insert` |
| 放大 / 缩小 / 重置字号 | `Ctrl+=` / `Ctrl+-` / `Ctrl+0` |
| 重命名会话 | 会话列表聚焦时按 `F2`，或双击会话标题 |

终端聚焦时，`F2` 交给终端内的程序。关闭会话会结束其进程。

<details>
<summary>配置保存在哪里？</summary>

| 平台 | 默认配置文件 |
| --- | --- |
| Windows | `%APPDATA%\TerminalHub\settings.json` |
| Linux | `${XDG_CONFIG_HOME:-~/.config}/terminalhub/settings.json` |

设置和工作区布局保存在本机。界面中修改 Shell 会影响之后新建的终端；已有会话继续使用原来的进程。

</details>

<a id="development"></a>

## 05 / 继续构建

项目采用 **Avalonia 11 + .NET 8**。Windows 使用 ConPTY，Linux 使用 `forkpty`；终端解析、屏幕缓冲和会话管理位于独立的 Core 项目。

```text
src/
├── TerminalHub.App     原生界面、终端绘制、输入法与工作区
├── TerminalHub.Core    VT 解析、屏幕缓冲、会话与设置
└── TerminalHub.Pty     Windows ConPTY、Linux PTY、Mock PTY
tests/
└── TerminalHub.Tests   核心逻辑、原生布局与交互回归
```

```powershell
dotnet build TerminalHub.sln -c Debug
dotnet test tests/TerminalHub.Tests/TerminalHub.Tests.csproj
```

<details>
<summary>生成可分发版本</summary>

Windows x64 发布脚本需要 PowerShell 7。先生成包含 .NET 运行时的应用：

```powershell
pwsh -File scripts/publish-windows.ps1 -SkipInstaller
```

输出目录为 `artifacts/publish/win-x64/`。安装 Inno Setup 6 后，去掉 `-SkipInstaller` 即可同时生成安装程序，输出到 `artifacts/installer/`。安装程序会结束正在运行的 Terminal Hub，使用前请先保存工作并正常退出。

Linux x64：

```bash
bash scripts/publish-linux.sh
```

输出目录为 `artifacts/publish/linux-x64/`。自包含发布携带 .NET 运行时，Linux 上仍需要相应的图形系统库。

</details>

<a id="docs"></a>

### 深入了解与参与贡献

- [开发指引](AGENTS.md)：代码入口、已知问题背景与验证方式。
- [CLI 交互适配](docs/cli-compatibility.md)：粘贴、鼠标、快捷键和兼容性验证范围。
- [Linux 调试记录](docs/local-debugging.md)：X11、Xvfb 与本机调试；历史界面描述以当前源码为准。
- [产品说明](docs/PRODUCT.md)：设计背景与需求记录。

欢迎通过 [Issues](https://github.com/coffe01-10/TerminalHub/issues) 提交问题或改进建议。终端显示与输入问题请附上系统、Shell / CLI 版本、复现步骤和截图；贡献代码时请为修复的具体行为补充或运行相关回归。

当前开发重点包括 Unicode 字素与格宽、光标与输入法定位，以及多会话性能。

---

<p align="center">
  <img src="docs/readme/logo.svg" width="240" alt="Terminal Hub">
</p>
<p align="center"><strong>把窗口留给任务，把注意力留给工作。</strong></p>
<p align="center"><a href="#quick-start">开始使用</a> · <a href="https://github.com/coffe01-10/TerminalHub/issues">反馈问题</a> · <a href="LICENSE">MIT 许可证</a></p>
<p align="center"><sub>Copyright © 2026 Jinhong Chen (coffe01-10)</sub></p>
