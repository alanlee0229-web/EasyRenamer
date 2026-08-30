# Roadmap

v1.0 是当前 Core Product。除 v1.0 外，以下版本与能力全部为 **Planned**，不代表当前产品已经实现，也不构成发布日期承诺。

## v1.0 — Core Product（Current）

- Live Preview 与 Windows 文件名 Validation。
- 基础名称组合、前缀/后缀、连续编号、文字查找替换、大小写转换。
- Natural Sort 与手动排序。
- Frozen RenamePlan 与 two-phase Transaction。
- Durable Journal、Rollback、Startup Recovery、Undo。
- Portable Windows x64 application。
- Public / Internal 双构建与 Public Build Purity Gate。

## v1.1 — Power User（Planned）

- Template Engine。
- Regex。
- Rule Chain。
- Rule Scope。
- Grouped Sequence。
- Recursive Folder。
- 更丰富的日期、父目录和扩展名规则。

## v1.2 — Personalization（Planned）

- Dark Mode。
- 可保存的个人设置与规则预设。
- 更灵活的工作区布局与显示偏好。

## v1.3 — Automation（Planned）

- CLI。
- 可审计的批处理自动化入口。
- 自动化预检与无人值守失败策略。

任何自动化仍必须经过 Validation → RenamePlanner → Frozen RenamePlan → Transaction。

## v1.5 — Extension Platform（Planned）

- Plugin SDK。
- Extension API 与能力权限模型。
- Preset ecosystem。

扩展只能生成候选名称或规则结果，不得绕过正式安全链直接 mutation 文件。

## v2.0 — Intelligent Renaming（Planned）

- 可选的智能命名建议。
- 可解释、可预览、可撤销的辅助工作流。
- 明确的数据隐私与离线/联网边界。

智能能力不得直接执行文件 mutation，也不得跳过用户确认、Validation、Planner 或 Transaction。
