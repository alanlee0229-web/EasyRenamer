# V0.11.1.1 — Compile Hotfix

- Fixed release-blocking `CS0103` in `JournaledRenameMutationFileSystem.BuildTransitionIndexes()`: the V0.11.1 O(1) index refactor referenced `EnumerateTransitions()` but accidentally omitted the helper during the replacement.
- Restored the exact four frozen transition directions from the Windows-validated V0.11.0 implementation: Phase1 Source→Temp, Phase2 Temp→Target, Rollback Target→Temp, Rollback Temp→Source.
- No transaction protocol, durability semantics, UI, planner, validation, recovery, or undo contract changed.
- V0.11.1 performance changes remain: O(1) transition indexes and session-scoped WriteThrough journal stream with per-event `Flush(true)`.

---

# V0.11.1 — Stress Performance Hotfix

- Fixed a release-blocking O(N²) live-journal transition resolution path. V0.11.0 scanned all `4 * plan.Entries` transitions before every namespace move; 20k two-phase Execute therefore expanded to billions of comparisons/allocations.
- Added immutable O(1) exact/semantic transition indexes while preserving case-only and ambiguous-transition safety rules.
- Reused one WriteThrough `events.jsonl` append stream per live Plan-bound session. INTENT/DONE still call `Flush(true)` individually; durability semantics are unchanged.
- Added 15-second stress-phase heartbeat output so long read-only Preflight/Recovery scans are visibly alive before the first move progress line.
- No Preview/Validation/RenamePlan/Phase1/Phase2/Rollback/Recovery/Undo behavior contract was intentionally changed.

---

# BatchRenamer V0.11.0 — V1 Release Stress Audit

- Fixed the two Release `CS8602` warnings in `RenamePlanIntegrity` by capturing already-guarded plan collections into non-null locals; integrity semantics are unchanged.
- Portable publish now treats all compiler warnings as errors by default; `--allow-warnings` exists only for diagnostics.
- Added `BatchRenamer.ReleaseStressTests` for real Windows filesystem scale validation.
- Added hard correctness checks for 20,000-item Execute + Undo, exact move counts, 80,000 Execute journal events, zero Temp residue, sample-content preservation, Startup Gate Clear, and idempotent second Undo.
- Added progress reporting every 1,000 namespace moves and a JSON performance report.
- Added `tools/run_v1_release_gate.py` to run strict Release build, accumulated SmokeTests, 20k stress, strict Portable publish, and emit a V1 release manifest with SHA256 values.
- No production Phase1/Phase2/Rollback/Journal/Recovery/Undo mutation algorithm changed.

---

# V0.10.0.1 RC Compile Hotfix

- Fixed C# CS0136 in the V0.10 retention smoke-test block by renaming the nested `plan` local to `v10TerminalPlan`.
- Hardened nullable flow for transaction metadata file-name inspection; `Path.GetFileName(...)` is now explicitly checked before dereference.
- No change to RenamePlan, transaction mutation, Journal, Recovery, Undo, retention policy, or UI behavior.

# BatchRenamer V0.10.0 — Release Candidate Foundation

- Added conservative `TransactionRetentionService` matching the frozen V1 goal of retaining approximately the latest 20 terminal transaction records.
- Added a 2-day grace period for abandoned Prepared plans.
- Retention never auto-removes interrupted, externally modified, busy, manual-required or current newest Undo metadata.
- Retention re-acquires the per-transaction lease, re-runs recovery analysis, and refuses directories containing unknown metadata.
- Retention deletes BatchRenamer metadata only; it never calls the user namespace mutation interface.
- Added self-contained, single-file, non-trimmed win-x64 publish profile.
- Added `tools/publish_portable.py` to build, hash and package `BatchRenamer_Portable_x64.zip`.
- Added app/file version 0.10.0 release-candidate metadata.
- Replaced normal Execute/Undo/Startup system MessageBox flows with a lightweight application-styled dialog; frozen main UI structure is unchanged.
- Expanded SmokeTests with V0.10 retention and user-namespace non-mutation coverage.

---

# BatchRenamer V0.9.0.1 — Compile / Nullable Hotfix

V0.9.0 首次 Windows 编译 Gate 暴露 4 个 `Path` 名称解析错误与 nullable 警告。该问题只位于 V0.9 新增 UI namespace snapshot / command-boundary glue，不涉及已验证的 Phase1/Phase2/Rollback/Journal/Recovery/Undo mutation core。

