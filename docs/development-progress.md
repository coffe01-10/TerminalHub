# Development progress — Terminal Hub / 终端控制中心

> Autonomous build log. PRs merge to `main` when green.

## 2026-10-02 — v0.3.4 发行与本地同步

- 根据用户授权统一程序、安装器和快速开始文档为 0.3.4，发行说明见 [v0.3.4](releases/v0.3.4.md)。本版包含 v0.3.3 后的多工作区、工作区工具、分屏、搜索、侧栏、终端编辑与视觉改进，以及下面两项工作区修复。
- Windows 自包含 `app`、便携 ZIP、安装器及 Linux x64 自包含包重新生成。安装器与 ZIP 使用明确的程序文件名单，保留本地个人配置；临时编译器和冒烟配置用后清除。修复 Windows PATH 优先选择 bsdtar、导致 Git Bash 打包无法设置 Linux 可执行权限的问题。
- Windows 便携程序以独立临时配置、模拟 PTY 打开原生窗口并正常退出；Linux 产物核对 x64 ELF 与归档可执行权限。沿用前两轮相关回归与真实 ConPTY 性能测量，不重复全量测试。Linux 实机、真实 SSH、系统输入法候选窗及安装器覆盖升级未验证。

## 2026-10-02 — 工作区标签快速点击与拖动（未发布）

- 用实际指针事件复现连续点击触发双击重命名、产生模态窗口并阻挡后续点击的问题。重命名移至标签右键菜单，点击与拖动结束后返回终端输入焦点。补上标签自定义模板的背景绑定，让文字间隙和内边距也能响应点击。
- 标签可左右拖动排序，拖动中预览其他标签让位，松开后提交并保存顺序；保持当前工作区和终端进程。标签栏溢出时靠近左右边缘自动滚动。Esc、拖出标签栏松开、丢失捕获或窗口失去焦点会取消拖动。
- 连点、不同宽度标签的双向排序、取消拖动、关闭按钮、右键重命名、溢出滚动、保存后重启恢复以及既有工作区/侧栏/分屏相关 30 项 Headless 回归通过。Debug/Release 编译成功；未验证真实桌面鼠标手感，未更新安装包，未结束用户已有实例，未提交或推送。

## 2026-10-02 — 工作区切换性能（未发布）

- 工作区会话与缩略图列表改为一次发布完整列表，合并排队的侧栏定位并避免重复活动会话同步。缩略图按会话复用，分组重排时避免控件父级冲突。工作区直接呈现目标终端，同一工作区内的会话动画与侧栏两种模式继续保留。
- [真实桌面测量](performance-workspace-switch-2026-10-02.md)：两个工作区各 10 个真实 PowerShell ConPTY，全部持续输出，固定侧栏的切换加布局平均耗时 601.81 → 128.81 ms，收起侧栏 446.96 → 55.27 ms；40 次切换的缩略图列表通知 800 → 40 次。UI 定时器间隔不代表 GPU 帧率，高输出负载仍有绘制成本。
- Debug/Release 编译成功，工作区、分组、主题、侧栏和分屏相关 61 项 Headless 回归通过。测量专用窗口、会话和临时设置已清除。未更新安装包、未结束用户已有进程，未提交或推送。

## 2026-10-01 — 工具胶囊与气泡展开动画（未发布）

- 底部「工具」胶囊高 30 像素，距底部 11 像素，比状态栏文字中心略高 10 像素；展开工具栏共用该锚点。新 `DropletDock` 在 420 ms 内先向上鼓起、再横向扩张，并同步改变圆角；内容保持原始尺寸，稍后淡入，不再从屏幕外滑入另一块面板。离开后沿同一形状收回，中途重新触碰继续当前进度。
- 自动模式只响应工具胶囊或展开栏，移除底部大面积空白触发区。保留 450 ms 离开延迟及 Deploy 菜单保持展开，沿用始终显示／隐藏设置。折叠时不拦截终端输入，展开中的内容达到可读阶段才可点击。
- 工具入口改为工具箱轮廓，栏内四个图标统一为 20×20 并继承按钮颜色，统一按钮宽度、顶部对齐及文字间距；悬停抬起幅度缩小为 2 像素。
- Headless 布局与主题相关 17 项通过，含中间形态、固定底部锚点、空白处不呼出、自动收回／隐藏模式及四种主题渲染。Debug 与本地 `app` 更新，不改已发布的 v0.3.1 附件。未验证真实桌面动画帧率。

