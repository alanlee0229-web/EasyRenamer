# Internal Audit — V0.7.1

## Status

```text
V0.6-A/B Foundation                 WINDOWS_VALIDATED
V0.6-C Phase1                       WINDOWS_VALIDATED
V0.6-D Phase2                       WINDOWS_VALIDATED
V0.6-E Rollback                     WINDOWS_VALIDATED
V0.7-A Journal                      WINDOWS_VALIDATED
V0.7-B state.json                   WINDOWS_VALIDATED
V0.7-C Recovery Analyzer            WINDOWS_VALIDATED

V0.7.1 Durable Mutation Wrapper     IMPLEMENTED / STATIC_CHECKED
V0.7.1 Execution Orchestrator       IMPLEMENTED / STATIC_CHECKED
V0.7.1 Recovery Orchestrator        IMPLEMENTED / STATIC_CHECKED
V0.7.1 Transaction Session Lease    IMPLEMENTED / STATIC_CHECKED
V0.7.1 Interrupted Rollback logic   IMPLEMENTED / STATIC_CHECKED
Windows runtime V0.7.1              PENDING

Startup transaction discovery       NOT IMPLEMENTED
Normal UI Execute                    NOT WIRED
Undo                                 NOT IMPLEMENTED
```

## Scope control

Relative to the Windows-validated V0.7.0 baseline, V0.7.1 does **not** modify:

```text
src/BatchRenamer.App
src/BatchRenamer.Core
src/BatchRenamer.FileSystem
TransactionPhase1Executor.cs
TransactionPhase2Executor.cs
TransactionRollbackExecutor.cs
SystemRenameMutationFileSystem.cs
```

This preserves the already Windows-validated mutation primitives.

New / materially changed transaction code:

```text
JournaledRenameMutationFileSystem.cs      NEW
PlanBoundTransactionJournalSink.cs        NEW
TransactionExecutionOrchestrator.cs       NEW
TransactionRecoveryOrchestrator.cs        NEW
TransactionSessionLease.cs                NEW
TransactionContracts.cs                   contracts added
TransactionJournal.cs                     plan-bound append fast path
TransactionRecoveryAnalyzer.cs            interrupted rollback classification
```

`TransactionRecoveryAnalyzer.cs` also removes one duplicate unreachable `return` left in V0.7.0 source; no intended behavioral change beyond rollback-state hardening.

## Mandatory mutation boundary

Production orchestration now constructs:

```text
JournaledRenameMutationFileSystem
    ↓
SystemRenameMutationFileSystem
```

The wrapper recognizes only these exact Frozen Plan transitions:

```text
Phase1SourceToTemp    Source → Temp
Phase2TempToTarget    Temp   → Target
RollbackTargetToTemp  Target → Temp
RollbackTempToSource  Temp   → Source
```

No arbitrary path pair is accepted.

### Fail-closed durable intent

Before calling the underlying no-overwrite Move:

```text
append INTENT
Flush(true)
```

If append fails, the wrapper throws before calling the inner mutation surface.

After Move returns:

```text
append DONE
Flush(true)
```

If DONE append fails after the namespace mutation already happened, the wrapper throws. Existing V0.6 executors then reconcile Source/Temp/Target from the real filesystem and return a recovery-required state rather than assuming the move did not happen.

## Plan immutability during a live session

`PlanBoundTransactionJournalSink`:

1. opens `plan.json` with `FileAccess.Read + FileShare.Read`;
2. while that lease is alive, write/delete sharing is denied;
3. loads and validates the persisted Plan;
4. compares it to the expected in-memory Frozen Plan;
5. only then permits journal append.

This closes the race where `plan.json` could be replaced between session validation and later INTENT/DONE events.

## Large-batch complexity audit

V0.7.0 `TransactionJournal.Append()` reloaded and deserialized `plan.json` for every event. Wiring that API directly into 20,000 items would cause severe repeated plan I/O.

V0.7.1 live sessions instead use `TransactionJournal.AppendBound()` with one validated plan instance.

Also, `ValidateAgainstPlan()` now resolves ordinary contiguous Ordinal via:

```text
plan.Entries[ordinal]
```

before falling back to a linear search. This prevents the normal 20,000-item event stream from degenerating to O(N²) plan-entry lookup.

Durability is not weakened: each event still uses `WriteThrough + Flush(true)`.

## Cross-process single-writer guard

Every mutating orchestrator uses `TransactionSessionLease`:

```text
session.lock
FileAccess.ReadWrite
FileShare.None
```

The file may remain after dispose; only the live OS handle represents ownership. This avoids unsafe stale-lock-file heuristics and releases automatically after process termination.

Execution acquires the lease before plan/recovery-state admission. Recovery performs an initial read-only analysis, acquires the lease only when mutation is required, then re-analyzes under the lease before rollback.

This closes concurrent Execute/Recover and analyze-then-mutate TOCTOU windows between cooperating BatchRenamer processes.

## Transaction replay guard

`TransactionExecutionOrchestrator` runs `TransactionRecoveryAnalyzer` before Phase1.

Execution requires:

```text
State == NotStarted
AND no Recovery Analysis Error
```

A TransactionId cannot be reused after:

```text
Completed
RolledBack
Partial Phase1/Phase2
RollbackInProgress
ExternallyModified
Ambiguous
Journal/Recovery integrity error
```

A new user attempt must produce a new RenamePlan / TransactionId.

## Recovery trust model

Automatic recovery is allowed only when:

```text
RecoveryAnalysis.RequiresRecoveryAction == true
AND RecoveryAnalysis.CanAutoRollback == true
```

`ExternallyModified` and `Ambiguous` are manual-only.

Automatic rollback itself is journaled. Therefore Recovery does not introduce a second unlogged mutation path.

## Interrupted rollback correction

V0.7.0 could observe Temp/Target or Source/Temp mixtures during rollback and classify them by forward-phase shape.

V0.7.1 gives precedence to durable rollback evidence:

```text
Rollback journal INTENT/DONE
```

so such states become:

```text
RollbackInProgress
```

and can be safely re-entered through the idempotent V0.6-E Rollback executor.

## Main UI boundary

Static reference scan confirms V0.7.1 orchestration classes are not referenced by:

```text
BatchRenamer.App
BatchRenamer.Core
BatchRenamer.FileSystem
```

The normal UI still cannot perform real Source/Temp/Target mutation.

## Mutation API surface

Expected production namespace mutation remains isolated to:

```text
SystemRenameMutationFileSystem.cs
    File.Move(... overwrite:false)
    Directory.Move(...)
```

Other File.Move/Delete occurrences are transaction metadata staging/cleanup only (`plan.json`, `state.json`). No overwrite/delete API was added to `IRenameMutationFileSystem`.

## Static-check limitation

The current execution environment has no .NET SDK / Windows WPF runtime, so this audit cannot claim compilation or Windows FileShare/FileIdentity behavior.

V0.7.1 must pass `WINDOWS_ACCEPTANCE_V0.7.1.md` before this protocol is considered Windows validated.
