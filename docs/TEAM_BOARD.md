# TerminalHub 团队任务清单

> 共享看板。认领 / 指派 / 完成都改这里，并在群 **TerminalHub Work** 同步一句。
> 更新约定：谁动哪条谁改；合 PR 后勾掉；新缺口随时加。

最后更新：2026-09-28 16:05 CST · 维护：Devin（PO）

## 分工

| 线 | 负责人 | 工具 | 工作区 |
|----|--------|------|--------|
| 壳 / Files / SSH / Codex / 视觉 / 合 PR | **Devin** | 本地 Devin CLI `swe-2-max`（tmux `mf-terminal-hub`） | `/workspace/TerminalHub` |
| Logs | **GLM** | ZCode · GLM-5.3 | `/workspace/TerminalHub-logs` |
| Deploy | **Grok** | Grok Build | `/workspace/TerminalHub-deploy` |

互相可指派：冒烟、`dotnet test`、截图、rebase、审 PR。推自己分支 / `--force-with-lease` **不必问用户**。

## 进行中

- [ ] **视觉打磨**（对照 `docs/design/ui-ref-*.png`）：左栏会话卡、中部进度条/输入行、玻璃质感（不要求像素级）— **Devin** · 分支待开 · 跳过 ConPTY / 付费 LLM
- [ ] **Logs 跟随尾部 + 导出可见行** — **GLM** · `feat/logs-follow-export` · 开 PR 后 @Devin 拉测
- [ ] **盯梢**（各线 20 分钟，有变化才群里说）— Devin / GLM / Grok 已设

## 待认领 / 下一刀

- [ ] 视觉 PR 合入后：整窗 1280×800 对照 mockup 走查清单（Files / Logs / SSH / Deploy / Codex / Problems）— 可派给任一人冒烟
- [ ] Windows ConPTY 本机冒烟 — **等用户本机**（不阻塞 Linux 迭代）
- [ ] Deploy 线下一需求（有缺口再开）— **Grok** 空档可接冒烟指派

## 已完成（近期）

- [x] PR #12 Deploy 一键发布 — Grok 开发 · Devin 实机验证 · `main@3e5d179`
- [x] PR #13 Codex 本地助手 + Problems 真计数 — Devin · `eac3316`
- [x] PR #11 dock 可点 + Debug/Search + 缩略图 — Devin · `b657bea`
- [x] PR #10 Logs 深缓冲 — GLM · `39a6e7f`
- [x] PR #6–#9 Files / Logs stub→真 / SSH / Deploy+彩缩略图

## 验收拒收（通用）

文档仍写错 PR 号 · 仍有 Mock/假进度 · 徽章/计数写死 · 缺截图或缺测 · 右栏/底栏空壳回潮
