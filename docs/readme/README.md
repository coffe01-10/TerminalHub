# README 展示素材

[English](README.en.md) · **简体中文**

仓库首页沿用 v0.4.1 宣传片的深色底、蓝青配色和像素字标，以视频介绍产品交互。SVG 用于品牌与主题展示，不是界面截图。

## 首页引用的素材

- `hero-en.svg`、`hero-zh.svg`：双语品牌横幅，与宣传片片尾使用同一套三扇终端图标和像素字标，主张“让 AI 写代码，你掌控全局”。
- `hero-en-compact.svg`、`hero-zh-compact.svg`：窄屏使用的紧凑横幅，保留更大的文字与命令行标识。
- 以上四幅横幅由 `generate_showcase.py` 生成，复用 `generate_logo.py` 的图标与字模，只使用 Python 标准库。SVG 自包含，无外部图片或字体依赖；说明文字使用系统字体回退。
- `runtime.svg`、`ui.svg`、`platform.svg`、`license.svg` 由 `generate_badges.py` 生成，描述技术栈、平台与许可证，不代表 CI 或兼容性测试结果。
- `theme-glass.svg`、`theme-black.svg`、`theme-white.svg`、`theme-paper.svg`：保留原有四种主题风格的配色示意图，继续在中英文首页展示。点击可直接查看 SVG，无需同步页面截图。

## 更新 SVG

仓库根目录运行：

```powershell
python docs/readme/generate_showcase.py
```

文字与几何布局保存在生成脚本中，修改后重新生成即可；中英文及窄屏横幅共享绘图逻辑。徽章的信息发生变化时运行 `python docs/readme/generate_badges.py`。

查看时检查中文字体回退、桌面与窄屏的可读性，以及 README 的图片链接。图中的主要信息也保留在正文和图片 alt 中。

## 保留的历史素材

`workflow.svg`、`logo.svg`、`typing.svg` 与已有 PNG 保留为历史素材，首页不再引用流程示意、旧页面截图或打字动画，不要求随界面更新同步维护。四幅主题 SVG 仍是首页展示素材。

- `workflow.svg` 由 `generate_showcase.py` 生成，表示开始项目、并行会话与恢复布局；恢复布局会创建新的 Shell 进程。
- `logo.svg` 由 `generate_logo.py` 生成，其图标与字模也用于当前首页横幅。
- `typing.svg` 由 `generate_typing.py` 生成，是命令示意动画。
- `workspace.png`、`split.png`、`settings.png`、`black.png` 于 2026-09-29 使用 Avalonia Headless + Skia 和 Mock PTY 导出，是当时原生控件的渲染，不能用于证明当前界面或真实 CLI 行为。

首页组织方式参考 `write-visual-readme` 技能，最终 SVG、文案和生成脚本均保存在本仓库。