## Fixed

- `RenameItemViewModel.ApplyNamespaceSnapshot()` 显式加入 `System.IO`，修复 4 处 `Path.Get*` 的编译错误。
- `Path.GetFileName` / `GetExtension` / `GetFileNameWithoutExtension` 结果做显式空值归一化，避免 nullable-flow 警告。
- V0.9 新增 Transaction coordinator 文件显式引入 `System.IO`，不再依赖生成的 implicit global using。
- `TransactionNewExecutionCoordinator` 将成功持久化后的 `TransactionDirectory` 捕获为稳定 non-null local，避免属性重复访问造成 nullable-flow 不确定性。
- V0.9 SmokeTests 将持久化事务目录一次性固定为 non-null local。

## Safety boundary

- 不改变 RenamePlan / Transaction schema。
- 不改变 Phase1 / Phase2 / Rollback / Journal / Recovery / Undo 算法。
- 不改变 UI 视觉结构。
- 不增加任何 overwrite/delete/auto-skip 行为。
- 本包仍需完成原 V0.9.0 Windows Gate；在编译 + SmokeTests 全 PASS 前不要进行普通 UI 真改名验收。

---

# BatchRenamer V0.9.0 — Safe UI Execute + Undo Integration

V0.8.0 已由用户 Windows 实机确认全量 PASS。

## Added

- 普通主 UI 正式接入真实“执行重命名”。
- 主 UI 增加最小必要“撤销上次”入口，不重开冻结 UI 设计。
- `TransactionCatalogLease`：`transactions/.catalog.lock` + `FileShare.None`，跨 TransactionId / 跨进程串行化真实 Execute/Undo。
- `TransactionNewExecutionCoordinator`：全局 Gate re-check → plan persistence → durable Execute → mutation 后失败时条件式自动 Recovery。
- `TransactionUserUndoCoordinator`：全局 catalog lease 包围 V0.8 durable Undo，并处理 Undo crash-window recovery。
- Execute/Undo 时工作区禁用；窗口拖放、隐藏测试快捷键等输入被阻断。
- Preview 增加 dirty gate：规则/顺序/勾选变化后立即禁用 Execute，直到最新 Preview + Validation 完成。
- 执行成功后 UI row namespace snapshot 更新到 Frozen Target，并保持当前批次为“无变化”，避免规则立即二次套用。
- Undo 后恢复 Frozen Source 并重新 Preview。
- 跨重启 Undo 的 UI reconciliation 支持 NamespaceIdentity 路径 fallback，不以 FileIdentity 合并 hard-link namespace。
- 第一次真实导入自动替换纯 synthetic 启动示例，防止演示数据混入真实事务。
- V0.9 SmokeTests：catalog contention、coordinated real Execute、History CanUndo、coordinated Undo、partial Execute 自动回滚。

## Safety boundary

- V0.6 Phase1 / Phase2 / Rollback mutation executors 未修改。
- V0.7 Journaled mutation / Recovery / Startup Recovery 内核未修改。
- V0.8 `TransactionUndoOrchestrator` 未修改。
- 新 UI Execute 仍只消费 `BuildFinalPlanAsync()` 生成的 Frozen RenamePlan；Transaction 层不读取 RenameRuleSet/WPF。
- 不增加 overwrite/delete/auto-(1)/auto-skip 行为。

---

# BatchRenamer V0.8.0 — Transaction History + Durable Undo Foundation

V0.7.3 已由用户 Windows 实机确认全量 PASS。

## Added

- `TransactionHistoryService`：read-only transaction history projection。
- History status：Prepared / Completed / Undone / Interrupted / ExternallyModified / SessionBusy / ManualRequired。
- `CanUndo` 只对当前仍被 FileIdentity 证明完整处于 Frozen Target 的 Completed transaction 开放。
- prepared/dry-run stale-source 规则：空 Journal 且无 mutation evidence 的历史计划保持 Prepared。
- `TransactionUndoOrchestrator`：显式用户 Undo，复用 durable Journaled rollback。
- Undo state：Completed / AlreadyUndone / NotEligible / SessionBusy / FailedNoMutation / RecoveryRequired / ManualRequired。
- Undo single-writer lease。
- Undo locked recovery-analysis eligibility gate。
- Undo rollback INTENT failure 零 namespace mutation + retry。
- Undo 后 History 自动转为 Undone，二次 Undo 幂等 no-op。
- A↔B swap 与 case-only explicit Undo smoke coverage。
- externally modified Target 禁止 Undo，foreign object 永不覆盖。