## 2026-10-01 — v0.3.1 发布

- 用户授权正式发布，程序、安装包和快速开始文档版本统一为 0.3.1，README 指向新版发布页；说明位于 `docs/releases/v0.3.1.md`。
- 发布内容包括 0.3.0 后的会话／输出工具、Claude 输入与绘制改进、Grok 配色兼容、终端悬浮提示移除和缩略图悬停动画。沿用此前已完成的相关回归与真实 CLI 捕获，不为版本号和文档修改重复跑全量。
- Windows x64 自包含程序、便携 ZIP 与安装包重新构建；临时便携编译器用后清理。安装／覆盖升级、系统输入法候选窗、多显示器和真实桌面动画帧率尚未验证，发布说明如实列出。

## 2026-10-01 — 缩略图悬停动画与产物整理

- 左侧卡片悬停时以 300 ms 无过冲缓动抬起、减小透视倾角，边框和底色在 220 ms 内过渡；离开收回，按下略微回压。活动卡片使用更轻的抬起幅度。卡片画面与固定命中区域分离，防止鼠标在边缘停留时因变形反复触发进入／离开。
- 点击展开读取当前卡片画面的四角，与悬停姿态衔接；拖动时保持排序位移动画，提升卡片所属列表项的层级。相关 Headless 回归 29 项通过，覆盖边缘停留、收回、点击输入、快速切换、拖动、主题、Grok 配色和分屏。未测量真实桌面动画帧率。
- 经用户确认删除重复的 `artifacts/debug`、`artifacts/stage-ui`、`artifacts/publish`。发布脚本与安装包改为直接使用 `app`，便携 ZIP 包含 `app` 文件夹；不再创建重复发布目录。截图、性能记录和正式回归样本保留。
- Debug、`app`、便携 ZIP 和安装包更新，包含 Grok 配色与终端悬浮提示移除。本轮临时编译器解压经过用户另行许可，采用官方便携模式，打包后立即清除。没有提交、推送或更新 GitHub Release。

## 2026-10-01 — Grok Build 与浅色主题兼容

- 本机 Grok Build 1.0.46 的真实 ConPTY 启动输出在 100×28 格全部使用 RGB `#141414` 背景，同时大量文字仍使用默认前景。纸张主题的深色默认文字因此失去对比度；背景格抗锯齿还会从行间、文字段之间漏出浅色底。
- 新会话默认自动配色：同一种显式背景覆盖超过四分之三屏幕时，按该背景选择浅色或深色默认文字、光标和选区。少量彩色提示不改变全会话配色，程序显式 RGB 不改写，外壳主题保持原设置。
- 会话工具栏「… → 终端配色」可选自动、跟随界面、深色、浅色；设置随工作区保存，主视图、缩略图、分屏和独立窗口共享模拟器中的配色。默认颜色查询按会话配色回答。单元格背景关闭边缘抗锯齿，文字绘制保留抗锯齿。
- 验证：本机 Grok 启动捕获完成，没有输入或提交模型请求；真实 VT 回放、分数字号背景像素检查、工作区配色保存恢复及绘制／主题／分屏／输入法相关回归共 53 项通过。回放截图位于 `artifacts/grok-theme-fix/grok-paper.png`，正式 VT 回归样本位于 `tests/TerminalHub.Tests/Fixtures/grok-build-1.0.46-startup.vt`。
- Debug 构建已更新。当前运行窗口、`app` 便携版和安装包没有自动替换；未验证 Grok 模型响应、多显示器及系统输入法候选窗。本轮未提交或推送。

## 2026-10-01 — Claude 输入体验收口、绘制优化与本地交付

