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

## 相关设计文档

- [Validation Architecture](VALIDATION_ARCHITECTURE_V0.4.md)
- [Rename Planner Architecture](RENAME_PLANNER_ARCHITECTURE_V0.5.md)
- [Transaction Foundation](TRANSACTION_FOUNDATION_V0.6.0.md)
- [Public Build Purity Gate](PS02_PUBLIC_BUILD_PURITY_GATE.md)
