# 工作台操作交付记录

2026-10-02：完成 TODO v0.4.0 第 1、3、5、6 项源码开发。当前安装版和发行包仍为 v0.3.4；本次未提交、推送或发布。

## 操作入口

| 功能 | 使用方式 |
| --- | --- |
| 跨工作区移动 | 拖动会话卡片到目标标签；提示显示目标名称，直接松开追加到末尾。悬停约 500ms 切到目标工作区，再拖到目标卡片前松开可指定顺序。Esc 或在无效位置松开取消。 |
| 最近使用切换 | 默认按住 Ctrl，再按 F6。继续按 F6 向前，Shift+F6 反向；松开修饰键确认，Esc 取消。列表显示会话名称、所属工作区和实时缩略图。 |
| 语言 | 设置 → 外观 → 界面语言，选择简体中文、English 或跟随系统。保存后启动时继续应用；系统语言为中文时使用中文，其他语言使用英文。 |
| 撤销／重做布局 | 会话菜单与命令面板提供入口。默认 Ctrl+Alt+Z 撤销、Ctrl+Alt+Y 重做；菜单显示将要恢复的操作名。 |

上述三个快捷键均可在设置 → 快捷键更改或清空。Ctrl+Tab 继续按侧栏顺序切换，Ctrl+Z 保留给终端；Alt+Tab 保留给系统。MRU 的基础快捷键需包含 Ctrl、Alt 或 Meta，Shift 专用于反向切换，并参与冲突检测。

预览候选时保留当前工作区、活动终端和输入，不向 Shell 发送预览按键或文本。确认后切到目标所属工作区并恢复终端焦点；独立窗口候选会激活原窗口，也可从独立窗口发起切换。关闭会话会移出候选。

## 迁移、保存与布局历史

迁移继续使用同一个会话、PTY 和模拟器，保留屏幕、历史、目录、名称、标签、颜色、置顶与分组属性。源分屏优先用剩余终端补位；会话不足时四窗格退回双窗格、双窗格退回单窗格。固定侧栏和自动呼出侧栏使用同一套拖动入口。

归属、顺序、两侧分屏比例和活动窗格写入已有工作区配置。重启恢复这些配置并创建新 Shell 进程，布局历史不跨启动保存。

历史记录拆分／合并、最大化／恢复、窗格交换、卡片排序、比例调整及跨工作区迁移；连续一次卡片拖动或分隔条拖动记为一步。撤销迁移同时恢复两侧工作区。新布局操作清空重做分支，最多保留 100 步，避免长期保存大量布局引用。拆分新建的终端在撤销后仍留在侧栏中运行。

历史仅恢复工作台布局，不执行 Shell 撤销、不还原 CLI 内部状态。关闭会话／工作区或把会话弹出独立窗口会清空旧布局历史，避免引用已关闭或已转移所有权的终端；不会通过撤销重新启动它们。

## 语言资源与扩展接口

内置资源位于 `src/TerminalHub.Core/Localization/Messages.json`；每条资源包含稳定 ID、中文原文和英文。Avalonia 的 `TextExtension`、`TranslateExtension` 和 `UiText.Binding` 随语言修订即时更新。应用生成的日志与原始 PTY 日志分开标记，文件内容和命令文本用明确的原文包装，避免把恰好叫“设置”或包含“运行中”的用户内容翻译掉。

`TerminalHub.Core.Localization.Localizer.Current` 是公开语言资源契约，可供后续插件 SDK 或独立扩展模块引用：

```csharp
var locale = Localizer.Current;
locale.RegisterResources("sample.module", "zh-CN",
    new Dictionary<string, IReadOnlyDictionary<string, string>>
    {
        ["zh-CN"] = new Dictionary<string, string>
        {
            ["title"] = "示例面板",
            ["empty"] = "暂无记录"
        },
        ["en"] = new Dictionary<string, string> { ["title"] = "Sample panel" }
    });

var language = locale.Language; // zh-CN / en
var title = locale.GetModuleText("sample.module", "title", "Sample panel");
locale.LanguageChanged += RefreshModuleUi;
// 停用模块时解除事件订阅并注销资源。
locale.LanguageChanged -= RefreshModuleUi;
locale.UnregisterResources("sample.module");
```

Avalonia 模块可直接绑定：

```xml
<TextBlock xmlns:loc="using:TerminalHub.App.Localization"
           Text="{loc:Text title, ModuleId=sample.module, Fallback='Sample panel'}" />
```

资源查找按当前语言 → 模块声明的默认语言 → 调用方 fallback 回退。UI 模块在 UI 线程注册和注销；`LanguageChanged` 和 `Revision` 可用于刷新自己的控件。此接口不包含第 4 项中的插件发现、加载、启停管理和完整 SDK 工程。

## 验证结果与范围

- Debug 编译通过。NuGet 的漏洞数据请求因当前网络不可访问产生 NU1900 警告，已有依赖支持正常编译。
- 新增工作台场景覆盖双工作区迁移和撤销／重做、原 PTY／屏幕复用、源布局退回、固定与自动呼出侧栏实际指针拖动、悬停与取消、分隔条一次拖动、窗格交换、快捷键、MRU 反向和独立窗口、预览输入隔离、菜单／下拉框／工具窗即时语言更新、原文保留、模块语言回退以及配置重启恢复。
- Windows 真实 ConPTY：PowerShell 输出迁移前标记，迁移、撤销、重做并切到英文后，原进程仍运行且接收输入，输出迁移后标记。测试只启动自己的临时 Shell，结束时释放，不触碰用户运行中的实例。
- 四套主题的英文主窗口／设置、MRU 和工作区工具已做 Headless 渲染检查；工具导航英文长文字换行，设置页签可见，独立终端窄窗口保留返回按钮。截图和临时配置已清除。
- 首批相关回归 97/97 通过。扩大到文件、日志和工作区工具后，120/126 通过：五个已有文件导入用例受当前权限影响，解析 `C:\Users\chen\AppData` 链接时返回 Access denied；一个搜索用例仍断言旧文案“1 处匹配”，当前原有代码显示“1 行匹配（每会话最多 200 行）”。本轮没有更改导入路径逻辑或搜索计数文案，没有将该批描述为全通过。

Headless 验证使用原生 Avalonia 输入、布局和绘制；真实 ConPTY 测试证明进程连续性和输入。用户桌面的实鼠标／系统输入法候选窗、Linux 实机及当前安装版尚未验收，本次不宣称这些层面已更新或通过。
