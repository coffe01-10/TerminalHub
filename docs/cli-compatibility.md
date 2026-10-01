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

- **bash Shell 集成**：Linux 上交互式 bash 会话注入 `ShellIntegration.BashArguments`（`--rcfile <settings>/bash-integration.sh`，脚本先加载 `/etc/bash.bashrc` 与 `~/.bashrc` 再挂钩子，不改动用户配置文件）。PROMPT_COMMAND 发出 `OSC 133;D;<退出码>`、`OSC 7;<绝对路径>`（cwd，不用 URI，保留路径中的 `#`、`?`、`%`）、`133;A`；PS1 尾部附 `133;B`；PS0 发 `133;C`。此前 Linux 只能靠 `/proc` 轮询 cwd，命令完成事件和退出码提示完全缺失。
- **PowerShell 集成不再限 Windows**：Linux 上选 pwsh 同样注入 OSC 133/9;9 参数。
- **环境变量**：`PtyEnvironment` 在非 Windows 且父环境没有 `LANG`/`LC_ALL` 时补 `LANG=C.UTF-8`，避免子进程落到 POSIX/C 使 UTF-8/CJK 工具退化。
- **新增 Linux 对等测试**（`dotnet test` 本机全绿）：`LinuxStreamingTests`（退出前流式输出 + COLORTERM/自定义环境变量、订阅者异常不阻断后续输出、退出码上报、`stty size` 验证 resize 生效）对照 `WindowsStreamingTests`；`LinuxCommandCompletionTests`（OSC 133 退出码序列 0→7→0→1、OSC 7 cwd 跟踪 cd、CommandJournal 记录）对照 `WindowsCommandCompletionTests`；`PtyEnvironmentTests` 覆盖环境合成。
- **实机 CLI**：`LocalClaudeFact`/`LocalGrokFact` 现同时在 Windows/Linux 生效，按平台选 ConPTY/forkpty；仍需 `TERMINALHUB_CLAUDE_PATH`/`TERMINALHUB_GROK_PATH` + 已信任目录（`TERMINALHUB_CLI_CWD`），不设则明确 Skip。Windows 专属的标准句柄重定向只在 Windows 分支执行。

zsh/sh 仍可作为自定义终端运行，但没有 OSC 命令完成集成。2026-10-01 后续修复已让 Linux 会话关闭时结束 Shell 的进程树；真实 PTY 回归覆盖独立进程组中忽略 HUP 的后台作业，避免关闭标签后留下进程。

## 2026-10-01 UI 与功能对齐收口

基于远端 main `0a34ce0` 的同一份代码，在 Windows 和 Linux 两端验证。

- Linux 首次启动默认使用 Bash，保留用户明确保存的 Shell；启动设置不再显示 Linux 上不能直接使用的 cmd.exe/WSL 选项，路径示例和字体预览提示也使用对应平台的样式。Windows 默认值及选择保持原样。
- Linux 设置中的文件夹右键入口现在实现 Thunar 自定义操作，安装/更新/删除只处理 Terminal Hub 自身条目，保留用户其他操作；Windows 保留原有资源管理器入口。Thunar 的字段码用法依据 [Xfce 官方文档](https://docs.xfce.org/xfce/thunar/custom-actions)。
- 修复 Linux PTY 对空参数、带空格的 Shell 文件名以及双引号内反斜杠的传递；修复文件链接在带点号的父目录中提前结束的问题。
- 深蓝玻璃、深黑、亮白、纸张四主题均在 1440×900 和 1100×680 绘制核对。Linux 自包含发布程序已在隔离 X11 显示中启动并捕获原生窗口。
- Linux 完整回归 519 通过、3 跳过；单独设置 CLI 路径后，Claude 中文光标/缩放/多行编辑及 Grok 启动颜色回归 2 项通过，没有发送模型请求。Windows 完整回归中 518 通过，注册表测试因沙箱权限失败后单独提权重跑通过；3 项按配置跳过。

这些验证未覆盖系统输入法的真实候选窗、Wayland、多显示器，也未人工点击 Thunar 右键菜单。SSH 面板的命令构建/配置逻辑由回归覆盖，未连接额外真实 SSH 服务。用户已有设置未修改，Windows 安装版未替换。

末次补测中，Linux 原生终端的普通键盘输入正常，但从 GTK 系统剪贴板通过快捷键和右键粘贴均得到空文本。尚未确认是隔离 X11 环境还是产品问题；随后 Linux 主机 SSH 连接中断，未能继续诊断。此项未修复，也未验证通过，不能据此宣称所有功能已完全对齐。
