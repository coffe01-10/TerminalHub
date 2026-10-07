<p align="center">
  <picture>
    <source media="(max-width: 600px)" srcset="docs/readme/hero-zh-compact.svg">
    <img src="docs/readme/hero-zh.svg" width="1200" alt="Terminal Hub：让 AI 写代码，你掌控全局。专为 AI CLI 打造的原生终端工作台。">
  </picture>
</p>

<p align="center"><a href="README.md">English</a> / <strong>简体中文</strong></p>
<p align="center"><strong>AI 会话、开发服务和测试，同在一个工作空间。</strong></p>

<p align="center">
  <a href="src/TerminalHub.App/TerminalHub.App.csproj"><img src="docs/readme/runtime.svg" height="24" alt=".NET 8"></a>
  <a href="src/TerminalHub.App"><img src="docs/readme/ui.svg" height="24" alt="Avalonia 11"></a>
  <a href="scripts/run-linux.sh"><img src="docs/readme/platform.svg" height="24" alt="Windows / Linux"></a>
  <a href="LICENSE"><img src="docs/readme/license.svg" height="24" alt="MIT 许可证"></a>
</p>

<p align="center">
  <a href="#demo">观看宣传片</a> &nbsp; · &nbsp;
  <a href="#quick-start">开始使用</a> &nbsp; · &nbsp;
  <a href="#plugins">官方扩展</a> &nbsp; · &nbsp;
  <a href="https://github.com/coffe01-10/TerminalHub/releases">下载</a>
</p>

Claude Code 正在改代码，Codex 在处理另一个任务，开发服务和测试还在后台运行。窗口越来越多，你真正需要的是看清进展，并在需要时接手。

**Terminal Hub 把这些独立终端收进一个按项目组织的原生工作台。** 从会话预览找到目标，分屏对照输出，把常用工具留在手边，让注意力回到正在做的事。

<a id="demo"></a>

## 66 秒，看看你的工作台

https://github.com/user-attachments/assets/5c55badb-4a9a-4e93-874c-4f8be8a4e3d7

<p align="center"><sub>v0.4.1 宣传片 · 从多窗口到同一工作台 · 动画展示产品功能与交互。</sub></p>

<a id="features"></a>

## 后台的进展，一眼可见

左侧会话栏持续显示终端内容的缩略预览，新输出提示和退出码帮助你发现变化。AI 会话、开发服务、测试与 SSH 各有自己的位置，点击预览即可回到对应终端，接着操作。

按项目建立工作区，给会话命名、分组或置顶。切换工作区时，后台进程继续运行；重要输出可以通过历史、搜索和日志重新找到。

## 任意嵌套分屏，进程保持运行

一边让 AI 修改代码，一边观察服务日志和测试结果。左右、上下和嵌套分屏可以继续拆分，拖动边缘调整比例，也能把会话弹到独立窗口，再收回工作台。

分屏、弹出和跨工作区移动沿用同一个会话，布局变化不会重启 Shell。下次启动时恢复保存的布局，并创建新的 Shell 进程。

## 光标在哪，中文输入就跟到哪

在 `ab中文cd` 中移动光标，再继续输入，输入法组合文字与候选窗跟随实际编辑位置。Terminal Hub 处理中文宽字符与 AI CLI 的反色光标，让混排文字、选区和输入位置对应起来。

拖选复制、历史搜索、多行粘贴和字号快捷调整也在终端里完成。Claude Code、Codex CLI 等工具的具体适配与验证范围见 [CLI 交互适配](docs/cli-compatibility.md)。

<a id="themes"></a>

## 选择你的主题

深蓝玻璃、深黑、亮白与纸张，保留四种不同的配色与质感。

<table>
  <tr>
    <td width="50%"><a href="docs/readme/theme-glass.svg"><img src="docs/readme/theme-glass.svg" width="580" alt="深蓝玻璃：高光与层次"></a><p align="center"><strong>深蓝玻璃</strong></p></td>
    <td width="50%"><a href="docs/readme/theme-black.svg"><img src="docs/readme/theme-black.svg" width="580" alt="深黑：克制与专注"></a><p align="center"><strong>深黑</strong></p></td>
  </tr>
  <tr>
    <td width="50%"><a href="docs/readme/theme-white.svg"><img src="docs/readme/theme-white.svg" width="580" alt="亮白：轻盈与留白"></a><p align="center"><strong>亮白</strong></p></td>
    <td width="50%"><a href="docs/readme/theme-paper.svg"><img src="docs/readme/theme-paper.svg" width="580" alt="纸张：暖色与叠页"></a><p align="center"><strong>纸张</strong></p></td>
  </tr>
</table>

<p align="center"><sub>SVG 主题配色示意。</sub></p>

<a id="plugins"></a>

## 九个官方扩展，一个开放 SDK

项目目录、Git、任务和终端记录，都可以成为工作台的一部分。顶部「插件」打开市场，按需安装附带的官方扩展；页面可以放在独立窗口、现有底部工具栏或右侧面板。

| 扩展 | 用它做什么 |
| --- | --- |
| 项目导航 | 浏览、收藏项目目录，在指定目录打开终端 |
| Git 工作台 | 查看差异、提交与推送，管理分支、PR 和 Issue |
| 任务面板 | 从 `package.json`、Makefile、justfile 和 `tasks.json` 运行任务 |
| 终端广播 | 选择多个目标会话，同时发送输入 |
| 命令看板 | 从 Shell 集成查看命令、耗时和退出码 |
| 端口看板 | 查看监听端口和占用进程，确认后结束进程 |
| 命令片段 | 保存、搜索常用命令，粘贴到指定会话 |
| 工作区笔记 | 为每个工作区保留独立笔记，切换时自动保存 |
| 屏幕摘录 | 收藏终端屏幕片段，随时查看和复制 |

