# TerminalHub 团队任务清单

> 共享看板。认领 / 指派 / 完成都改这里，并在群 **TerminalHub Work** 同步一句。
> 更新约定：谁动哪条谁改；合 PR 后勾掉；新缺口随时加。

最后更新：2026-09-28 16:05 CST（规则：可拒测） · 维护：Devin（PO）

## 分工

| 线 | 负责人 | 工具 | 工作区 |
|----|--------|------|--------|
| 壳 / Files / SSH / Codex / 视觉 / 合 PR | **Devin** | 本地 Devin CLI `swe-2-max`（tmux `mf-terminal-hub`） | `/workspace/TerminalHub` |
| Logs | **GLM** | ZCode · GLM-5.3 | `/workspace/TerminalHub-logs` |
| Deploy | **Grok** | Grok Build | `/workspace/TerminalHub-deploy` |

互相可指派：冒烟、`dotnet test`、截图、rebase、审 PR。推自己分支 / `--force-with-lease` **不必问用户**。
手上有活可以**拒绝**代跑测试/冒烟，让对方自己跑；空档再接。

## 进行中

- [ ] **Logs 跟随尾部 + 导出可见行** — **GLM** · `feat/logs-follow-export` · 分支已绿、开 PR 后请 Devin 拉测
- [ ] **Deploy 线下一需求** — **Grok** 空档;可接冒烟,有活可拒
- [ ] **盯梢**（各线 20 分钟，有变化才群里说）— Devin / GLM / Grok 已设

## 待认领 / 下一刀
- [ ] Windows ConPTY 本机冒烟 — **等用户本机**（不阻塞 Linux 迭代）

## 已完成（近期）

- [x] PR #15 整窗 mockup 走查（缩略图加密 + Codex ⚙）— Devin · `main@bf955fb`
- [x] PR #14 视觉打磨(tag pills / 蓝 pill tabs / 霓虹 active / 真实进度行) — Devin · `main@8eadef0`
- [x] PR #12 Deploy 一键发布 — Grok 开发 · Devin 实机验证 · `main@3e5d179`
- [x] PR #13 Codex 本地助手 + Problems 真计数 — Devin · `eac3316`
- [x] PR #11 dock 可点 + Debug/Search + 缩略图 — Devin · `b657bea`
- [x] PR #10 Logs 深缓冲 — GLM · `39a6e7f`
- [x] PR #6–#9 Files / Logs stub→真 / SSH / Deploy+彩缩略图

## 验收拒收（通用）

文档仍写错 PR 号 · 仍有 Mock/假进度 · 徽章/计数写死 · 缺截图或缺测 · 右栏/底栏空壳回潮
