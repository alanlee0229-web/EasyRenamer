# V0.9.0 Internal Audit

## Baseline

Source: V0.8.0 Transaction History + Durable Undo (Windows validated by user).

## Changed production files

```text
src/BatchRenamer.Transaction/TransactionCatalogLease.cs          NEW
src/BatchRenamer.Transaction/TransactionNewExecutionCoordinator.cs NEW
src/BatchRenamer.Transaction/TransactionUserUndoCoordinator.cs    NEW
src/BatchRenamer.App/Models/RenameItemViewModel.cs                 MODIFIED
src/BatchRenamer.App/ViewModels/MainViewModel.cs                   MODIFIED
src/BatchRenamer.App/MainWindow.xaml                               MODIFIED
src/BatchRenamer.App/MainWindow.xaml.cs                            MODIFIED
```

## Explicitly unchanged validated mutation core

```text
TransactionPhase1Executor.cs
TransactionPhase2Executor.cs
TransactionRollbackExecutor.cs
JournaledRenameMutationFileSystem.cs
SystemRenameMutationFileSystem.cs
TransactionExecutionOrchestrator.cs
TransactionRecoveryAnalyzer.cs
TransactionRecoveryOrchestrator.cs
TransactionStartupDiscovery.cs
TransactionStartupRecoveryCoordinator.cs
TransactionUndoOrchestrator.cs
```

## Safety observations

1. UI never performs `File.Move` / `Directory.Move` directly.
2. UI Execute consumes only fresh Frozen RenamePlan.
3. Cross-process root lease closes different-TransactionId concurrent command gap.
4. Dirty Preview disables Execute during debounce; button is not enabled against stale visual preview.
5. Workspace/drop/hotkey mutations are blocked while a real transaction Task is in flight.
6. First real import replaces pure synthetic demo data.
7. Execute failure after mutation uses filesystem-driven Recovery eligibility; no blind rollback.
8. UI reconciliation prefers stable ItemId in-session, then namespace-path fallback across restart; it never uses FileIdentity as NamespaceIdentity.
9. No overwrite/delete API added.

## Validation level

```text
IMPLEMENTED        yes
STATIC_CHECKED     yes
WINDOWS_COMPILED   pending
WINDOWS_SMOKETEST  pending
REAL_UI_EXECUTE    pending
REAL_UI_UNDO       pending
```