## Safety boundary

- V0.7.1 已 Windows 验证的 Phase1 / Phase2 / Rollback / Journaled mutation core 未修改。
- V0.7.2 Startup Discovery 未修改。
- V0.7.3 Startup Recovery Coordinator 未修改。
- Main UI `执行重命名` 仍 `IsEnabled=False`，未接入 Execute / Undo / History。
- V0.8.0 的真实 Undo mutation 只存在于 Transaction core + `%TEMP%` SmokeTests。

---

# BatchRenamer V0.7.3 — Startup Recovery Coordinator

V0.7.2 已由用户 Windows 实机确认全量 PASS，SmokeTests 退出码 0。

## Added

- `TransactionStartupRecoveryCoordinator`：启动阶段自动处理纯 `RecoveryRequired` catalog。
- Coordinator result/state contract：ClearNoAction / AutoRecoveryCompleted / BlockedSessionBusy / ManualRequired / RecoveryIncomplete。
- 全局 fail-closed：ManualRequired 或 SessionBusy 存在时，零自动恢复 mutation。
- Recover 前仍由 `TransactionRecoveryOrchestrator` 取得 single-writer lease 并重新分析，关闭 Discovery→Recovery TOCTOU。
- 任意 recovery 非 Success 后停止处理剩余 transaction。
- Recovery 后强制重新 Startup Discovery，只有 Final Gate=Clear 才放行未来新事务。
- MainWindow Loaded 接入 Startup Recovery Coordinator；成功自动回滚仅显示必要提示。
- 新增单事务恢复、多事务恢复、二次启动幂等、busy/manual 零 mutation、rollback mutation failure fail-closed smoke tests。

## Safety boundary

- V0.7.1 Phase1 / Phase2 / Rollback / durable mutation core 未修改。
- V0.7.2 `TransactionStartupDiscovery` 未修改。
- Normal UI Execute 仍保持禁用且未接线。
- `ManualRequired` / `SessionBusy` 永远不会被 Startup Coordinator 自动修改。

---

# BatchRenamer V0.7.2 — Startup Transaction Discovery + Recovery Gate

V0.7.1 已由用户 Windows 实机确认全量 PASS，进程退出码 0。

## Added

- `TransactionStoragePaths`：统一默认 transaction root。
- `TransactionStartupDiscovery`：启动时枚举有效 TransactionId 目录并联合 Recovery Analyzer 分类。
- Startup disposition：NotStarted / Completed / RolledBack / RecoveryRequired / SessionBusy / ManualRequired。
- Global Gate：ManualRequired > SessionBusy > RecoveryRequired > Clear。
- prepared-plan stale-source 规则：空 Journal 且未观察到 Temp/Target mutation 时，不把用户后续 Source 变化误判为 crash。
- terminal-history 规则：Completed / RolledBack 有 durable checkpoint + Journal evidence 时，后续用户文件变化不阻塞启动。
- live session 检测：持有同一 transaction lease 的其他进程被识别为 SessionBusy。
- MainWindow Loaded 后后台执行 read-only startup scan；Clear 静默，阻塞状态只提示、不自动 mutation。
- V0.7.2 Windows smoke scenarios 与 acceptance 文档。

## Safety boundary

- V0.7.1 mutation core 未修改。
- Startup Discovery 不引用 namespace mutation interface。
- Normal UI Execute 仍未接线。
- Startup automatic rollback 仍未接线。

---

# BatchRenamer V0.7.1 — Durable Mutation + Recovery Orchestration

V0.7.0 已由用户 Windows 实机确认：Journal / state.json / Recovery Analyzer 全量 SmokeTests PASS，进程退出码 `0`。

## Durable mutation protocol

新增：

- `JournaledRenameMutationFileSystem`：所有 Source/Temp/Target Move 强制 `INTENT → Move → DONE`；
- INTENT 不可持久化时禁止底层 Move；
- DONE 写失败时沿用 V0.6 apply-then-throw reconciliation；
- 计划外 namespace transition 拒绝执行；
- case-only transition exact-direction guard。

## Plan-bound journal session

新增 `PlanBoundTransactionJournalSink`：

- live mutation 时对 `plan.json` 持有 read-only / deny-write-delete lease；
- session 启动时一次性验证 persisted plan 与内存 Frozen Plan 完全一致；
- 后续事件不再每次反序列化 plan.json；
- 连续 Ordinal entry validation 走 O(1) fast path，避免大批次 O(N²) 校验。

