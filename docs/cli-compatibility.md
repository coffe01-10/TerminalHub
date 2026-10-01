# CLI 交互适配

重点对象：Claude Code、Grok Build、Codex CLI。工具栏中的本地 Codex 助手面板已移除，终端内运行 `codex` 不受影响。

## 操作

- `Ctrl+V`、`Ctrl+Shift+V`、`Shift+Insert`：粘贴文字。应用启用 bracketed paste 后，多行文字用同一组粘贴边界发送，保留中文及换行。
- 普通 Shell 右键粘贴；Grok 等已启用鼠标协议的应用接收右键事件。
- Grok 的点击区块、拖动、悬停和滚轮通过终端鼠标协议发送；按住 `Shift` 可使用终端自身的拖选和历史滚动。选中后 `Ctrl+Shift+C` 复制，`Ctrl+C` 保留中断功能。
- `Alt+Enter` 发送多行输入所需的替代组合键。`Shift+Enter` / `Ctrl+Enter` 仅在 CLI 协商 Kitty 键盘协议后发送对应 CSI-u；未协商时 `Shift+Enter` 保留普通 Enter 行为。
- 带 Ctrl/Shift/Alt 的方向键保留修饰键；支持 F1–F12、Ctrl+Backspace、Alt+B/F 和 Ctrl+反斜杠。
- 终端聚焦时 F2 交给 CLI（Grok 设置）；会话列表聚焦时 F2 重命名，也可双击标题。

## 本次验证（2026-09-29）

- Avalonia Headless：真实剪贴板入口到 PTY 的中文多行粘贴、鼠标点击/拖动/悬停/滚轮、Shift 本地选择、键盘修饰键、会话焦点切换。
- Grok Build 1.0.41 / Windows ConPTY：捕获到 `CSI ?1003;1006h` 和 bracketed paste 协商；通过鼠标坐标点击信任界面的“No, quit”，进程正常退出。没有接受目录信任，也没有提交模型请求，因此未验证会话内各类响应区块。
- Claude Code 2.1.281：启动并捕获到 bracketed paste 和键盘协议查询，停在目录信任提示；中文编辑光标另由已有 VT 回放回归覆盖。
- Codex CLI 0.149.1：启动并捕获到 bracketed paste，当前测试环境显示登录界面，未验证登录后的对话输入。
- 动画：按真实缩略图边界展开，快速切换从当前姿态继续；几何与 PTY 尺寸不变由 Headless 用例验证。桌面实机流畅度仍需人工体验。

参考：[Grok 终端支持](https://github.com/xai-org/grok-build/blob/main/crates/codegen/xai-grok-pager/docs/user-guide/21-terminal-support.md)、[Grok 快捷键](https://docs.x.ai/build/keyboard-shortcuts)。

## 2026-09-30 中文编辑与缩放验证

本机已信任的项目目录，通过真实 Windows ConPTY 启动 Claude Code；没有接受新的目录信任，也没有提交模型请求。`WindowsCliEditingTests` 验证输入 `ab中文cd` 后：反色编辑光标从列 10 经三次左移到“文”的列 6，再右移到 `c` 的列 8；宽度从 100 缩到 60 后仍为列 8；Home/End 返回列 2/10；bracketed paste 的第二行中文仍保留独立编辑位置。每步读取真实输出帧，并核对 TerminalView 的 IME 锚点。

Avalonia Headless 回归还覆盖：组合状态中的普通 Enter 不发送给 Shell，切换会话清除组合文字，字体与格尺寸改变后在 Render 前查询新 IME 坐标。

手动启用实机回归（未设置环境变量时明确跳过）：

```powershell
$env:TERMINALHUB_CLAUDE_PATH = (Get-Command claude.exe).Source
$env:TERMINALHUB_CLI_CWD = '已经信任的项目目录'
dotnet test tests/TerminalHub.Tests/TerminalHub.Tests.csproj --filter FullyQualifiedName~WindowsCliEditingTests
```

系统输入法的实际候选窗、桌面缩放和多显示器体验尚未人工验证；Grok/Codex 登录后的编辑行为本轮未验证。上述 ConPTY 与 Headless 结果不能代替这些体验。

## 2026-09-30 v0.4 未重复实机

v0.4 工作区功能没有重跑 Claude、Codex 或 Grok 的实机编辑。新增的 Headless 用例只确认两个终端视图在字号 13 和 20 下仍对到同一格；已有用例继续覆盖字体变化后、绘制前的输入法坐标。真实 PowerShell 用更新后的 OSC 133 回车处理仍能报告成功和非零退出码。系统输入法候选窗、多显示器和登录后的 Codex / Grok 仍然没有验证。

## 2026-10-01 Linux 与 Windows 对齐

Linux 侧补齐了与 Windows 同档的 PTY 能力和回归：

- **bash Shell 集成**：Linux 上交互式 bash 会话注入 `ShellIntegration.BashArguments`（`--rcfile <settings>/bash-integration.sh`，脚本先加载 `/etc/bash.bashrc` 与 `~/.bashrc` 再挂钩子，不改动用户配置文件）。PROMPT_COMMAND 发出 `OSC 133;D;<退出码>`、`OSC 7;file://host/path`（cwd）、`133;A`；PS1 尾部附 `133;B`；PS0 发 `133;C`。此前 Linux 只能靠 `/proc` 轮询 cwd，命令完成事件和退出码提示完全缺失。bash < 5.0 没有 PS0，退化为只有 cwd/prompt 标记。
- **PowerShell 集成不再限 Windows**：Linux 上选 pwsh 同样注入 OSC 133/9;9 参数。
- **环境变量**：`PtyEnvironment` 在非 Windows 且父环境没有 `LANG`/`LC_ALL` 时补 `LANG=C.UTF-8`，避免子进程落到 POSIX/C 使 UTF-8/CJK 工具退化。
- **新增 Linux 对等测试**（`dotnet test` 本机全绿）：`LinuxStreamingTests`（退出前流式输出 + COLORTERM/自定义环境变量、订阅者异常不阻断后续输出、退出码上报、`stty size` 验证 resize 生效）对照 `WindowsStreamingTests`；`LinuxCommandCompletionTests`（OSC 133 退出码序列 0→7→0→1、OSC 7 cwd 跟踪 cd、CommandJournal 记录）对照 `WindowsCommandCompletionTests`；`PtyEnvironmentTests` 覆盖环境合成。
- **实机 CLI**：`LocalClaudeFact`/`LocalGrokFact` 现同时在 Windows/Linux 生效，按平台选 ConPTY/forkpty；仍需 `TERMINALHUB_CLAUDE_PATH`/`TERMINALHUB_GROK_PATH` + 已信任目录（`TERMINALHUB_CLI_CWD`），不设则明确 Skip。Windows 专属的标准句柄重定向只在 Windows 分支执行。

仍不对等项：zsh/sh 无集成（zsh 需要 ZDOTDIR 方案，另立项）；交互式 bash 中 `sleep &` 这类作业控制子进程有独立进程组，Kill 只负责 shell 所在组（与 Windows 的整树杀法有差异，属有意保留的 Linux 语义）。
