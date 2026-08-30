# Internal Audit — V0.7.2 Startup Discovery + Recovery Gate

## Baseline

```text
V0.7.1 Durable Mutation/Recovery Orchestration  WINDOWS_VALIDATED / CLOSED
V0.7.2 Startup Discovery                        IMPLEMENTED / STATIC_CHECKED
V0.7.2 App startup read-only gate               IMPLEMENTED / STATIC_CHECKED
V0.7.2 Windows runtime                          PENDING
```

## Mutation-core preservation

SHA256 comparison against the Windows-validated V0.7.1 baseline confirms these files are unchanged:

```text
TransactionPhase1Executor.cs
TransactionPhase2Executor.cs
TransactionRollbackExecutor.cs
SystemRenameMutationFileSystem.cs
JournaledRenameMutationFileSystem.cs
TransactionExecutionOrchestrator.cs
TransactionRecoveryOrchestrator.cs
```

No V0.7.2 startup class references `IRenameMutationFileSystem` or any Source/Temp/Target Move API.

`BatchRenamer.App` still contains no references to:

```text
TransactionExecutionOrchestrator
TransactionRecoveryOrchestrator
TransactionPhase1Executor
TransactionPhase2Executor
TransactionRollbackExecutor
SystemRenameMutationFileSystem
```

Therefore normal UI still cannot perform a real rename.

## New components

```text
TransactionStoragePaths.cs
TransactionStartupDiscovery.cs
TransactionStartupDisposition / TransactionStartupGateState contracts
```

`MainViewModel.EvaluateStartupTransactionGateAsync()` runs the scan on a background task.
`MainWindow.Loaded` only displays a blocking-state message; it does not invoke Recovery mutation.

## Historical catalog safety

A startup catalog has different semantics from an immediate crash analyzer.

Fail-safe rules implemented:

1. Valid plan + valid empty Journal + no Temp/Target owned object => Prepared/NotStarted, even if Source later changed externally.
2. Completed checkpoint + Phase2 DONE for every plan entry => historical Completed even if target later changed.
3. RolledBack checkpoint + rollback DONE evidence => historical RolledBack even if source later changed.
4. No Journal mutation evidence but object observed at Temp/Target => ManualRequired.
5. Checkpoint claims applied mutation without Journal evidence => ManualRequired.
6. Corrupt/missing plan in a valid TransactionId directory => ManualRequired.
7. Non-GUID directories under transaction root are ignored with warning; they do not impersonate transactions.

## Live-session rule

Discovery tries to acquire the same per-transaction `session.lock` lease while analyzing a candidate.
A live owner yields `SessionBusy`; the scanner does not infer crash state while another cooperating process is mutating that transaction.

This may create/open BatchRenamer-owned `session.lock` metadata for a scanned transaction directory. It does not touch user namespace entries.

## Global gate precedence

```text
ManualRequired > SessionBusy > RecoveryRequired > Clear
```

`CanStartNewTransaction` is true only for `Clear`.

Future real Execute wiring must re-scan immediately before creating/executing a new transaction; the startup snapshot alone is not a permanent authorization token.

## Static checks

- changed C# file brace/parenthesis structure audited;
- startup classes scanned for namespace mutation APIs: none;
- existing V0.7.1 mutation-core hashes: unchanged;
- normal App scanned for execution/recovery mutation orchestration references: none;
- no .NET SDK is available in the current non-Windows environment, so no claim of compilation/runtime validation is made.