## Cross-process transaction session lease

新增 `TransactionSessionLease`：

- `session.lock` + live `FileShare.None` handle；
- 同一 TransactionId 只允许一个 mutation session；
- crash 自动释放 handle，不依赖 stale-lock 删除；
- Recovery 获取 lease 后强制 re-analyze，关闭分析到执行之间的 TOCTOU。

## TransactionExecutionOrchestrator

- 从 persisted `plan.json` 启动；
- 执行前先做 Recovery Analysis；
- 只有 pristine `NotStarted` transaction 可开始；
- Completed / RolledBack / partial / external / ambiguous transaction 禁止同 ID 重执行；
- Phase1/Phase2 checkpoint 接入；
- 正常成功写 `Completed` advisory checkpoint。

## TransactionRecoveryOrchestrator

- read-only analysis 先行；
- 仅 `CanAutoRollback` 状态允许自动 mutation；
- 自动 rollback 同样 Journaled；
- `ExternallyModified/Ambiguous` 永远 ManualRequired；
- rollback journal INTENT 写失败时 fail-closed，零未记录 mutation。

## Recovery classification hardening

- rollback INTENT/DONE 出现后，mixed namespace 优先识别 `RollbackInProgress`；
- interrupted rollback 可重启并幂等完成；
- 所有 Frozen Target 已正确到位时，即使最终 DONE 丢失，也以真实 filesystem + FileIdentity 证据接受 `Completed`。

## New crash-window smoke tests

覆盖：

- crash after INTENT before Move；
- crash after Move before DONE in Phase1；
- partial Phase2 after Move before DONE；
- final Phase2 Move applied but DONE missing；
- interrupted rollback after Target→Temp before DONE；
- journal INTENT failure before mutation；
- journal DONE failure after mutation；
- recovery journal failure；
- external conflict manual-only；
- durable case-only rename；
- completed/rolled-back TransactionId replay rejection；
- concurrent Execute/Recovery session rejection。

### 当前仍禁止

```text
Startup recovery discovery  NOT IMPLEMENTED
Normal UI Execute            NOT WIRED
Undo                         NOT IMPLEMENTED
```

---

# BatchRenamer V0.7.0 — Journal + Crash Recovery Analysis Foundation

V0.6.2.1 已由用户 Windows 实机确认：Phase2 + Rollback 全量 SmokeTests PASS，进程退出码 `0`。

## V0.7-A — Append-only Journal

新增 `TransactionJournal`：

- `events.jsonl` append-only；
- INTENT / DONE 事件；
- Phase1 / Phase2 / Rollback 四类 mutation operation；
- TransactionId / Ordinal / ItemId 与 Frozen Plan 强绑定；
- `WriteThrough + Flush(true)`；
- 最后一行 crash truncation 容错；
- 中间 JSON 损坏拒绝信任；
- Journal 只作为 Recovery evidence，不覆盖真实 filesystem evidence。

## V0.7-B — Advisory state.json

新增 `TransactionStateStore`：

- 小型 checkpoint；
- staging + metadata replace；
- 写后 read-back；
- 明确非权威状态。

## V0.7-C — Read-only Recovery Analyzer

新增 `TransactionRecoveryAnalyzer`：

- Plan + Journal + Checkpoint + current filesystem 联合判断；
- `NotStarted / Phase1InProgress / Phase1Applied / Phase2InProgress / Completed / RollbackInProgress / RolledBack / ExternallyModified / Ambiguous`；
- FileIdentity continuity；
- PathSemantics drift guard；
- external namespace conflict；
- case-only exact spelling；
- Best-effort identity fallback。

### 当前仍禁止

```text
Journal hooks in mutation executors  NOT WIRED
Automatic crash rollback             NOT IMPLEMENTED
Startup recovery discovery           NOT IMPLEMENTED
Undo                                 NOT IMPLEMENTED
Normal UI Execute                    NOT WIRED
```

---

# BatchRenamer V0.6.2 — Phase 2 + Rollback Foundation

V0.6.1 `Source → Temp` 已由用户 Windows 实机确认：完整 SmokeTests 全部 PASS，进程退出码 `0`。

## V0.6-D — Temp → Target

新增：