- 长组合文字按完整字符簇换行，超出输入区后在本地组合视口内显示光标附近的内容；移动组合光标到开头后重新显示前缀。绘制与候选窗共享输入区、编辑位置及组合布局，不再分别扫描分隔线。覆盖格先绘制背景，避免组合字形与 CLI 原有字形重叠。
- 字号、真实终端网格尺寸和窗格位置变化时通知输入法；切换会话、分屏失焦或视图脱离时清除旧组合。候选窗仍可在下一次 Render 前读取当前稳定帧。
- 同机 1／5／10 会话 Headless 测量后，确认绘制是连续输出的主要开销。增加最多两屏的行绘制缓存，滚动中相同内容复用已有绘制；主题／字号变化清理缓存，脱离视图树释放。会话切换首帧直接绘制，后续才建立行缓存，不再因每次换会话重新测量未变的字体。
- [性能记录](performance-2026-10-01.md)：10 会话全部输出的固定负载处理耗时中位数 2522.1 → 958.8 ms，累计托管分配 843.7 → 770.3 MiB。工作集有增加，报告保留全部数据和内存代价；此为 Headless 解析／帧／绘制测量，不是桌面 CPU 占用率测量。
- 最终相关回归 135 项通过，含输入法、绘制、输入、缓冲／历史／重排、分屏、主题、弹出窗口和本机 Claude 真实 ConPTY 中文编辑／方向键／Home/End／缩放／多行粘贴。没有提交 Claude 模型请求。性能入口单独执行，不设通过阈值，不加入普通回归门禁。
- Debug 回归及 Release Windows x64 自包含发布完成；`app` 主程序、便携 ZIP、安装包已同步，附本轮说明与性能记录。版本保留 0.3.0，没有提交、推送或更新 GitHub Release。官方 Inno Setup 编译器仅临时解压用于打包，使用后清除。
- 发布后的 `app/TerminalHub.exe --mock` 用独立实例名与临时设置目录启动，确认创建原生窗口后关闭本轮实例，临时目录已清除。未结束用户原有进程。系统输入法实际候选窗、多显示器和系统安装／升级未验证。此轮交付说明见 [本地迭代包](releases/local-2026-10-01.md)。

## 2026-10-01 — Claude 输入法组合文字换行（早期记录）

- 输入法组合文字从实际编辑光标所在格开始，靠近右边界时按完整字符簇换行，不再整段向左挪。中文宽字符不会拆到两行，组合光标与候选窗使用同一份布局，UTF-16 字符簇内部的位置落到簇起点。
- 隐藏光标且识别到上下分隔线的输入框时，组合文字止于下分隔线，避免覆盖 Claude 状态栏。超过输入框可用行的组合文字仍会截断，此轮没有实现输入框扩高。
- 按用户要求未运行测试，也未启动 Claude 或验证系统输入法候选窗。仅 Debug 编译成功（0 警告、0 错误）；未更新安装包或当前运行进程。
- 随后用户要求测试：新增右边界宽字符换行、整行末尾光标、代理对内部光标、CRLF 及下分隔线裁剪的回归。发现 CRLF 被算成两次换行，已修正为一次。IME／输入相关 25 项通过；本机 Claude 真实 ConPTY 中文方向键、Home/End、缩放和多行粘贴 1 项通过，没有提交模型请求。系统输入法实际候选窗仍未验证。

## 2026-09-30 — v0.4.0 工作区功能（未发行）

