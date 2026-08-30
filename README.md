# easy重命名 / BatchRenamer

一个现代、安全、可扩展的 Windows 批量重命名工具。当前代码基线来自已通过 Release Qualification 的 V0.11.1.1，外部版本为 v1.0.0。

当前提供同源双构建 Flavor：

```powershell
dotnet build BatchRenamer.UIPrototype.sln -c Release-Internal
dotnet build BatchRenamer.UIPrototype.sln -c Release-Public
```

Internal 保留内部 QA；Public 在编译阶段排除 `InternalTools`。详细命令见 `docs/PS00_BUILD_FLAVORS.md`。

---

# 历史基线：BatchRenamer V0.11.1.1 — Compile Hotfix

V0.11.1 的 20k 性能优化方向不变，但严格 Release Build 抓到一个打包前遗漏：`BuildTransitionIndexes()` 调用了 `EnumerateTransitions()`，而重构时误删了该 helper，导致 `CS0103`。本版只恢复这一 helper，并保持四种 Frozen RenamePlan transition 与 V0.11.0 完全同义。

请直接重新运行：

```text
python tools\run_v1_release_gate.py
```

脚本第一步仍使用 `TreatWarningsAsErrors=true`，因此任何新的编译错误/警告会在 20k 文件压力测试开始前阻断。

---

# BatchRenamer V0.11.1 — 20k Stress Performance Hotfix

V0.11.0 的正式 20,000 项 Release Gate 暴露了一个只在真实大事务规模下才明显的性能缺陷：`JournaledRenameMutationFileSystem.ResolveTransition()` 在**每一次真实 move 前**重新枚举整个 Frozen Plan 的 4×N 条 transition。20,000 项完整 Execute 有 40,000 次 move，因此形成数十亿级 transition 比较/对象分配，表现为进入 `durable_execute` 后长时间没有进度。

V0.11.1 保持 V0.7.1 已冻结的 durable 协议不变：每个 INTENT / DONE 仍然 `Flush(true)` 后才返回。只做以下性能修复：

- Frozen transition 在 live transaction session 构造时一次性建立 exact / semantic O(1) lookup index；
- case-only rename 仍只允许 exact spelling 命中，禁止 IgnoreCase fallback 折叠方向；
- A↔B / cycle / rollback transition 的歧义检测仍保留；
- `PlanBoundTransactionJournalSink` 在一个 transaction session 内复用同一个 `events.jsonl` WriteThrough append stream，避免每一条 Journal event 都反复 Open/Close；
- **每一条 INTENT / DONE 仍然执行 `Flush(true)`，Crash Recovery 安全合同没有降级**；
- ReleaseStress 每 15 秒打印 `[alive]` heartbeat；move 开始后仍每 1,000 次显示吞吐，避免长 Preflight/Recovery scan 被误认为死锁。

V0.11.0 正在 `durable_execute` 卡住的旧压力测试可以直接停止；它只操作日志里显示的 `%TEMP%\BatchRenamer.ReleaseStress\<runId>` sandbox。停止后可删除整个旧 sandbox。正式 Gate 请改用本版重新运行 `python tools\run_v1_release_gate.py`。

---


V0.10.0.1 已完成 Release Candidate 基础链。真实 Windows publish 成功，但暴露 `RenamePlanIntegrity.cs` 两处 CS8602 nullable warning。V0.11.0 不增加 V1 功能，专门完成发布前最后的规模/严格编译审计：

- 修复两处 Release nullable warning，不改变 RenamePlan integrity 规则；
- Portable 发布默认 `TreatWarningsAsErrors=true`；
- 新增独立 Windows `BatchRenamer.ReleaseStressTests`；
- 用真实 TEMP sandbox 文件执行 20,000 项 Planner → durable Rename → Startup Scan → durable Undo → idempotent Undo；
- 自动检查 80,000 条 Execute Journal event、文件数量、内容、Temp 残留、Startup Gate；
- 新增 `tools/run_v1_release_gate.py`，一条命令完成 Strict Build + 全量 Smoke + 20k Stress + Strict Portable Publish；
- 成功后生成 `artifacts/release-gate/V1_RELEASE_GATE_MANIFEST.json` 和最终 SHA256。

Windows 最终 Gate：`docs/WINDOWS_ACCEPTANCE_V0.11.0.md`。

---

> **V0.10.0.1 compile hotfix**: fixes the V0.10 retention smoke-test local-name collision and nullable-analysis warnings only. Runtime transaction contracts are unchanged.

# BatchRenamer V0.10.0 — Release Candidate Foundation

V0.9.0.1 已完成普通 UI 真实 Rename + Undo Windows Gate。V0.10.0 不扩张 V1 功能面，开始进入发布候选收口：