- `TransactionPhase2Executor`；
- Phase2 专用 preflight；
- Source 必须全部 vacated；
- Temp kind / FileIdentity 必须匹配；
- Target 必须全部空闲；
- 每项 JIT recheck + post-move verification；
- no-overwrite finalization；
- partial failure applied prefix；
- apply-then-throw reconciliation；
- A↔B / cycle；
- directory；
- case-only target exact spelling verification。

## V0.6-E — Rollback Foundation

新增：

- `TransactionRollbackExecutor`；
- Frozen Plan + current filesystem + FileIdentity 联合状态判断；
- `Target → Temp → Source` 两阶段回滚，避免循环 rename 依赖；
- completed/partial Phase2 与 partial Phase1 恢复；
- no-overwrite；
- external Source occupancy 拒绝；
- case-only exact namespace inspector；
- apply-then-throw 后可重入恢复；
- 成功后再次 rollback 为 0 mutation。

### 当前仍禁止

```text
Normal UI Execute       NOT WIRED
Journal / events.jsonl  NOT IMPLEMENTED
Crash Recovery          NOT IMPLEMENTED
Undo                    NOT IMPLEMENTED
```

本版需要通过 `docs/WINDOWS_ACCEPTANCE_V0.6.2.md` 后，才进入 Journal / Crash Recovery Foundation。

---

# BatchRenamer V0.6.1 — Transaction Phase 1 Engine

## V0.6-C — Source → Temp

V0.6.0 Transaction Foundation 已由用户 Windows 实机确认全部通过。V0.6.1 开始实现第一次真实 namespace mutation，但仍不接入普通主 UI。

新增：

- `IRenameMutationFileSystem` 极小无覆盖 mutation surface；
- `SystemRenameMutationFileSystem`；
- `TransactionPhase1Executor`；
- Phase1 state / applied-prefix / recovery-needed 合同；
- Preflight 后、每项 move 前的 JIT Source / Temp / FileIdentity recheck；
- move 后 Source vacated / Temp kind / FileIdentity post-check；
- partial failure 返回已确认应用 prefix；
- move API 异常后重新读取 Source/Temp，区分“确认未应用 / 已应用后异常 / 状态歧义”，不把 exception 机械等同于零 mutation；
- mutation 开始后不因 cancellation 主动制造 partial state；
- Windows SmokeTests 切换为 `net10.0-windows`，加入真实 `%TEMP%` sandbox mutation、文件夹 move、FileIdentity 连续性、Temp 占用拒绝与第二项故障注入测试。

### 当前仍禁止

```text
Temp → Target     NOT IMPLEMENTED
Rollback Engine   NOT IMPLEMENTED
Journal           NOT IMPLEMENTED
Crash Recovery    NOT IMPLEMENTED
Undo              NOT IMPLEMENTED
Normal UI Execute NOT WIRED
```

本版必须先通过 `docs/WINDOWS_ACCEPTANCE_V0.6.1.md`，之后才进入 V0.6-D。

---

# BatchRenamer V0.6.0 — Transaction Foundation

## 定位

本版内部连续完成两个不修改用户文件的事务基础 Gate：

- V0.6-A：Transaction Plan Persistence；
- V0.6-B：Transaction Preflight。

V1 基础 UI、Preview、Validation、RenamePlanner 合同继续冻结。

## 修复：V0.5 Final Validation 状态栏一致性

V0.5 Windows 实测发现：Planner Final Validation 已检测出 `SOURCE_MISSING`，行状态和弹窗正确，但底部状态栏仍可能保留上一轮 Preview 的 `0 错误`。

原因：`BuildFinalPlanAsync()` 应用 Final Validation 后更新了 `HasErrors/HasWarnings`，但未重算 `StatusText`。

V0.6.0 已修复：

- Final Validation 后同步刷新错误/警告计数；
- `只看问题` / 搜索过滤依赖状态变化时同步刷新 CollectionView；
- 不改变冻结 UI 布局。

## 安全修复：已知 FileIdentity 不允许静默降级

补强 `ValidationEngine`：如果导入时已经冻结了 `ExpectedFileIdentity`，但 Final Validation / Preview 时当前身份读取返回 `null`，现在返回：

```text
SOURCE_IDENTITY_UNVERIFIABLE
```

并阻止 Plan。

这样不会出现“之前知道对象身份，执行前却因为读取失败把 Plan 悄悄降级为无身份校验”的情况。

原本就无法取得 FileIdentity 的远程/特殊文件系统仍可按 Best-effort 路线工作，不会被伪装成 Strong Recovery。

