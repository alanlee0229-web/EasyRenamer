# BatchRenamer UI / 技术基线 V0.2

## 本阶段目的

V0.2 同时验证两件事：

1. UI 更新链在 7 / 1,000 / 20,000 项级别是否保持可响应；
2. Desktop SaaS 视觉语言是否适合长期高频使用。

**本原型仍明确不执行真实文件重命名。**

## 已锁定技术路线

- Language: C#
- Framework: .NET 10 LTS
- GUI: WPF
- WPF-UI: 4.3.0（提供 FluentWindow / Theme 基础，不采用 Mica/玻璃拟态主视觉）
- Architecture: Core pure computation + WPF ViewModel + 少量 Drag/Drop code-behind
- Target: Windows x64
- Final delivery: self-contained Portable EXE 优先

## V0.2 UI 决策

- 64px 窄导航栏：只提供真实桌面工具需要的入口，不复制复杂 SaaS 菜单。
- Page Header：标题 / 简述 / Add CTA，取代传统按钮工具条。
- 主区域：DataGrid 仍为视觉与空间主体。
- DataGrid 工具行：搜索、问题筛选、排序、上下移动集中在同一层。
- 右侧：单一连续 Rename Rules 面板，不再堆叠多张 Inspector 卡片。
- 底部：状态计数、Preview latency、安全模式、最终执行 CTA。

## V0.2 Preview / Collection 架构

- `BatchRenamer.Core.PreviewEngine` 是纯计算层，可从 worker thread 调用。
- 规则输入 120ms debounce。
- UI thread 只制作 snapshot 和 apply batch result。
- 每行只替换一个 `PreviewRowState`，避免多属性通知风暴。
- `ItemsView.Refresh()` 不再属于普通 Preview pipeline。
- 搜索过滤 160ms debounce。
- `BulkObservableCollection` 为排序/批量替换提供单次 Reset。
- CheckBox binding 初始化不得调用 CollectionView Refresh。

## V0.2 已实现交互

- 默认 7 条演示数据
- 添加真实文件（仅读基本元数据）
- 添加文件夹本身（不递归）
- Explorer 文件拖入
- DataGrid 多选
- 拖动重排
- 上移 / 下移
- Natural Sort / 扩展名 / 时间 / 大小排序
- 排序撤销
- 参与改名 Checkbox + 表头全选/全不选切换
- 文件搜索
- 只看问题
- 基础名称 / 原名组合 / 前后缀
- 单一连续编号
- 字面量查找替换
- 大小写
- 后台 Preview
- Preview latency
- 20,000 合成数据隐藏压力入口

## 尚未实现

- NamespaceIdentity / IPathSemanticsProvider 正式实现
- Rev.A 完整 ValidationEngine
- VacatingSourceSet
- RenamePlan freeze
- Two-phase Rename
- Journal / Crash Recovery
- Undo 文件系统事务
- Preset persistence
- 正式历史 / 设置页
- 真实 Execute 按钮

真实写盘能力必须在 UI 性能通过后按 Rev.A 分层接入，不能从 View code-behind 拼接。