- 本轮审查修复：OSC 133 命令记录在标记到达时立即处理，保留当时的屏幕归属、位置和目录，避免同一批后续输出切换屏幕或目录后记录到错误内容。启动客户端读取握手后中断，服务端仍会创建下一条监听管道。新增同批主／备用屏幕切换、目录变化及握手后断连三项回归；产品相关 14 项、终端解析／重排／激活及真实 PowerShell 相关 66 项通过。本次未重复执行资源管理器注册表用例，未打包或发布。
- 会话可以分组、重命名、移入移出、折叠和置顶。折叠只从侧栏藏起卡片，Alt+1–9 仍按完整会话顺序，不因折叠改号。分组标题显示数量，以及“有新输出”或“有终端已退出”。置顶排在分组前面。命令面板带分组名。分组、置顶和顺序写入设置。左侧缩略图不因折叠变高。
- 带 OSC 133 的会话记录命令、目录、耗时和退出码。PowerShell 在回车时标记命令文本和输出起点。记录可以定位、前后跳转，并复制命令和输出；历史被裁掉时说明无法定位，不复制另一条命令。没有 C/D 标记的会话不生成记录。
- 收藏保存名称、多行命令和适用 Shell，可在命令面板搜索，也可用快捷键填入当前终端，不自动回车。Shell 不一致只作提示。
- `TerminalHub.exe --cwd 路径` 在该目录新建终端。已打开的实例经命名管道接收目录，原有终端保留。目录不存在或参数不完整时给出明确提示。启动设置里可以添加或移除资源管理器文件夹右键。
- 工作区模板按最近使用排序，可以复制、导入和导出。打开前预览会话、目录、Shell 和已启用的启动命令。导入只进入预览，不执行启动命令。缺失的目录或 Shell 可以修改。
- 版本号仍是 0.3.0，未打包、未发布。终端兼容性打磨和多会话性能测量没有做。Debug 构建已更新 `src/TerminalHub.App/bin/Debug/net8.0/TerminalHub.exe`；已经打开的窗口仍是旧进程。
- 验证：`ProductV04Tests` 11 项通过；终端、输入法、输入、布局、主题、分屏、历史、重排、工作区恢复、真实 PowerShell 退出码等相关回归通过。没有跑全量，没有 Linux，没有系统输入法候选窗。未设置 `TERMINALHUB_CLAUDE_PATH`，没有重跑 Claude 实机编辑。
- 审查后补上：没开括号粘贴时多行收藏不填入；收藏快捷键避开复制、粘贴和现有操作；命令锚点只在记录它的那块屏幕上解析；模板预览的磁盘检查在停止输入后再做；右键菜单要文件夹和背景两项都在才算已安装。

## 2026-09-30 — v0.3.0 产品迭代

- 根据用户截图修复顶部“查找”文字偏高：统一工具按钮内容居中，查找采用与输出／工具一致的图标文字布局。命令面板结果撑满行、长名称省略、快捷键靠右，选中背景跟随主题；关于页面增加滚动。1100×680／1440×900、深色／纸张主题四组 Headless 布局及文字坐标回归通过，预览保存于 `artifacts/iteration-preview`。按用户要求已将 `app`、旧 `stage-ui` 预览、Debug／Release 构建和安装包同步到当前修复版，便携 ZIP 亦为新版；当前运行的发布目录进程已包含修复，无需强制重启。临时安装包编译工具已清除。
- 实现命令面板、命名工作区及可选模板启动命令，复用已有会话与布局恢复。修复模板重命名时列表替换导致选中项丢失；保存、打开、删除模板均不结束现有会话。
- 单实例通过本机命名管道传递激活请求；最小化窗口恢复，启动阶段请求排队，第二进程中途断开不影响后续激活。
- 保留原有未读输出／进程退出状态，增加 OSC 133 命令状态与应用内合并提醒。真实 PowerShell / ConPTY 回归覆盖成功、非零原生命令退出码、旧退出码清除及 PowerShell 错误。
- 增加 OSC 8 链接单元格、Ctrl+点击网址／本地文件、报错行号与文件拖入引用。远程会话不解析本地文件，点击脚本路径通过文本编辑器打开。
- 关于页面提供版本说明、手动检查更新与下载入口。修正安装脚本会强杀运行进程的问题，补齐中文语言文件。
- 新功能与原有终端、IME、输入、历史、快捷键、分屏、主题及弹窗相关回归通过；检查命令面板和模板设置的窄窗口／正常窗口渲染。便携与安装包交付状态见本版说明及产品待办。
- 原生桌面确认窗口可见，第二次启动保留原主进程和五个 Shell 进程。旧版 PowerShell 参数迁移与拖入路径回归通过；最后兼容修复已纳入本地发布包。安装包未执行系统安装／升级验证。用户已提交推送上一轮实现；经本轮授权已发布 v0.3.0 GitHub Release，附最新安装包与便携 ZIP。
- 多会话性能优化继续暂缓；未用系统输入法候选窗验证代替终端回归，也未声称所有 Shell 均支持命令结果识别。

## 2026-09-30 — v0.2.0 界面整理与终端切换快捷键

