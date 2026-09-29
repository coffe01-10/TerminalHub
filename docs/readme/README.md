# README 展示素材

这些素材用于仓库首页，不参与应用运行。

## 来源

- `logo.svg` 由本目录的 `generate_logo.py` 生成。徽章沿用应用图标的三窗枢纽造型（`src/TerminalHub.App/Assets/terminal-hub-icon.png`），字标为脚本绘制的 5×7 像素字形；墨色通过 `prefers-color-scheme` 随明暗主题变化。
- `typing.svg` 由本目录的 `generate_typing.py` 生成。迷你终端窗口用 SMIL textPath 逐字敲入还原、构建与启动命令，随光标跟踪闪烁、循环清屏重播；机制参考 readme-typing-svg（DenverCoder1），本地生成而非调用外部服务。内容为示意动画，不是终端录屏。
- `runtime.svg`、`ui.svg`、`platform.svg`、`license.svg` 由本目录的 `generate_badges.py` 生成。采用项目的深蓝与强调色，不依赖外部徽章服务；徽章描述技术栈与许可证，不代表 CI 或兼容性测试结果。
- `workspace.png`、`split.png`、`settings.png`、`black.png` 于 2026-09-29 从当前应用代码导出。来源是 `ThemeWorkspaceTests.ThemeCoversTerminalAndPreview_WithoutChangingPtyDimensions`，采用 Avalonia Headless + Skia 渲染真实原生控件，PTY 为 Mock，终端文本为示例数据。图片未经合成或重绘，不用于证明真实 CLI 交互或动画帧率。
- 首页的组织方式参考 `write-visual-readme` 技能；图标、截图和徽章均采用本项目素材，未引用其他项目的品牌图片。

## 更新

仓库根目录运行以下命令重新生成 logo、打字动画与徽章：

```powershell
python docs/readme/generate_logo.py
python docs/readme/generate_typing.py
python docs/readme/generate_badges.py
```

需要更新界面图时，将截图输出到一个临时目录，再运行已有的主题渲染用例：

```powershell
$env:TERMINALHUB_STAGE_CAPTURES = Join-Path $env:TEMP 'terminalhub-readme-captures'
dotnet test tests/TerminalHub.Tests/TerminalHub.Tests.csproj --filter 'FullyQualifiedName~ThemeCoversTerminalAndPreview'
```

人工检查后，按以下关系替换本目录的正式素材。临时目录和环境变量用完即清理；不要删除本目录的正式图片。

| 导出文件 | 首页素材 |
| --- | --- |
| `theme-DarkGlass.png` | `workspace.png` |
| `split-White.png` | `split.png` |
| `settings-Paper.png` | `settings.png` |
| `theme-Black.png` | `black.png` |

检查截图中是否包含私人路径或用户数据；避免使用个人真实会话作为展示样本。
