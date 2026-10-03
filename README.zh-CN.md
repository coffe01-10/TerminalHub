<p align="center">
  <picture>
    <source media="(max-width: 600px)" srcset="docs/readme/hero-zh-compact.svg">
    <img src="docs/readme/hero-zh.svg" width="1200" alt="Terminal Hub：专为 AI CLI 打造的终端管理工作台。把 Claude Code、Codex CLI 和日常 Shell 会话放在同一个工作空间。">
  </picture>
</p>

<p align="center"><a href="README.md">English</a> / <strong>简体中文</strong></p>
<p align="center"><strong>专为 AI CLI 打造的终端管理工作台。</strong></p>

<p align="center">
  <a href="src/TerminalHub.App/TerminalHub.App.csproj"><img src="docs/readme/runtime.svg" height="24" alt=".NET 8"></a>
  <a href="src/TerminalHub.App"><img src="docs/readme/ui.svg" height="24" alt="Avalonia 11"></a>
  <a href="scripts/run-linux.sh"><img src="docs/readme/platform.svg" height="24" alt="Windows / Linux"></a>
  <a href="LICENSE"><img src="docs/readme/license.svg" height="24" alt="MIT 许可证"></a>
</p>

<p align="center">
  <a href="#quick-start">开始使用</a> &nbsp; · &nbsp;
  <a href="https://github.com/coffe01-10/TerminalHub/releases">下载发布包</a> &nbsp; · &nbsp;
  <a href="#themes">主题风格</a> &nbsp; · &nbsp;
  <a href="#docs">深入了解</a>
</p>

让 AI 写代码的同时，你还需要运行项目、观察构建、执行测试，以及偶尔接管命令行。Terminal Hub 把这些终端会话组织在一个原生工作台里，让你按项目安排工作，随时找到需要关注的任务。

Claude Code、Codex CLI 和日常 Shell 可以各自运行。AI CLI 由你自行安装并登录，Terminal Hub 承载真实 Shell 进程并管理会话；认证、模型选择与 CLI 自身权限沿用各工具的设置。

## 围绕 AI CLI 的日常工作

**一个项目，容纳多种任务。** 把 AI 对话、开发服务、构建与测试放进同一个工作区；多个项目各自保留会话和布局，切换时后台进程继续运行。

**看见进度，再决定介入。** 实时会话预览与后台输出提示帮助你关注正在发生的事。需要时切换、分屏或独立打开终端，历史与搜索让重要输出可以重新找到。

**认真对待终端输入。** 中文输入法、光标定位、多行粘贴、文本选择与键盘协议，都是 AI 命令行体验的一部分。具体工具的验证范围见 [CLI 交互适配](docs/cli-compatibility.md)。

**把重复工作留下来。** 保存常用项目命令，用输出规则关注关键内容，也可以录制终端操作、处理 SSH 会话与远程文件。字体、主题和快捷键按你的习惯调整。

<p align="center">
  <img src="docs/readme/workflow.svg" width="1200" alt="使用流程示意：从项目和 Shell 开始，在多个独立会话中并行工作，下次启动恢复保存的布局。">
</p>

<p align="center"><sub>工作方式的概念图，不依赖具体页面布局。</sub></p>

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

<a id="quick-start"></a>

## 开始使用

### 下载发布包

到 [GitHub Releases](https://github.com/coffe01-10/TerminalHub/releases) 选择 Windows 安装版、Windows 便携包或 Linux x64 包。发布包包含 .NET 运行时；平台依赖与版本变更见对应发行说明。

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

- [开发指引](AGENTS.md)：代码入口、已踩过的坑和对应验证方式。
- [CLI 交互适配](docs/cli-compatibility.md)：工具、协议和实机验证范围。
- [工作区工具说明](docs/project-features-2026-10-02.md)：任务、输出规则、广播、远程文件与录制的具体用法。
- [迭代待办](docs/TODO.md)：后续开发计划。
- [README 素材](docs/readme/README.md)：SVG 来源与生成方式。

欢迎在 [Issues](https://github.com/coffe01-10/TerminalHub/issues) 提交建议。输入和显示问题请附系统、Shell / CLI 版本及复现步骤。

[MIT License](LICENSE) · Copyright © 2026 Jinhong Chen (coffe01-10)


## v0.4.0 工作台扩展（本地构建）

支持任意嵌套分屏、跨工作区拖动、最近使用切换、中英文切换、布局撤销/重做及保留状态的独立工作区工具窗口。新增本地 .NET/Avalonia 插件机制，开发与导入见 [插件 SDK](docs/plugin-sdk.md)，交付边界见 [本轮记录](docs/workbench-2026-10-03.md)。工具窗口和新增模块已精修界面，见 [当前界面与验收](docs/ui-refinement-2026-10-03.md)。已发布版仍为 v0.3.4。