九个扩展已随当前源码构建携带，发布包内容以对应发行说明为准。安装与位置设置见 [插件市场](docs/plugins/marketplace.md)。你也可以用 .NET / Avalonia 开发自己的工具页，或用声明式脚本注册命令；开发时支持热重载，见 [插件 SDK](docs/plugin-sdk.md)与 [从零开发教程](docs/plugins/development-tutorial.md)。

## 原生终端，承载真实任务

**Avalonia 11 + .NET 8，Windows ConPTY / Linux PTY。** Claude Code、Codex CLI 和日常 Shell 在各自的真实进程中运行。AI CLI 由你自行安装并登录，认证、模型选择和权限沿用各工具的设置。

宣传片中的 **CPU 占用下降 71%、内存分配下降 76%**，来自 v0.4.1 优化前后、同机 10 个会话持续输出场景的对照，表示该负载下的测量结果。数据与其他负载结果见 [性能记录](docs/round-2026-10-04.md)。

<a id="quick-start"></a>

## 开始使用

### 下载发布包

到 [Releases](https://github.com/coffe01-10/TerminalHub/releases) 选择适合你平台的包。便携包、安装包及插件 SDK 的提供情况因版本而异。程序包包含 .NET 运行时，平台依赖、携带插件和变更见对应发行说明。

### 从源码启动

需要 Git 和 **.NET 8 SDK**。Windows 使用 ConPTY，需要 Windows 10 1809 或更新版本；Linux 使用真实 PTY，需要可用的 X11 显示环境。

**Windows**

```powershell
git clone https://github.com/coffe01-10/TerminalHub.git
cd TerminalHub
dotnet restore TerminalHub.sln
dotnet run --project src/TerminalHub.App
```

默认 Shell 为 PowerShell 7；没有安装 `pwsh` 时，可在启动时选择 Windows PowerShell 或命令提示符。

**Linux（Debian / Ubuntu）**

```bash
sudo apt-get install libx11-6 libxcb1 libfontconfig1 libice6 libsm6 fonts-noto-cjk
git clone https://github.com/coffe01-10/TerminalHub.git
cd TerminalHub
bash scripts/run-linux.sh
```

Linux 首次启动默认使用 Bash，也可以选择已安装的 PowerShell 或自定义 Shell。其他发行版的环境说明见 [Linux 调试文档](docs/local-debugging.md)。

### 开始你的第一个项目

进入项目目录，在不同会话中运行已安装的 AI CLI 和项目命令，例如：

```text
claude
codex
```

先用 `echo Hello Terminal Hub` 确认 Shell 正常，再按自己的工作习惯安排 AI 会话、服务和测试。切换会话保留进程；关闭会话会结束对应进程。下次启动恢复的是工作区布局，并创建新的 Shell 进程。

应用采用单实例运行，重新编译后需先正常退出旧实例，再启动新版本。

## 日常快捷键

| 操作 | 默认按键 |
| --- | --- |
| 新建 / 关闭当前会话 | `Ctrl+Shift+N` / `Ctrl+Shift+W` |
| 下一个 / 上一个会话 | `Ctrl+Tab` / `Ctrl+Shift+Tab` |
| 复制选中文字 / 中断程序 | `Ctrl+Shift+C` / `Ctrl+C` |
| 粘贴 | `Ctrl+V`、`Ctrl+Shift+V` 或 `Shift+Insert` |
| 放大 / 缩小 / 重置字号 | `Ctrl+=` / `Ctrl+-` / `Ctrl+0` |

会话切换按键可以在设置中调整；应用启用终端鼠标协议时，按住 `Shift` 使用本地选择和历史滚动。

<a id="docs"></a>

## 开发与文档

原生界面由 **Avalonia 11 + .NET 8** 构建。终端解析、屏幕缓冲、会话管理和平台 PTY 分层实现。

```powershell
dotnet build TerminalHub.sln -c Debug
dotnet test tests/TerminalHub.Tests/TerminalHub.Tests.csproj
```

- [文档目录](docs/README.zh-CN.md)：使用、插件和开发文档的中英文入口。
- [插件使用](docs/plugins/README.md)与[市场设置](docs/plugins/marketplace.md)：安装九个官方扩展、调整位置和启动方式。
- [插件 SDK](docs/plugin-sdk.md)与[开发教程](docs/plugins/development-tutorial.md)：公开 API 和完整示例。
- [开发指引](AGENTS.md)：代码入口、已踩过的坑和对应验证方式。
- [CLI 交互适配](docs/cli-compatibility.md)：工具、协议和实机验证范围。
- [工作区工具说明](docs/project-features-2026-10-02.md)：任务、输出规则、广播、远程文件与录制的具体用法。
- [迭代待办](docs/TODO.md)：后续开发计划。
- [README 素材](docs/readme/README.md)：SVG 来源与生成方式。

欢迎在 [Issues](https://github.com/coffe01-10/TerminalHub/issues) 提交建议。输入和显示问题请附系统、Shell / CLI 版本及复现步骤。

[MIT License](LICENSE) · Copyright © 2026 Jinhong Chen (coffe01-10)
