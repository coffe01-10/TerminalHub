# PR #55 评论核对与修复

2026-10-02，基于 `feat/inspector-default-visible-mockup@eb4cda6`。

已读取 PR 的讨论、审查、行内评论，以及仓库提交评论的全部分页。PR 自身三个评论入口为空；当前分支历史包含 17 条提交评论。用户明确选择「按评论恢复 Windows 旧行为」，因此 Windows 的默认值、文件打开、滚轮、响铃、退格和两项卡片视觉按评论恢复；Linux 对应行为保留。已有配置中的显式选择仍优先。

## 历史评论逐条处理

| 提交评论 | 核对与处理 |
| --- | --- |
| [cb5fc1a：慢路径拖拽](https://github.com/coffe01-10/TerminalHub/commit/cb5fc1ad7c5b0dfd90ad63a7e8912a8c5f915501#commitcomment-203050976) | 等待文件句柄时捕获指针；松手、丢失捕获或关窗取消本次拖拽。保留 6px 拖动阈值和单击选中。回归覆盖列表外松手与迟到的句柄。 |
| [9d3b837：同步图片解码](https://github.com/coffe01-10/TerminalHub/commit/9d3b8372ff516a3d79cccdcc7d9c6df1f33a903d#commitcomment-203050979) | 图片读取、元数据和解码转到后台；切换选择或释放 VM 后丢弃并释放迟到位图。回归验证加载不占 UI 线程，旧图片不能覆盖文本或在关闭后出现。 |
| [7754a3e：静默跳过目录链接](https://github.com/coffe01-10/TerminalHub/commit/7754a3e6562dcb33ed33f9c3455ae2341c6f5445#commitcomment-203050980) | 保留跳过子目录链接的复制语义，并在导入结果显示跳过数量；真实目录联接回归确认普通文件仍被复制。 |
| [8d35486：多段扩展名去重](https://github.com/coffe01-10/TerminalHub/commit/8d35486bb2274c0f9b6eb56dcee054984fa89494#commitcomment-203050982) | 核对为命名风格备注；去重仍保留内容并避免覆盖。保持按最后一个扩展名加后缀的既有约定，不把风格备注当作数据丢失缺陷。 |
| [c4a9a44：Search 标签索引](https://github.com/coffe01-10/TerminalHub/commit/c4a9a44db18a15cb781ced68f28f533d0a6d6813#commitcomment-203050985) | 用 `DashboardViewModel.SearchTabIndex` 统一快捷键、搜索刷新和 XAML 面板可见性引用。 |
| [5572e04：弹出会话可空警告](https://github.com/coffe01-10/TerminalHub/commit/5572e049f7daaf92280f91ad714d9cdb7d78e48b#commitcomment-203050988) | 菜单构建取非空 Session 后读取名称和目录；设计器构造的空 Session 不再被解引用。 |
| [6e515eb：旧审查总结](https://github.com/coffe01-10/TerminalHub/commit/6e515eb122f487d039f8ba28ef3b781d90ccdaac#commitcomment-203051015) | 重新验证总结中的结论。原有导入保护遗漏目标路径联接，已用实际文件系统复现并修正；“无需修改”结论未沿用。 |
| [5dc0886：中文退格](https://github.com/coffe01-10/TerminalHub/commit/5dc0886c85cd84f2ba20f4586f3b6e6047311997#commitcomment-203051923) | Windows 恢复宽字符续格吸附，Linux 保留逐列退格。真实 ConPTY PowerShell 输入 `ab中文cd`、两次左移、删除“文”，核对内容和编辑光标。 |
| [bfe69c7：BEL 通知](https://github.com/coffe01-10/TerminalHub/commit/bfe69c74b7de2f76be79df1ab7f2c8fc4c6c3d7a#commitcomment-203051926) | Windows BEL 恢复静默：不弹通知、不额外标记未读，也禁用最新提交加入的窗格闪烁；Linux 保留响铃提示。 |
| [1863413：文件 Enter／双击](https://github.com/coffe01-10/TerminalHub/commit/1863413e99141de329bd035859d6b96cf1dc1ce9#commitcomment-203051930) | Windows 恢复应用内预览；右键“外部打开”仍可用。回归使用真实文本文件并记录外部打开回调。 |
| [3b8ccea：Ctrl+滚轮](https://github.com/coffe01-10/TerminalHub/commit/3b8ccea8d94b79425f6b9ceccb192bcbf6007aef#commitcomment-203051931) | Windows 恢复历史滚动，Linux 保留缩放。测试同时发现向上滚轮反向修改历史偏移，已修正；鼠标追踪 CLI 仍优先接收事件。 |
| [ca3e19d：检查器默认可见](https://github.com/coffe01-10/TerminalHub/commit/ca3e19db09cee75ca242a4e760dc9921a3dd9179#commitcomment-203051936) | Windows 从未设置时恢复关闭；显式 true／false 保留；继续省略 null 序列化以兼容旧设置读取。 |
| [7706244：Output 默认展开](https://github.com/coffe01-10/TerminalHub/commit/7706244afcdf16bffa01aea1957982782ca1bd7b#commitcomment-203051939) | Windows 默认关闭；用户保存的 true 仍生效。 |
| [fc29657：坞默认与文案](https://github.com/coffe01-10/TerminalHub/commit/fc296571497302b642b0ddf791c2a80fad8e686a#commitcomment-203051940) | Windows 默认自动隐藏，闲置标题恢复 `Deploy`；保留显式显示选择和实际打包进度。 |
| [fb8d9e5：活动卡光晕](https://github.com/coffe01-10/TerminalHub/commit/fb8d9e59edeccc04e1d25c7093006fc3ddb28e96#commitcomment-203051944) | Windows 四主题恢复旧投影参数；Linux 保留光晕。 |
| [c39b49f：侧栏堆叠](https://github.com/coffe01-10/TerminalHub/commit/c39b49faa645be05aee191a5522e8d8cfb624ed6#commitcomment-203051946) | Windows 卡片重叠量恢复 0，卡片间距恢复 16px，活动边框恢复 1px，状态保持原文本样式；布局和拖拽使用相同值。Linux 保留堆叠和标签胶囊。 |
| [4ab2b63：Windows 兼容总结](https://github.com/coffe01-10/TerminalHub/commit/4ab2b6329c3f060e365e9789a64be73ede3cde31#commitcomment-203051998) | 上述九项逐条处理；保留总结所述平台隔离、路径识别改善和单实例行为，没有改动 ConPtySession。 |

## 额外确认的缺陷

- 导入目标或目标父目录是目录联接时，原保护会允许自复制，甚至递归生成自己的副本。现在比较解析后的源和目标路径；回归覆盖同目录文件、目录本身及链接父目录。
- Linux Ctrl+Insert 的复制分支原先被 Insert 转义序列分支挡住，现先处理复制。Windows 按用户要求保留基准的 CLI Insert 序列，不用新复制行为覆盖旧快捷键；Ctrl+Shift+Insert 在两平台继续发送原序列。
- Linux 空 SSH 列表按 Esc 原先隐藏唯一添加入口，现保留表单和草稿、焦点返回终端。Windows 表单按基准一直展开，Enter／Esc 不触发新增保存、连接或收起行为。
- 两个侧栏各自按窗口宽度限制仍会挤没中间终端。现在联合分配宽度，窗口缩小时保留中心控件空间且不覆盖保存尺寸；回归覆盖窗口收缩和两个侧栏拖动。
- Output 缩小后拖动原先从保存高度起算，可能跳动。改从当前显示高度开始拖动。
- 新回归确认：滚动条的 `IsVisible` 绑定会被历史长度／行数变化时的控件内部更新覆盖。改为绑定 `ScrollBar.Visibility`，避免 Windows 隐藏的覆盖条重新出现，也保留非 Windows 的备用屏幕隐藏逻辑。此行为已核对 [Avalonia 11.3.2 源码](https://github.com/AvaloniaUI/Avalonia/blob/11.3.2/src/Avalonia.Controls/Primitives/ScrollBar.cs#L158-L169)。

## Windows 基准兼容性补充

基准为用户主工作区 `main@1e4a9c4`。逐项对比产品代码，不只依据历史评论：

- 新字段缺省时继续使用 Windows PowerShell、关闭检查器／Output、自动隐藏坞。旧设置中的显式选择保留；保存的检查器选择仍写成旧版本可读取的 JSON 布尔值。
- 文件列表默认继续显示隐藏项和点文件。单击只选中，不自动切换预览；Enter／双击仍在应用内预览，显式外部打开入口保留。
- Windows 底部坞恢复旧版四入口（监控、SSH、日志、Deploy）；新增的新建／设置按钮仅在非 Windows 显示，原工具栏新建和顶部设置按钮保留。Windows 保留原两行工具栏的 84px 高度、分屏／弹出文字、恒定窗口标题和无新增边框的终端区域。新增滚动条仅在非 Windows 显示，避免覆盖 Windows 终端右侧鼠标操作区域；原滚动和回到底部入口仍可用。
- Windows 保留 Ctrl+Insert、Ctrl+Shift+A、Ctrl+Shift+F 的原处理，不让新增复制／全选／搜索截走 CLI 按键。Ctrl+滚轮继续滚动历史。
- Windows 保留侧栏拖动排序；中键松开不关闭会话，横向拖动不自动弹出，空白处双击不自动创建会话。
- Windows SSH 表单保持展开，保留显式添加／更新／连接按钮。进程表默认保持监视器原来的 CPU／内存排序结果及同值顺序，点击列头后才使用新增排序。
- Linux 单实例管道重连仅在 Linux 启用，Windows 保留原发送失败返回行为。ConPtySession、SessionManager、TerminalSessionModel 和 Program 与基准没有差异；Windows 环境变量和 PowerShell 集成参数保持原处理。

新增正式 `WindowsCompatibilityTests` 覆盖旧配置读写、文件选择／预览、SSH Enter、实际工具栏布局、侧栏鼠标手势、滚动区域和快捷键焦点；与已有分屏／弹出／中文输入用例一起运行。

## 人工试用反馈补修

- 用户截图指出底部多了新建会话按钮。本轮按 Windows 基准恢复四入口坞，同时隐藏新增的底部设置入口；原有新建和设置入口继续可用。
- Grok 颜色差异来自启动测试窗口时继承了工具进程的 `NO_COLOR=1`，而用户／系统环境中均未设置该变量。Windows `TerminalPalette` 与基准无差异，未修改产品代码忽略用户的 `NO_COLOR` 选择。测试启动现仅去掉工具带入的值，用户显式环境值仍保留。
- 本机 Grok 1.0.46 的真实 ConPTY 启动捕获通过：清除工具变量后，100×28 屏幕的 2800 格背景均为 RGB `#141414`，文字包含多个 RGB 色值。没有发送输入、确认信任或提交模型请求；临时原始输出和颜色统计已立即删除。
- 本轮布局、坞、Deploy、Windows 兼容性和颜色回放组跑过 63 项，唯一失败为仍要求六入口的旧断言。更新为 Windows 四入口／非 Windows 六入口，并验证顶部设置按钮后，11 项相关回归全部通过；未重复无关的全量测试。

## 验证

- 本轮产品和正式回归编译通过，没有编译警告；弹出会话菜单原有的 CS8602 警告已消除。
- 本轮全套运行 610 项：606 通过、3 跳过，唯一失败为新增 Windows 鼠标区域回归，发现滚动条在历史增长后重新显示。已修复其可见性绑定，随后定向复验受影响的主终端／分屏／弹出／布局／输入用例，共 70 项全部通过（包括 7 项新增 Windows 兼容性回归）。全量加定向复验最终覆盖 610 个唯一用例，607 通过、3 跳过；修复后未重复无关的全量测试。
- 上一轮修复的文件导入联接、拖拽取消、迟到图片释放、Output 高度夹紧和侧栏联合宽度回归均在本轮全量中通过。
- 原全量跳过项：未启用性能测量；当时未提供本地 Claude 和 Grok 的测试路径。本轮已定位本机 Grok 并补做真实颜色捕获；性能测量和真实 Claude 验证仍未执行。没有自动接受信任提示或提交 AI 请求。
- 真实 ConPTY PowerShell 中文编辑、输出流和订阅者失败恢复测试通过；界面相关验证使用 Avalonia Headless 和 Mock PTY。
- Linux 原生 GUI、真实 Explorer 拖放及系统输入法候选窗未在本机验证。

## 合并结果核对

2026-10-02 拉取远端后，PR #55 仍打开，目标为 `main@fb8d9e5`，该目标已包含在 PR 分支历史中。因此将当前修复分支合入这个 main 时，产品代码与修复分支一致，没有额外上游改动需要解决。提交后用 Git 的合并计算核对结果，GitHub 最终状态见 [PR #55](https://github.com/coffe01-10/TerminalHub/pull/55)。

Windows 基准检查与原生／Headless 验证范围如上。主工作区和用户配置没有修改；临时 stash 和 Grok 原始捕获已清除。本报告与源码、正式回归一起提交，未包含本地测试配置或程序产物。
