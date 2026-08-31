# Safety Architecture

BatchRenamer 将“生成新名称”和“修改文件系统”明确分离。界面、规则、未来 CLI 或扩展都只能产生候选名称；真实 mutation 只能走同一条正式链。

## 永久边界

```text
Candidate name
    ↓
Validation
    ↓
RenamePlanner
    ↓
Frozen RenamePlan
    ↓
Preflight
    ↓
Transaction
```

禁止从 UI、脚本、插件、CLI、智能能力或 QA 工具直接建立第二条文件移动路径。

## Validation

Validation 在计划冻结前拒绝无效字符、Windows 保留名、重复 Target、外部占用、危险尾部字符及其他不可执行状态。Validation 通过只表示候选关系可进入 Planner，不代表可以跳过后续检查。

## RenamePlanner 与 Frozen Plan

RenamePlanner 只接收最终 Source → Target 关系，并生成不可变计划。计划冻结：

- Source、Temp、Target 路径。
- 文件类型与可用的 FileIdentity。
- 路径比较语义。
- TransactionId、时间与 schema。

执行阶段不得根据 UI 当前状态重新计算名称。

## Preflight 与身份

每次执行前重新核对 Source、Target、Temp 和 FileIdentity。缺失证据、身份变化、未知路径语义或外部占用都必须 fail closed。

## Two-phase Transaction

```text
Phase 1: Source → Temp
Phase 2: Temp → Target
```

Temp namespace 让 swap、cycle 和 case-only rename 不依赖覆盖行为。每次实际 move 前后写入 durable `INTENT` / `DONE` Journal；目标存在时绝不静默覆盖。

## Rollback、Recovery 与 Undo

- Rollback 根据 Frozen Plan 与当前文件系统证据反向移动。
- Startup Recovery Gate 在未解决事务存在时阻止新事务。
- 只有 `CanAutoRollback=true` 且证据无歧义时才允许自动恢复。
- Undo 使用已完成事务的 Frozen Plan 和 durable Journal，不重新推测旧规则。
- 外部修改、身份不明或占用冲突会关闭自动恢复/撤销，而不是覆盖现有文件。

## 并发与保留

Transaction session lease 和 catalog lease 防止多个进程并行操作同一事务目录。未解决、manual 或 busy 状态不会被自动清理；终态记录按保留策略处理，但不得触碰用户文件。

## Public / Internal 边界

Internal QA 只能调用正式 Preview、Planner 与 Transaction API。Release-Public 在编译阶段排除 `InternalTools`，并由 Public Build Purity Gate 验证类型、命令、资源、依赖、身份和发布目录。

## 身份与路径语义永久约束

- `NamespaceIdentity` 用于导入去重、目标路径判等与批次冲突；`FileIdentity` 只用于执行前 / Recovery 的对象替换 Guard，不得互相替代，也不使用文件内容 Hash。
- 同一 NTFS 文件对象的多个 Hard Link 是多个独立目录项，允许同时导入并分别改名。
- 路径比较必须经 `IPathSemanticsProvider` 获取目录级大小写语义，禁止全局写死 `OrdinalIgnoreCase`；无法可靠确认语义时保守失败（fail closed / 结构化 `FILESYSTEM_SEMANTICS_UNKNOWN`），不伪装成可靠结果。

## Frozen Plan 永久约束

- Temp 必须位于 `.~br-` 保留 namespace，与 Source / Target 不同、批次内唯一、执行前确认不存在；普通 UI 永不展示 TempPath。
- Plan invariants：Source / Temp / Target 各自唯一；Temp 不得与任何 Source / Target 冲突；V1 中三者必须同目录；`A↔B`、三向循环的 Source / Target 交叉合法；Ordinal 从 0 连续。
- `plan.json` 持久化必须走"临时文件 → Flush(true) → 原子替换 → 回读校验 SHA256"流程；同一 TransactionId 目录已存在时拒绝覆盖。
- 任何 UI 状态变化（规则、勾选、导入、排序、拖动）必须立即废弃已准备的 Plan；Planner 计算期间输入变化时返回 `INPUT_CHANGED_DURING_PLANNING`，旧计划不得进入执行。
- `BatchRenamer.Transaction` 不依赖 WPF、不接收 `RenameRuleSet`、不重新生成目标名称。

## 相关权威文档

- [V1 Authority Freeze](../BatchRenamer_V1_Authoritative_Freeze_20260830_DualFlavor_Update.md)
- [V1 Engineering Qualification](releases/V1_ENGINEERING_QUALIFICATION.md)
