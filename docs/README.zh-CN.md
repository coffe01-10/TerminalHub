# 文档目录

[English](README.md) · **简体中文** · [项目首页](../README.zh-CN.md)

安装与介绍从项目 README 开始。下面的使用说明对应当前源码构建，各发行包携带的内容以对应版本说明为准。

| 文档 | 中文 | English |
| --- | --- | --- |
| 工作区工具：规则、广播、任务、SFTP、录制 | [阅读](project-features-2026-10-02.md) | [Read](project-features-2026-10-02.en.md) |
| CLI 输入、鼠标、剪贴板、输入法与验证历史 | [阅读](cli-compatibility.md) | [Read](cli-compatibility.en.md) |
| 插件安装与九个官方扩展 | [阅读](plugins/README.md) | [Read](plugins/README.en.md) |
| 市场、位置、启动和插件设置 | [阅读](plugins/marketplace.md) | [Read](plugins/marketplace.en.md) |
| 项目导航、Git 和 GitHub | [阅读](plugins/project-git-workbench.md) | [Read](plugins/project-git-workbench.en.md) |
| 从零开发命令草稿插件 | [阅读](plugins/development-tutorial.md) | [Read](plugins/development-tutorial.en.md) |
| 插件 SDK：清单、生命周期、界面与宿主 API | [阅读](plugin-sdk.md) | [Read](plugin-sdk.en.md) |
| 开发入口、终端陷阱与相关验证 | [阅读](../AGENTS.md) | [Read](development.en.md) |
| README 素材与生成方式 | [阅读](readme/README.md) | [Read](readme/README.en.md) |

离线插件手册：[中文](plugins/index.html) · [English](plugins/index.en.html)。应用按界面语言打开手册和教程。HTML 随构建携带，其中源码/API 外链仍需联网。

## 发行包快速说明

- Windows：[中文](../packaging/QUICKSTART.zh-CN.txt) · [English](../packaging/QUICKSTART.en.txt)。
- Linux：[中文](../packaging/QUICKSTART.linux.zh-CN.txt) · [English](../packaging/QUICKSTART.linux.en.txt)。

## 插件示例

[最小插件](../examples/plugins/Minimal/README.md) · [紧凑侧栏](../examples/plugins/CompactSidebar/README.md) · [会话面板](../examples/plugins/SessionPanel/README.md) · [命令草稿](../examples/plugins/CommandDraft/README.md)。各示例均提供英文说明。

## 测量与历史记录

[英文性能概览](performance.en.md)解释宣传片中的特定负载测量结果。[Linux 调试说明](local-debugging.md)已有英文版。

[发布记录](releases)、[迭代待办](TODO.md)、历次验收和开发日志保留原语言，记录各自的条件和未执行范围。旧数量与旧状态不能替代当前功能清单。本轮英文覆盖使用、插件和开发文档，不包含全部历史日志。

## 双语维护

修改成对文档时同步更新另一语言、入口链接和相关离线 HTML。API 名称、命令、路径和验证边界保持一致。英文开发指引是面向读者的整理版，代理工作约定仍以根 AGENTS.md 为准。

英文离线插件手册从 `plugins/README.en.md` 生成，命令见该页末尾。现有教程的 Markdown 与 HTML 继续同步维护。
