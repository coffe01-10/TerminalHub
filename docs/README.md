# Documentation

**English** · [简体中文](README.zh-CN.md) · [Project home](../README.md)

Start with the project README for installation and an introduction. The guides below cover the current source build; release notes describe the contents of each published package.

| Guide | English | 中文 |
| --- | --- | --- |
| Workspace tools: rules, broadcast, tasks, SFTP, recording | [Read](project-features-2026-10-02.en.md) | [阅读](project-features-2026-10-02.md) |
| CLI input, mouse, clipboard, IME, verification history | [Read](cli-compatibility.en.md) | [阅读](cli-compatibility.md) |
| Plugin installation and all nine official extensions | [Read](plugins/README.en.md) | [阅读](plugins/README.md) |
| Marketplace, placement, startup, and plugin settings | [Read](plugins/marketplace.en.md) | [阅读](plugins/marketplace.md) |
| Project Navigator, Git, and GitHub | [Read](plugins/project-git-workbench.en.md) | [阅读](plugins/project-git-workbench.md) |
| Build a complete Command Draft plugin | [Read](plugins/development-tutorial.en.md) | [阅读](plugins/development-tutorial.md) |
| Plugin SDK: manifest, lifecycle, UI, host API | [Read](plugin-sdk.en.md) | [阅读](plugin-sdk.md) |
| Development entry points, terminal pitfalls, checks | [Read](development.en.md) | [阅读](../AGENTS.md) |
| README artwork and generators | [Read](readme/README.en.md) | [阅读](readme/README.md) |

Offline plugin manuals: [English](plugins/index.en.html) · [中文](plugins/index.html). The app chooses its interface language for both manual and tutorial links. The HTML pages are bundled with builds; source/API links on them need internet access.

## Distribution quick starts

- Windows: [English](../packaging/QUICKSTART.en.txt) · [中文](../packaging/QUICKSTART.zh-CN.txt).
- Linux: [English](../packaging/QUICKSTART.linux.en.txt) · [中文](../packaging/QUICKSTART.linux.zh-CN.txt).

## Plugin examples

[Minimal](../examples/plugins/Minimal/README.en.md) · [Compact Sidebar](../examples/plugins/CompactSidebar/README.en.md) · [Session Panel](../examples/plugins/SessionPanel/README.en.md) · [Command Draft](../examples/plugins/CommandDraft/README.en.md).

## Measurements and historical records

[Performance overview](performance.en.md) explains the film's workload-specific figures in English. [Linux debugging](local-debugging.md) is already in English.

[Release notes](releases), [planning records](TODO.md), dated acceptance reports, and the development log remain in their original languages. They preserve historical conditions and unperformed checks; their old counts/status are not the current feature inventory. English translation in this round covers usage, plugins, and development guides rather than all historical logs.

## Keep languages aligned

When changing a paired guide, update its English/Chinese counterpart, navigation links, and any bundled HTML affected by it. Preserve API names, commands, paths, and recorded verification limits. State an intentional content difference, such as this English development summary versus the agent working instructions. Do not silently turn a simulated test into a real-platform claim.

The English offline manual is generated from `plugins/README.en.md`; regeneration instructions are at the bottom of that guide. Existing tutorial Markdown/HTML pairs are maintained together.
