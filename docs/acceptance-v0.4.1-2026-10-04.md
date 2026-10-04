# v0.4.1 本地维护版验收记录（2026-10-04）

范围：中文输入/光标实测与本地 Windows 维护包。本记录只描述实际执行结果；候选窗位置已完成逐张人工视觉审核，适用边界见正文。

## 环境

- Windows 10.0.26200，单显示器 1536×864（125% 缩放，AppliedDPI=120），交互 Console 会话可用真实按键注入。
- .NET SDK 8.0.425；pwsh 7.6.6；Inno Setup 6.7.3（用户安装路径，经脚本自动发现）。
- 已装中文输入法：微软拼音（Microsoft Pinyin）、微信输入法（WeType）。本轮测试线程只激活 0804 zh-CN 布局（实测 HKL=0x08040804），未改用户默认输入法。
- CLI：Claude Code 2.1.287、Codex CLI 0.158.0、PowerShell 7.6.6。

## 结果一览

| 项目 | 结果 | 证据 |
| --- | --- | --- |
| TerminalImeTests + TerminalInputTests | 30/30 通过 | `acceptance/ime-input.trx`、ime-input-test.log |
| 真实 ConPTY 编辑（Claude/PSReadLine/Codex/分屏弹出光标） | 首轮 5/6（Codex 等待条件与 0.158.0 界面不匹配）；修正等待条件后通过 | `acceptance/real-editing.trx`、`codex-editing.trx` |
| Codex 提示词识别回归 | 5/5 回放通过（新 UI 无 model: / 旧 UI / 登录 / 信任 / 光标他行） | `acceptance/codex-editing.trx` |
| 系统 IME 真实验收 `--ime-acceptance` | 5/5 通过，退出码 0；候选窗位置经逐张人工视觉审核确认 | `acceptance/ime-acceptance.json` + `ime-visual-review.json` |
| 官方插件验收 `--official-plugin-acceptance`（新 v0.4.1 包目录） | 11 项全部通过 | `acceptance/official-plugins.json` |
| OfficialPluginTests/PluginReviewFixTests/TerminalVtParsingRegressions/MainViewModelReviewFixTests | 32/32 通过 | `acceptance/plugins-vt.trx`、plugins-vt-test.log |
| 打包 | 便携 ZIP + 安装 EXE 生成；app smoke 打开/正常关闭 | `v0.4.1/publish-windows.log`、zip 清单、smoke 输出 |

## 真实 CLI 编辑

`TERMINALHUB_CLAUDE_PATH` 指向本机 claude.exe（2.1.287）、`TERMINALHUB_CODEX_PATH` 指向 codex.cmd（0.158.0）、`TERMINALHUB_CLI_CWD`=仓库根目录（已信任）。

- Claude Code 真实 ConPTY：中文输入、左/右跨宽字符、Home/End、resize 后光标保持、多行粘贴，通过。
- PowerShell 7 PSReadLine 真实 ConPTY：中文、方向键、多行粘贴、resize，通过。
- Codex 真实 ConPTY：首轮因等待条件仍匹配旧版 `model:` 行而未验证（0.158.0 界面只有 `>_ OpenAI Codex` 头与 `› Ask Codex to do anything`）。`WindowsCodexEditingTests.HasEditingPrompt` 改为按帧判定：必须有 OpenAI Codex 头、无登录/信任文案、可见 VT 光标位于 `› ` 行箭头后第 2 格且箭头后内容为空或占位文案。修正后**真实 Codex 中文输入、跨宽字符方向键与 resize 通过**；登录与信任断言保留。新增 `CodexPromptRecognitionTests` 纯回放 5 场景。
- SplitPopoutCursorTests 3 项：通过。

## 系统 IME 验收（`--ime-acceptance`）

入口：`tools/TerminalHub.DesktopMeasurements`（`ImeAcceptance.cs` + `ImeAcceptance.Scenarios.cs` + `ImeNative.cs`）。生产 MainWindow + 两个真实 pwsh ConPTY（PSReadLine、固定 `IME> ` prompt、`-NoProfile`），隔离 SettingsStore。该模式渲染后端改为 `Software+RedirectionSurface`（仅本工具此模式；默认 GPU 合成下窗口像素不在 BitBlt 可读帧缓冲内，此前截图抓到的是背后窗口内容，已作废删除）。全部按键经 SendInput 原子 batch 注入，每个 batch 前严格校验 `GetForegroundWindow==测试HWND`，不符即中止该 case，不从其他窗口抢回前台、也不放行 IME 宿主前台；zh-CN 布局仅本线程激活并在 finally 恢复。截图 = BitBlt+CAPTUREBLT 真实屏幕像素，严格裁剪测试 HWND 窗口矩形，拍照前同样校验前台且先 `DwmFlush`。组合只用真实字母键触发，已提交文字用 KEYEVENTF_UNICODE。退出码精确映射：任一 fail 或异常=1，partial/unverified/aborted=2，全过=0。

**最终结果：5/5 通过（EXIT=0）。** 输入法界定为「系统中文输入法」：本机装有微软拼音与微信输入法，HKL=0x08040804 不能证明具体 TSF profile，`ImmGetDescription` 对活动 HKL 返回空，未独立核实为哪一个。

