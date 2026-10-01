# 终端输出书签与导出 — 进度

2026-09-30 开始。按用户给定的 8 步子功能顺序推进。

## 状态

| 子功能 | 状态 |
| --- | --- |
| 1. 复制当前屏幕 | 已实现,测试通过 |
| 2. 复制全部输出 | 已实现,测试通过 |
| 3. 保存选区 / 全部输出为文本 | 已实现(UI 对话框未实测) |
| 4. 导出单条命令记录 | 已实现,测试通过 |
| 5. 添加输出书签 | 已实现,测试通过 |
| 6. 书签列表与跳转 | 已实现,已补齐完整文字查看与导出 |
| 7. 书签搜索与持久保存 | 已实现,测试通过 |
| 8. 命令面板接入与弹层收尾 | 已实现 |

## 实现入口

- 终端工具栏的会话操作菜单:复制当前屏幕 / 复制全部输出 / 保存选区为文本 / 保存全部输出 / 添加书签 / 书签列表。
- 命令记录面板:「保存为文本」按钮(复用 `CommandRecord.CopyText`)。
- 命令面板:六个输出工具操作,作用于 `ActiveSession` / 活动终端。
- 书签弹层:当前会话/全部切换、搜索、定位/查看/复制/改名/删除、完整文字阅读与保存为文本、空态与提示。

## 关键文件

- `Controls/TerminalView.OutputTools.cs`:`GetVisibleText`(尊重 `_viewOffset`)、`GetAllText`、`HasSelection`、`CaptureBookmark`。
- `Views/MainWindow.OutputTools.cs`:复制/保存编排、原生保存对话框、书签弹层、Toast。
- `ViewModels/MainWindowViewModel.Bookmarks.cs` + `BookmarkViewModel.cs`:书签集合、筛选、增删改、持久化。
- `Core/Settings/OutputBookmark.cs` + `AppSettings.OutputBookmarks`:JSON 持久化,锚点不持久化。

## 已验证

- `dotnet build`:0 错误 0 警告。
- `tests/TerminalHub.Tests/OutputToolsTests.cs` 16 个用例全部通过:文本提取、软换行与中文、视口漂移、锚点重排与裁剪、备用屏幕切换、书签搜索与持久化、会话关闭、命令记录、剪贴板不可用提示，以及本次修复的回归。
- 2026-10-01 相关回归 141 项全部通过:TerminalImeTests、TerminalInputTests、TerminalCoreTests、TerminalStreamingTests、SplitPaneTests、ThemeWorkspaceTests、ProductV04Tests、OutputToolsTests、TerminalHistoryTests、TerminalResizeTests。排除了 `ExplorerMenu_InstallsQuotedCommands_AndRemovesOnlyThatVerb`：本次沙箱禁止注册表写入，该旧测试在前次检查中因权限失败。
- 审查修复:书签行内按钮点击误触发定位(改为视觉祖先检查);关闭弹层时焦点落到已卸载元素(加 ActiveTerminal 兜底)。

## 2026-10-01 收尾修复

- 移除 200 条书签上限及静默删除逻辑；添加第 201 条后，最早书签的文字、锚点和持久化记录全部保留，重新加载也保留 201 条。
- 书签弹层增加完整文字阅读区和「查看」「保存书签为文本」入口，重启前保存的书签也可查看与导出。
- 阅读区保留方向键、选中文字和复制行为，Tab 可以移到操作按钮，避免弹层按键处理抢走文本操作。
- 在 1440×900 和 1100×680 下用 Avalonia Headless 验证读取完整 25 行中文/emoji 快照，以及实际导出按钮写入的 UTF-8 文件内容。文件选择结果由模拟选择器提供，文件写入使用 Avalonia 的本地文件实现；取消选择后原文件内容保持不变。
- 两种窗口尺寸的深色弹层已渲染检查；临时截图已删除。

## 未验证(如实记录)

- 原生保存对话框未在真实窗口中操作过(headless 使用模拟选择器);真实系统剪贴板未操作。
- 真实窗口与浅色主题下的视觉确认未做。
- 分屏下书签跳转的实机点击验证未做(VM 层的 pane 分配逻辑已审查)。
- 未启动真实应用验证菜单/命令面板交互；Headless 已验证书签查看和导出按钮。
- 安装版/正在运行的进程未更新。