- 根据用户反馈恢复左侧缩略图原有高度、间距、倾斜、透明度、叠层装饰与「当前终端」标记。
- 主标题显示真实运行状态与退出码。
- 外观设置集中主题、字体与字号，附实时预览；工作区、快捷键与启动单独分类。快捷键支持录入、清除、恢复默认和保存，默认 Alt+1…9 按侧栏顺序直达终端。修复 KeyGesture.Parse 将数字解析为枚举值的问题，并验证录入不触发会话切换。
- 顶部与 Dock 去除重复入口，底部增加可悬停呼出的「工具」提示；主要操作统一矢量图标、选中和键盘焦点状态。纸张背景纹理与主面板阴影减轻，提高文字与边框对比度。
- 主终端切换动画由 540ms 调整到 300ms，Dock 展开由 320ms 调整到 180ms；保留快速切换连续衔接与原缩略图动画。
- 针对用户截图中的任务栏通用图标，主窗口与弹出窗口打开后重新应用原有 ICO，触发 Windows 图标刷新。已构建并替换本地程序，任务栏实际恢复效果仍需用户核对。
- 终端工具栏统一 32 高度按钮、矢量图标与悬停／按下／禁用状态，分屏开启时有持续选中样式；会话标题超长时省略。
- 已检查四主题、窄窗口及分屏的 Avalonia Headless 渲染，布局、拖动、快速切换、输入与快捷键相关回归通过。Windows 自包含包附快捷键使用说明。本轮不做多会话性能测量或优化。

## 2026-09-30 — 终端基础体验与 Windows 便携预览

- 普通屏幕和历史按软换行重排，保留字符属性、宽字符、当前/保存光标及延迟换行；备用屏幕按固定坐标裁剪，裁剪边界不保留孤立宽字符。复制保留软换行边界的真实空格。
- 历史裁剪后，保留的选区与搜索位置随原文移动；被删除的选区失效；旧搜索结果点击时换算到当前行。屏幕切换或重排清除旧格坐标选区。
- 输入法组合 Enter 不提交到 Shell；切换会话清除旧组合。真实 Claude ConPTY 编辑与多行粘贴已验证，详见 `cli-compatibility.md`。
- 缺少实际启动需要的默认 Shell 时，在主界面选择已安装程序。选择前关闭窗口保留尚未恢复的工作区；已有工作区不依赖未使用的默认 Shell。
- 会话卡显示后台新输出和终端进程退出码；查看会话清除未读，分屏两侧都视为已查看，resize 不计作输出。
- `scripts/publish-windows.ps1 -SkipInstaller` 生成自包含 Windows x64 ZIP，附快速使用说明与许可证。经用户授权放入项目 `app` 文件夹；已通过实际原生窗口核对 PowerShell 会话显示。初次启动遗留无可见窗口的后台实例，清除本轮启动的实例后重新打开成功。
- 相关 Core、IME、输入、历史、CLI、分屏、主题、恢复、会话状态和弹出窗口回归通过；首次启动界面经 Headless 渲染检查，临时截图已删除。系统输入法实际候选窗、多显示器，以及 Grok/Codex 登录后体验未实机验证。NuGet 在线元数据不可达产生 NU1900 警告，编译与打包成功。
- 本轮不做多会话性能测量或优化。

## 2026-09-28

### pending — Logs ▲/▼ error 跳转 (`feat/logs-jump-level`) · PR #43
- **工具栏**: 匹配导航旁「▲ error」/「▼ error」；在当前 `Entries`（尤其「全部」）
  内选上/下一条 Level=`error`（大小写不敏感）；无环绕；仅当邻居存在时启用。
- **行为**: 与 GoNextMatch 一样暂停 follow-tail；软 `StatusText`（`error N/M`）。
- **API**: 静态 `FindAdjacentLevel(entries, fromIndex, level, direction)`（warn 同 helpers
  可用；UI 仅 error）。`CanGoPrevError` / `CanGoNextError` + RelayCommands。
- **边界**: 只动 Logs；不动 Deploy/Files/SSH/Core PTY。
- **验证**: 231 tests green（+4：FindAdjacentLevel、跳转无环绕/暂停跟随、筛选门控、
  MainWindow 按钮绑定 + 截图）。worktree `/workspace/TerminalHub-logs2`；工具 Grok Build /
  Claude Code OK。
- 截图 `docs/screenshots/logs-jump-level.png`（▲/▼ error 可见）。
- 已 rebase 到 `origin/main` @ `a06dd9e`（含 #41）。

### PR #41 — Deploy 坞显示当前配置档 + 清除上次发布结果 (`feat/deploy-active-profile-clear-result`)
- **配置档标签**: Deploy 坞在上次发布徽章下增加安静一行 `ActivePublishProfileLabel`
  （`PublishProfiles.FormatDockLabel`：名称，非空 RID 时 `名称 · linux-x64`）。