- 事务完整元数据按冻结合同默认保留最近约 20 次；
- 放弃的 Prepared 计划采用宽限期清理；
- unresolved/manual/busy 状态永不自动清理；
- 新增 win-x64 Self-contained Single-file Portable 发布配置与 Python 打包脚本；
- 普通事务弹窗顺手统一为与主界面一致的浅色应用内 Dialog；
- Preview / Validation / Planner / Transaction / Recovery / Undo 安全合同保持不变。

Windows Gate：`docs/WINDOWS_ACCEPTANCE_V0.10.0.md`。

---

# BatchRenamer V0.9.0.1 — V0.9 Compile Hotfix

这是 V0.9.0 Safe UI Execute + Undo Integration 的最小编译修复版。功能与 V0.9.0 保持一致；仅修复首次 Windows 编译 Gate 暴露的 `System.IO.Path` 名称解析与 nullable-flow 问题。

Windows 验收继续执行 `docs/WINDOWS_ACCEPTANCE_V0.9.0.md`。必须先完成 0 Error 编译和全量 SmokeTests，再进行普通 UI 真改名 + Undo Gate。

---

# BatchRenamer V0.9.0 — Safe UI Execute + Undo Integration

V0.8.0 Transaction History + Durable Undo 已由 Windows 实机 SmokeTests 全量通过。

V0.9.0 是项目第一次把已经逐层验证的事务闭环正式接回冻结主界面：普通用户现在可以从 **“执行重命名”** 完成真实两阶段 Rename，并通过 **“撤销上次”** 使用 Frozen Plan + durable Journal 恢复。

本版没有重新设计基础 UI，只增加事务功能必需的状态与操作入口。

## 正式 UI 执行链

```text
当前视觉顺序 / 命名规则
        ↓
BuildFinalPlanAsync()
Fresh Preview + Final Validation
        ↓
用户明确确认
        ↓
TransactionCatalogLease（跨进程全局事务锁）
        ↓
Startup Recovery Gate 再检查
        ↓
Persist immutable plan.json
        ↓
TransactionExecutionOrchestrator
        ↓
INTENT → Source→Temp → DONE
        ↓
INTENT → Temp→Target → DONE
        ↓
Completed
```

如果真实执行在已经发生 namespace mutation 后失败，V0.9 command coordinator 不直接把半批次暴露给 UI，而是先联合 Plan + Journal + 当前文件系统重新分析；只有 `CanAutoRollback=true` 时才自动调用已经验证的 Recovery Orchestrator。

## 新增跨 TransactionId 并发保护

V0.7 的 `session.lock` 只保证**同一个 TransactionId**不能被两个进程同时操作。

V0.9 新增：

```text
transactions/.catalog.lock
```

`TransactionCatalogLease` 使用 live `FileShare.None` handle，使不同 BatchRenamer 进程不能同时启动两个不同 TransactionId 的真实 Execute/Undo。

这关闭了“两个程序窗口各自生成不同 Plan、同时操作相同文件”的并发边界。

## 正式 Undo

主界面底部新增最小必要按钮：

```text
撤销上次 | 执行重命名
```

`撤销上次` 只在 `TransactionHistoryService` 重新核对后存在 `CanUndo=true` 的 Completed transaction 时启用。

正式 Undo：

```text
TransactionCatalogLease
        ↓
Startup Gate Clear
        ↓
TransactionUndoOrchestrator
        ↓
Target → Temp → Source
(INTENT / DONE durable journal)
```

如果 Undo 自身在 mutation 后中断，仍复用 V0.7 Recovery Orchestrator。

## UI 一致性

执行成功后：

- 当前行路径更新到 Frozen Target；
- FileIdentity 重新读取；
- 当前批次立即显示为“无变化”；
- 不自动再次套用规则，避免“保留原名称”等规则在成功后立即二次叠加；
- 下一次用户修改规则时，Preview 才基于新的真实名称重新计算。

Undo 后：

- 当前行恢复到 Frozen Source；
- 再次执行 Preview，使现有规则重新反映可执行结果；
- 跨应用重启时，如果 UI ItemId 已变化，会使用 NamespaceIdentity 路径匹配回当前导入行，而不是使用 FileIdentity 去重/匹配。

## Demo 数据处理

冻结 UI 仍可启动时显示 synthetic 示例，但**第一次真实导入会自动替换纯 synthetic 示例集**，避免演示项混入真实执行集合。

## 当前仍未完成

```text
Manual Recovery Workbench       NOT IMPLEMENTED（V1 仅 fail-closed + 保留事务目录）
Transaction History full UI     NOT IMPLEMENTED（不属于 V1 当前 Gate）
History retention / cleanup     IMPLEMENTED / WINDOWS VALIDATION PENDING
Portable publish                IMPLEMENTED / WINDOWS VALIDATION PENDING
Explorer integration            NOT V1 CURRENT GATE
```

V0.9.0 必须通过 Windows SmokeTests + 一次专用测试目录的真实 UI Execute/Undo Gate 后，才能视为正式开放主 UI 文件重命名能力。
