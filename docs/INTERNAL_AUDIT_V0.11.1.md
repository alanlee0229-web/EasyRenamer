# INTERNAL AUDIT — V0.11.1 Stress Performance Hotfix

## Trigger

Windows 20,000-file release stress reached `durable_execute` after a valid 102.05 s Planner pass and then appeared stalled before the first 1,000-move progress line.

## Root cause confirmed in source

`JournaledRenameMutationFileSystem.ResolveTransition()` called `EnumerateTransitions()` for every move. `EnumerateTransitions()` materializes four possible transitions per plan entry.

For N=20,000:

- frozen transitions scanned per lookup: 80,000;
- Execute moves: 40,000;
- worst-order transition examinations for Execute: ~3.2 billion;
- Undo adds another ~3.2 billion.

This is a release-blocking scale defect, not an expected durable-Journal delay.

## Fix

1. Build immutable exact and semantic transition indexes once per live Journaled mutation session. Normal transition lookup becomes O(1).
2. Preserve exact-first behavior and ambiguity rejection.
3. Exclude case-only Rename entries from IgnoreCase semantic fallback exactly as before.
4. Reuse one WriteThrough journal append FileStream per Plan-bound transaction session.
5. Preserve `Flush(true)` for every INTENT and every DONE. No crash-durability weakening.
6. Add 15-second phase heartbeat to ReleaseStress.

## Production diff boundary

Only these production files changed from V0.11.0:

- `JournaledRenameMutationFileSystem.cs`
- `PlanBoundTransactionJournalSink.cs`
- `TransactionJournal.cs`

No PreviewEngine, ValidationEngine, RenamePlanner, Phase1/Phase2 executor, Rollback, Recovery Analyzer/Orchestrator, Undo Orchestrator, or UI behavior was changed.

## Validation status

- STATIC_CHECKED: PASS
- Windows strict build/full smoke: PENDING
- Windows 20k stress: PENDING

The old V0.11.0 stress run should be stopped rather than waited out.