- **清除上次结果**: 右键「清除上次发布结果 Clear last result」；有已知 outcome 时可清。
- **验证**: `dotnet test -c Release`。已合 `main@a06dd9e`。

### PR #42 — Logs 级别 chip 活计数 (`feat/logs-level-counts`)
- **活计数**: 级别 chip 文案 `全部 N` / `info N` / `warn N` / `error N`，数字来自
  **环形缓冲 `_buffer`**（不是当前 `Entries` 筛选结果），所以在「全部」下仍可见
  e.g. `error 3`。append / trim / clear / ClearVisible / 缩容后 `RefreshLevelCounts`。
- **API**: `CountLevelsInBuffer` + `FormatLevelChipLabel`；bindable
  `Level*Count` / `Level*ChipLabel`。级别筛选语义不变。
- **边界**: 只动 Logs；不动 Deploy/Files/SSH/Core PTY。
- **验证**: 224 tests green（+4：helpers、缓冲 vs 筛选、trim/clear、MainWindow
  chip 文案 + 截图）。worktree `/workspace/TerminalHub-logs2`；工具 Grok Build /
  Claude Code OK。
- 截图 `docs/screenshots/logs-level-counts.png`（chip 带计数可见）。已合 `main@2c6998c`。


### PR #40 — Logs 紧凑密度 toggle (`feat/logs-compact-density`)
- **「紧凑」chip**: 「换行」旁 `timechip`；关（默认）FontSize 9.5 / Padding 4,1；
  开 ≈8.5 / 2,0。`Classes.compact` + `BoolToLogsFontSizeConverter`。
- **持久化**: 全局 `AppSettings.LogsCompactDensity`；经 `PersistLogsFilters`。
- **验证**: 220 tests green。截图 `docs/screenshots/logs-compact-density.png`。已合 `main@ecc5da1`。
  已合 `main@ecc5da1`。

### PR #37 — Deploy 打包中实时耗时 + 复制上次成功产物路径 (`feat/deploy-live-elapsed-copy-path`)
- **实时耗时**: `IsPublishRunning` 时 Deploy 坞状态行显示 `打包中 · Ns` / `打包中 · NmNs`，
  基于真实 `_publishAttemptStartedAt`，`DispatcherTimer` ~1s 刷新；复用
  `LastPublishResults.FormatDuration` 风格（`FormatLiveElapsed`：0→`0s`）。结束后停表，
  空闲仍显示上次结果徽章。Tooltip 含「已耗时 …」。
- **复制路径**: 右键「复制上次成功产物路径 Copy last artifact path」，门控与
  「打开上次成功产物」一致（`CanOpen`）；点击经 `CopyTextToClipboardAsync` 写绝对路径，
  Output `source=deploy` 记「已复制产物路径 …」；目录消失则禁用/警告。
- **验证**: `dotnet test -c Release` 212+ green；不动 Files/Logs/SSH。
- 截图 `docs/screenshots/deploy-live-elapsed-copy-path.png`。已合 `main@a6c34a6`。

### PR #39 — Logs 环形缓冲容量 presets (`feat/logs-buffer-capacity`)
- **「容量」chips**: 级别 /「相对」/「换行」旁（同 WrapPanel）`capchip` ToggleButton
  条 **500 / 2000 / 5000**；默认 **2000**。缩小容量会丢弃最旧行并刷新
  `Entries`；`StatusText`「缓冲容量 → N」。
- **持久化**: 全局 `AppSettings.LogsBufferCapacity`（int）；经同一
  `PersistLogsFilters` 落盘（与 wrap/relative 同路）。启动时
  `NormalizeSavedBufferCapacity` 把非预设值吸到最近档；`MainWindowViewModel`
  ctor 把已存容量传入 `LogsViewModel`。
- **边界**: 只动 Logs + AppSettings；不动 Deploy/Files/SSH/Core PTY。
- **验证**: 216 tests green（+4：Normalize、shrink trim、SettingsStore 往返、
  MainWindow capchip 绑定 + 截图）。worktree `/workspace/TerminalHub-logs2`；
  工具 Grok Build / Claude Code OK。
