# README 展示素材

仓库首页用 SVG 表达产品定位与工作方式。素材不描绘具体页面，也不跟随界面改版更新。

## 首页引用的素材

- `hero-en.svg`、`hero-zh.svg`：双语品牌横幅。三个独立的命令行会话围绕 Terminal Hub 汇聚，强调“专为 AI CLI 打造的终端管理工作台”。图形为概念示意，不表示自动调度 AI、共享对话上下文或产品运行画面。
- `workflow.svg`：开始项目、并行会话、恢复布局的工作方式示意。恢复布局会创建新的 Shell 进程，不恢复原进程的内存状态。
- `hero-en-compact.svg`、`hero-zh-compact.svg`：窄屏使用的紧凑横幅，保留更大的文字与命令行标识。
- 以上五幅图由 `generate_showcase.py` 生成，只使用 Python 标准库。品牌图标沿用本项目的三扇终端造型，CLI 名称使用普通文字，不包含第三方品牌图标。SVG 自包含，无外部图片或字体依赖；文字使用系统字体回退。
- `runtime.svg`、`ui.svg`、`platform.svg`、`license.svg` 由 `generate_badges.py` 生成，描述技术栈、平台与许可证，不代表 CI 或兼容性测试结果。
- `theme-glass.svg`、`theme-black.svg`、`theme-white.svg`、`theme-paper.svg`：保留原有四种主题风格的配色示意图，继续在中英文首页展示。点击可直接查看 SVG，无需同步页面截图。

## 更新 SVG

仓库根目录运行：

```powershell
python docs/readme/generate_showcase.py
```

只有定位或工作方式发生变化时才需要改横幅与概念图。文字与几何布局保存在生成脚本中，修改后重新生成即可；中英文横幅共享绘图逻辑。徽章的信息发生变化时运行 `python docs/readme/generate_badges.py`。

查看时检查中文字体回退、桌面与窄屏的可读性，以及 README 的图片链接。图中的主要信息也保留在正文和图片 alt 中。

## 保留的历史素材

`logo.svg`、`typing.svg` 与已有 PNG 保留为历史素材，首页不再引用页面截图或打字动画，不要求随界面更新同步维护。四幅主题 SVG 仍是首页展示素材。

- `logo.svg` 由 `generate_logo.py` 生成，沿用应用图标造型与本地像素字标。
- `typing.svg` 由 `generate_typing.py` 生成，是命令示意动画。
- `workspace.png`、`split.png`、`settings.png`、`black.png` 于 2026-09-29 使用 Avalonia Headless + Skia 和 Mock PTY 导出，是当时原生控件的渲染，不能用于证明当前界面或真实 CLI 行为。

首页组织方式参考 `write-visual-readme` 技能，最终 SVG、文案和生成脚本均保存在本仓库。
