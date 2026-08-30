# Internal Audit — V0.8.0 Transaction History + Durable Undo Foundation

## 基线

- 输入基线：V0.7.3 Startup Recovery Coordinator（用户 Windows 实机 PASS）。
- 当前容器无 Windows/.NET 10 SDK，因此本版不能声明 WINDOWS_VALIDATED。

## 修改范围

生产代码：

```text
BatchRenamer.Transaction/TransactionContracts.cs               contracts appended
BatchRenamer.Transaction/TransactionHistoryService.cs          NEW
BatchRenamer.Transaction/TransactionUndoOrchestrator.cs         NEW
```

测试/文档：

```text
tools/BatchRenamer.Core.SmokeTests/Program.cs
docs/INTERNAL_AUDIT_V0.8.0.md
docs/WINDOWS_ACCEPTANCE_V0.8.0.md
README.md
CHANGELOG.md
```

## 未修改的 Windows-validated mutation/recovery core

```text
TransactionPhase1Executor.cs
TransactionPhase2Executor.cs
TransactionRollbackExecutor.cs
JournaledRenameMutationFileSystem.cs
SystemRenameMutationFileSystem.cs
TransactionExecutionOrchestrator.cs
TransactionRecoveryOrchestrator.cs
TransactionSessionLease.cs
TransactionRecoveryAnalyzer.cs
TransactionStartupDiscovery.cs
TransactionStartupRecoveryCoordinator.cs
```

## Safety assertions

- History service 不调用 `IRenameMutationFileSystem`。
- Undo 仅在 locked analysis = Completed 且全部 entry = Phase2Applied 时进入 mutation。
- Undo 复用 `JournaledRenameMutationFileSystem` 与 `TransactionRollbackExecutor`；不存在新的 overwrite/delete API。
- Undo 前 single-writer lease；busy 时零 mutation。
- externally modified / ambiguous transaction 不允许 Undo。
- 已 RolledBack transaction 的二次 Undo 为 no-op。
- rollback INTENT 写失败时底层 Move 不会调用；transaction 保持可重试 Completed 状态。
- Main UI 仍未引用 `TransactionUndoOrchestrator` / `TransactionHistoryService`。
- MainWindow 的“执行重命名”仍 `IsEnabled=False`。

## Source-level checks

生产 namespace mutation API 仍只集中于已验证底层：

```text
SystemRenameMutationFileSystem:
  File.Move(... overwrite:false)
  Directory.Move(...)
```

其余 `File.Move/Delete` 只用于 transaction metadata staging/cleanup。

## 状态

```text
V0.8.0 implementation       IMPLEMENTED
Source-level audit          STATIC_CHECKED
Windows build/runtime       PENDING
Normal UI Execute           NOT WIRED
Normal UI Undo              NOT WIRED
History UI                  NOT WIRED
```