## 新增：BatchRenamer.Transaction

新增独立层：

```text
src/BatchRenamer.Transaction/
```

事务层不接收 `RenameRuleSet`，只消费 Frozen `RenamePlan`。

### RenamePlanIntegrity

拒绝：

- 空 / 未知 Schema Plan；
- 空 TransactionId；
- 空 Entries；
- 缺失 DirectorySemantics；
- 重复 / 非连续 Ordinal；
- 重复 ItemId；
- 空 Source/Temp/Target；
- V1 跨目录 move；
- no-op entry；
- 非 `.~br-` Temp namespace；
- 重复 Source / Temp / Target；
- Temp 与 Source/Target namespace 冲突。

A↔B / cycle 的 Source/Target namespace 交叉仍然合法。

### RenamePlanPersistence

新增 `plan.json` 安全持久化：

```text
%LOCALAPPDATA%\BatchRenamer\transactions\<TransactionId>\plan.json
```

安全规则：

- 不覆盖已有 Transaction 目录；
- staging 与 final 位于同一目录；
- staging 使用 `CreateNew + FileShare.None + WriteThrough`；
- `Flush(true)` 后才提交；
- 同目录原子 `File.Move(staging, plan.json, overwrite:false)`；
- 写后强制 read-back；
- read-back 再做 plan integrity；
- 写前 / 写后 SHA256 必须一致；
- failure cleanup 只允许清理 BatchRenamer 自己的 staging / 空事务目录。

注意：这里唯一的 `File.Move` 是 **BatchRenamer 自身 staging plan 文件 → plan.json**，不允许接收任何 `RenamePlanEntry.SourcePath / TemporaryPath / TargetPath`。

### TransactionPreflight

真正 Phase 1 前增加独立只读 Gate：

- `SOURCE_MISSING`；
- `SOURCE_UNREADABLE`；
- `SOURCE_KIND_CHANGED`；
- `SOURCE_IDENTITY_UNVERIFIABLE`；
- `SOURCE_IDENTITY_CHANGED`；
- `TEMP_NAMESPACE_UNVERIFIABLE`；
- `TEMP_ALREADY_EXISTS`；
- `TARGET_NAMESPACE_UNVERIFIABLE`；
- `TARGET_EXISTS`；
- `PATH_SEMANTICS_CHANGED`；
- `PATH_SEMANTICS_UNVERIFIABLE`；
- `PATH_COMPONENT_LIMIT_CHANGED`；
- `PATH_LENGTH_LIMIT_CHANGED`。

对于无法冻结 FileIdentity / 无法可靠获得目录语义的环境，明确产生 Best-effort Warning，不伪装成 Strong Recovery。

## SmokeTests 扩展

新增覆盖：

- plan.json persist + read-back；
- SHA256 稳定；
- 同 TransactionId 不覆盖；
- 未知 Schema 拒绝；
- Preflight 正常通过；
- Source missing；
- FileIdentity changed；
- 外部 Target occupancy；
- Temp occupancy；
- A↔B vacating target；
- PathSemantics changed。

## 当前明确未实现

V0.6.0 **仍没有用户文件 Rename**：

```text
Source → Temp     NOT IMPLEMENTED
Temp → Target     NOT IMPLEMENTED
Rollback          NOT IMPLEMENTED
Journal           NOT IMPLEMENTED
Crash Recovery    NOT IMPLEMENTED
Undo              NOT IMPLEMENTED
```

下一阶段只有在 V0.6.0 Windows Gate 通过后才进入 V0.6-C。

---

# Historical Baseline — V0.5.0

V0.5.0 建立 `RenamePlanner + Immutable RenamePlan Schema V1`，并由用户 Windows 实机确认：

- 真实文件 Dry-run Plan 可生成；
- Source 外部删除能被 Final Validation 阻止；
- Synthetic 20,000 项不能进入执行 Plan。

## V0.6.2.1 compile hotfix
- Fixed CS0136 in SmokeTests by separating the planner case-only test variable from the Phase2 transaction case-only plan variable.
- Removed nullable-flow ambiguity in the planner case-only assertion by materializing the validated non-null plan before dereferencing it.
- No production transaction behavior changed; Phase2/Rollback implementation is unchanged.
- Fixed the two CS8602 nullable warnings in rollback move-state reconciliation by capturing ExpectedFileIdentity into a non-null local value before comparison.
