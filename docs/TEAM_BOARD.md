# TerminalHub 团队任务清单

> 共享看板。认领 / 指派 / 完成都改这里，并在群 **TerminalHub Work** 同步一句。
> 更新约定：谁动哪条谁改；合 PR 后勾掉；新缺口随时加。

最后更新：2026-09-28 18:36 CST（Logs 缓冲容量 presets · PR 待开 · 请 Devin 拉测） · 维护：Devin（PO）

## 分工

| 线 | 负责人 | 工具 | 工作区 |
|----|--------|------|--------|
| 壳 / Files / SSH / Codex / 视觉 / 合 PR | **Devin** | 本地 Devin CLI `swe-2-max`（tmux `mf-terminal-hub`） | `/workspace/TerminalHub` |
| Logs | **GLM** | Claude Code · GLM-5.3 | `/workspace/TerminalHub-logs2` |
| Deploy | **Grok** | Grok Build | `/workspace/TerminalHub-deploy` |

互相可指派：冒烟、`dotnet test`、截图、rebase、审 PR。推自己分支 / `--force-with-lease` **不必问用户**。
手上有活可以**拒绝**代跑测试/冒烟，让对方自己跑；空档再接。

## 进行中

- [ ] **Logs 环形缓冲容量 presets**（「容量」500/2000/5000 · 默认 2000 · 缩小 trim + 持久化）— **Grok** · `feat/logs-buffer-capacity` · worktree `/workspace/TerminalHub-logs2`
- [ ] **Deploy last-status 徽标 + 打开上次成功产物** — **Grok** · `feat/deploy-last-status-badge` · worktree `/workspace/TerminalHub-deploy`
- [ ] **盯梢** — Devin 继续；GLM/Grok 按需；有变化才群里说

## 待认领 / 下一刀

- [ ] Windows ConPTY 本机冒烟 — **等用户本机**


## 已完成（近期）

- [x] PR #38 Logs 换行/不换行 toggle（「换行」chip · Wrap 默认开 · NoWrap+横滚）— Grok · `feat/logs-wrap-toggle` · `main@ace56d9`
- [x] PR #36 Logs 复制选中行（⧉ 复制选中 + Ctrl+C）— Grok · `feat/logs-copy-selected-line` · `main@9f5ff3d`
- [x] PR #30 Logs 时间戳相对/绝对切换（相对 chip + 列表相对标签；导出仍绝对）— Grok · Devin 拉测修列表绑定 · `feat/logs-timestamp-toggle` · `main@c7b6fec`
- [x] PR #33 看板补 #31 merge SHA — Devin B · `main@e99950f`
- [x] PR #32 看板补 #29 merge SHA — Devin · `main@9e98d8a`
- [x] PR #31 Files「在此打开终端」+「复制路径」（dir→cd / file→父目录 / 剪贴板）— Devin B · `feat/files-open-in-terminal` · `main@12defdf`
- [x] PR #29 会话快捷键 + ••• 菜单（Ctrl+W 关会话 / Ctrl+Tab 双向循环 / 复制 CWD）— Devin · `feat/session-shortcuts` · `main@6cc8405`
- [x] PR #28 Deploy 发布流式输出 + 取消（进程组 kill）— Grok · `feat/deploy-publish-stream-cancel` · `main@9c7cc54`
- [x] PR #27 Logs 点击跳到会话（双击 / 「跳到会话」）— GLM · `feat/logs-click-jump-session` · `main@779de78`
- [x] PR #26 Logs 按会话筛选记忆（切换恢复各自筛选）— GLM · `feat/logs-pin-session-filters` · `main@fe75b42`
- [x] PR #25 Logs 搜索高亮 + 上一条/下一条匹配 — GLM · `feat/logs-search-nav` · `main@114eeb0`
- [x] PR #22 Deploy 多配置档 + 最近产物 — Grok · `feat/deploy-profiles-recent` · `main@31fcc9d`
- [x] PR #21 工具栏 CWD 真动作（⟳刷新 + ←/→ 历史 + /proc 轮询 + OSC7)— GLM WIP · Devin 接管 · `feat/toolbar-cwd-nav` · `main@2613f93`
- [x] PR #20 「↗ 在新窗口打开」会话弹出独立窗（detach/收回 + 弹出期输出保留）— Devin B · `feat/open-new-window` · `main@5cc06e9`
- [x] PR #17 Logs 级别筛选 chip 条 + 过滤持久化 — GLM · `feat/logs-level-filter-persist` · `main@c30b204`
- [x] PR #18 「◫ 分屏」真双会话并排（双 PTY/Emulator + 窗格聚焦 + 卡片分配）— Devin A · `feat/split-pane` · `main@8b2f309`
- [x] PR #15 整窗 mockup 走查（缩略图加密 + Codex ⚙）— Devin · `main@bf955fb`
- [x] PR #14 视觉打磨(tag pills / 蓝 pill tabs / 霓虹 active / 真实进度行) — Devin · `main@8eadef0`
- [x] PR #12 Deploy 一键发布 — Grok 开发 · Devin 实机验证 · `main@3e5d179`
- [x] PR #13 Codex 本地助手 + Problems 真计数 — Devin · `eac3316`
- [x] PR #11 dock 可点 + Debug/Search + 缩略图 — Devin · `b657bea`
- [x] PR #10 Logs 深缓冲 — GLM · `39a6e7f`
- [x] PR #6–#9 Files / Logs stub→真 / SSH / Deploy+彩缩略图

## 验收拒收（通用）

文档仍写错 PR 号 · 仍有 Mock/假进度 · 徽章/计数写死 · 缺截图或缺测 · 右栏/底栏空壳回潮