- 截图 `docs/screenshots/logs-buffer-capacity.png`（「容量」+ 500/2000/5000 可见；
  500 选中 + StatusText）。已合 `main@671b516`。

### PR #38 — Logs 换行/不换行 toggle (`feat/logs-wrap-toggle`)
- **「换行」chip**: 级别 chip /「相对」旁 `timechip` ToggleButton；开（默认）=
  `TextWrapping.Wrap`（现行行为）；关 = `NoWrap` + `LogsList`
  `ScrollViewer.HorizontalScrollBarVisibility=Auto`，便于密扫长行。
- **绑定**: DataTemplate 内 Message 的 `HighlightTextBlock.TextWrapping` 经
  `BoolToTextWrappingConverter` 绑 `Logs.WrapLines`。
- **持久化**: 全局 `AppSettings.LogsWrapLines`（默认 true）；与
  `LogsUseRelativeTimestamps` 同路经 `PersistLogsFilters` 落盘；**不是** per-session。
- **边界**: 只动 Logs + settings；不动 Deploy/Files/SSH/Core PTY。
- **验证**: 212 tests green（+2：SettingsStore 往返、MainWindow「换行」Toggle 默认开绑定）。
  worktree `/workspace/TerminalHub-logs2`；工具 Grok Build / Claude Code OK。
- 截图 `docs/screenshots/logs-wrap-toggle.png`（「换行」chip 可见且默认开）。
  已合 `main@ace56d9`。

### PR #36 — Logs 复制选中行 (`feat/logs-copy-selected-line`)
- **CopySelectedCommand**: 若 `SelectedIndex` 有效，把该行绝对 `FormatLine`
  （与导出/复制可见行同格式）写入剪贴板；`StatusText`「已复制选中行」。
  无选中 →「没有选中的日志行」；无剪贴板钩子 →「剪贴板不可用」（软失败，不抛）。
- **UI**: 工具行「⧉ 复制可见行」旁「⧉ 复制选中」，`IsEnabled` 绑 `HasSelectedEntry`。
- **键盘**: `LogsList` 聚焦时 Ctrl+C（code-behind `OnLogsListKeyDown`）复制选中行；
  过滤 TextBox 不抢（handler 只挂在 ListBox）。
- **边界**: 不改 CopyVisible / Export；不动 Deploy/Files/SSH/PTY。
- **验证**: 204 tests green（+2：选中行 FormatLine 绝对 + 无选中/无剪贴板软失败）。worktree
  `/workspace/TerminalHub-logs2`；工具 Grok Build。
- 截图 `docs/screenshots/logs-copy-selected-line.png`（Logs 工具行「⧉ 复制选中」可见；DISPLAY=:9 Release）。
  已 rebase 到 `origin/main`（含 #35 @16a8575）。

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
### pending / next Deploy slice — 坞上真实上次发布结果 + 一键打开上次成功产物 (`feat/deploy-last-status-badge`)
- **上次结果**: `ReportPublishExit`（以及启动失败 / 启动前取消）把真实结局写入
  `AppSettings.LastPublishResult`：`success` / `fail` / `cancelled`、进程 exit code、
  `FinishedAt`、耗时。空闲时 Deploy 坞标题下显示 `成功 · 12s` / `失败 exit 1 · 3s` /
  `已取消 · 1s`；从未打包则为空，提示「尚未打包」。打包进行中标题仍是「打包中」，
  不盖上次徽章。没有假进度、假百分比。
- **持久化**: 与配置档相同，经 `SettingsStore` 写入 `settings.json`。重启后坞上仍显示。
  缺字段或 `LastPublishResult: null` 时 Load 不抛。失败或取消保留上次成功目录路径。
- **打开上次成功产物**: 右键「打开上次成功产物 Open last success」，与「最近产物」并列。
  仅当该目录仍在磁盘上时启用；点击复用 `OpenRecentArtifact`。成功退出后取
  `RecentArtifactList` 最新目录写入 `ArtifactPath`。目录被删则禁用，若仍被调用则 Output 警告。
- 发布流进 Output、取消、配置档、最近产物行为保持。
- **验证**: `dotnet test -c Release` 204 tests green（本切片 +6：SettingsStore 往返与缺字段、
  失败/取消保留成功路径、坞菜单打开上次成功产物、目录缺失则禁用）。