| case | 状态 | 实测事实 |
| --- | --- | --- |
| empty-input | pass | 真实 n/i → `_preedit='ni'`；Space 提交后 PTY 输入行出现 CJK 文字；CursorRectangle/PointToScreen/稳定帧记录 |
| enter-does-not-submit | pass | 组合中 Enter：preedit 结束、无新 prompt；候选确认 Enter 提交原始 `ni` 属预期 |
| cjk-arrows | pass | `ab中文cd` 后 Left×3→end-4、Right→end-2；CursorRectangle 与稳定帧逐格一致；行中组合后 Escape 取消 |
| right-edge | pass | 139 列填满至 col 138，组合在右边界出现；Escape 取消未提交 |
| split-font-popout | pass | `GetPane(1)`=ime-b，pane 同步与焦点确认后组合落在该窗格视图（无其他视图持有 preedit）；失焦清除；字号 18 组合正常；弹出窗组合、关闭后同 PTY 存活回列表、收回后可再组合 |

**截图与候选位置（已人工审核）**：8 张严格 `GetWindowRect` 裁剪的真实屏幕截图由人工逐张核对，结论见权威记录 `acceptance/ime-visual-review.json`：原生测试窗确实可见（非背后浏览器内容）；empty 输入、`ab中文cd` 中段编辑、右边界组合换行、右侧 ime-b 分屏、字号 18、独立弹出窗六处均确认候选条跟随组合光标位置；Enter 图无新 prompt 与功能记录一致。截图按约定在审核后删除，本目录不再保留 PNG。此前 union+扩边且未校验前台、GPU 合成下的旧截图已全部作废删除。JSON 中各 case 的 `NeedsVisualReview=true` 为生成时标记，人工结论以 `ime-visual-review.json` 为准。

**关于首轮分屏"组合投递到非焦点窗格"的澄清**：已确认为验收时序干扰，非产品 bug。`FocusPane→Activate→ActiveChanged` 经 `Dispatcher.Post` 才同步 `vm.ActiveSession`，旧脚本在其后立即读取拿到的是旧活动会话，再 `Focus` 又经 `FocusTreePane` 改焦，产生错配观测。修正为 `vm.GetPane(1)` 固定目标 + 等待 `ActiveSession/FocusedPane/FocusManager` 全部同步后，组合正确落在焦点窗格。旧记录保留于 `acceptance/initial-ime-acceptance.json`、`run0-ime-acceptance.json`、`run2-ime-acceptance.json`（其中 run2 为严格前台防护下两 case 因前台非测试窗被中止的一轮：cjk-arrows 中止时组合正活跃、split 中止于注入前，抢占者未独立确认，防护按预期中止而非继续打字）。

**另一次观测**：早前一轮 `empty-input` 首次按键未观察到 composition（字母直进 shell），重试/复测后组合正常，**原因未确认**；代码保留一次有界重试并在 facts 中如实记录首轮事实，本轮一次通过。另有单轮前台被外部抢占触发防护中止（见上 run2），同样如实标记 aborted。

**验收边界（未验证项）**：
- 候选位置确认仅覆盖**单屏 125% + 工具 Software+RedirectionSurface 渲染**；生产默认 GPU 后端下候选窗位置未验收。
- 具体 TSF 输入法 profile 未确认（`ImmGetDescription` 返回空）；微软拼音与微信输入法未分别覆盖，不能确认本轮对应哪一种。
- 多显示器、其他 DPI 未测，不能由单屏 125% 外推。

## 打包与插件

- `artifacts/v0.4.1/app/`：dotnet publish win-x64 self-contained 产物；`TerminalHub.exe` FileVersion=0.4.1.0、ProductVersion=0.4.1+b16a76e。
- `artifacts/v0.4.1/TerminalHub-windows-x64-preview.zip`：payload 已列清单核对（exe+4 pdb+LICENSE+QUICKSTART+CHANGES+PERFORMANCE+3 个离线文档页，无 settings/.ssh）。
- `artifacts/v0.4.1/installer/TerminalHub-Setup-0.4.1.exe`：ISCC 6.7.3 编译成功；语言文件若干 Inno 6.7 兼容性 warning（既有 isl 文案，非阻塞）。**未实际安装**——同 AppId 会修改用户现有安装注册信息；此处只验证编译生成。
- `artifacts/v0.4.1/official-plugins/`：三个插件目录+ZIP，由 `build-official-plugins.ps1 -OutputDirectory`（新增可选参数）产出；`--official-plugin-acceptance` 经 `TERMINALHUB_OFFICIAL_PLUGIN_DIR`（新增）指向本目录验收通过。
- smoke：隔离 `TERMINALHUB_SETTINGS_DIR`+唯一 `TERMINALHUB_INSTANCE_NAME` 启动打包 exe `--mock`，真实窗口打开、`CloseMainWindow` 正常退出 exitCode=0；临时配置目录已删。

## 未执行项

安装包未实际安装、默认 GPU 渲染下候选位置、两种已安装输入法的分别验收、多显示器/多 DPI、Linux 包与实机、真实 SFTP 远端、性能测量、GitHub 发行上传均未在本轮范围。