- 实机截图：`docs/screenshots/deploy-last-status-badge.png`（Deploy 坞「成功 · 42s」徽章 + 右键「打开上次成功产物」）。

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

## 2026-10-01
- PR #45（`main@1d54437`）会话缩略图收口：确认 StagePreview 即真缓冲下采样并补像素级活性回归；修 #44 引入的 bash PS0 `\[ \]` → `\x01\x02` 字节泄漏（命令输出前的 □□）；active 卡片描边/光晕贴近 mockup。Linux 冒烟截图 `docs/screenshots/session-thumb-live.png`。
- PR #46（`main@2f149fd`）Linux bash 编辑回归：修 `Backspace` 宽字符续格多退一格（readline 按显示列发 \b，终端二次吸附导致中文编辑光标偏一格）；新增 LinuxCliEditingTests 真 PTY 五例 + 缓冲级 ResizeReflow。
- 分屏/弹出光标回归（无产品改动，纯测试+实机冒烟）：分屏进出 reflow 后光标守输入尾、焦点切换不影响邻窗格选区/光标、弹出→收回全程光标不漂且换绑不搬旧选区 — `SplitPopoutCursorTests` 3 例；实机截图 linux-split-popout-*.png。
- PR #47（`main@6a25b7e`）分屏/弹出光标选区回归：进出分屏 reflow、焦点切换、弹出→收回全程光标守输入尾且视图换绑不搬旧选区；无产品改动，行为锁成回归。
- 底栏六键坞常显：默认 `DockVisibilityMode=1`（自动隐藏/隐藏仍在设置可选并持久化）；坞补「新建」「设置」两键成六键（新建/监控/SSH/日志/部署/设置），部署键标签归一为「部署」；`DockVisibilityTests` 3 例。
- Output 底栏默认展开：`OutputVisible` 默认 true（关可持久化）；面板只挂真实应用/会话事件（时间戳+level），无 demo 行；`OutputVisibilityTests` 3 例。
- Linux UI 视觉对齐审计（`feat/linux-ui-visual-parity`）：四主题（DarkGlass/Black/White/Paper）Linux 实机整窗截图齐——结构与 Windows 实机 Paper 参照同构（共享 XAML）；修 `NewDockButton` 缺 `dock-active`（DockHighlight=0）；补 NewDockButton 高亮类绑定。`docs/screenshots/linux-visual-parity-{dashboard,DarkGlass,Black,White,Paper}.png`。
- OSC7 cwd 修复（`feat/linux-osc7-cwd-encoding`）：bash 集成改发裸绝对路径（原 file://$PWD 未编码被 '#'/'?'/'%' 截断）；恢复会话识别 legacy --rcfile 重注入刷新 rc；真 PTY 特殊字符目录回归 + 实机截图。
- PR #51 合入 `main@7a674f7`：OSC7 裸路径 + IsBashRcArguments 重注入；502/0/3 绿；实机截图 C#proj %test 完整。
- PR #52 合入 `main@cadefdd`（Linux 平台对齐）；恢复 Output 级别筛选刀 `feat/output-level-filter-mockup` → PR #53（全部/info/warn/error + 持久化，522 绿）。
- PR #53 合入 `main@75cc0c7`（Output 级别筛选+持久化，522 绿）。下一刀：对照 ui-ref-dashboard/ai-assistant mockup 挑一个可辨差距切片实改。


## 2026-10-03

- v0.4.0 七项工作台功能的源码与文档补齐：任意树形分屏、插件 SDK/本地管理、工作区工具组件化接续上一轮拖动/MRU/语言/布局历史。
- 新增 SDK 独立项目及 Minimal、CompactSidebar、SessionPanel 示例；不引用 App 私有实现。
- 用户追加验收要求后，完成相关 Headless 回归、真实 Windows 原生窗口和 ConPTY 验收；修复备用会话填补、侧栏覆盖终端时的拖放误判、组件样式丢失，以及旧测试依赖隐藏控件/提示符尾空格的问题。结果见 [验收记录](acceptance-v0.4.0-2026-10-03.md)。系统 IME 实际候选窗、多显示器与 Linux 实机未验收；尚未 commit/push/发布。
- 详情见 [本轮工作台记录](workbench-2026-10-03.md)及 [插件 SDK](plugin-sdk.md)。
