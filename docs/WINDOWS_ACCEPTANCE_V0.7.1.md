# Windows Acceptance — V0.7.1 Durable Mutation + Recovery Orchestration

## Purpose

This Gate validates the first version where `events.jsonl` is actually placed around real namespace mutations.

All real mutations in this test are created inside isolated `%TEMP%` smoke-test sandboxes. Do **not** test with important user files.

## Step 1 — Rebuild

Open:

```text
BatchRenamer.UIPrototype.sln
```

in Visual Studio 2026 and run **Rebuild Solution**.

Expected:

```text
0 Error
```

Preferably:

```text
0 Warning
```

If any warning/error mentions nullable flow, FileShare, FileIdentity, journal, recovery, or transaction code, report the exact text.

## Step 2 — Run SmokeTests

Run:

```text
tools/BatchRenamer.Core.SmokeTests
```

The existing V0.2–V0.7 tests must continue to pass.

V0.7.1 adds the following critical expected PASS groups.

### Durable execution + journal

```text
PASS  durable execution identities available
PASS  durable execution plan persisted
PASS  transaction single-writer lease acquired
PASS  concurrent transaction execution blocked by session lease
PASS  busy execution performs zero mutation
PASS  durable plan lease blocks concurrent plan.json write
PASS  durable execution completed
PASS  durable execution target A content: durable-a
PASS  durable execution target B content: durable-b
PASS  durable execution journal event count
PASS  durable entry 0 Phase1 INTENT
PASS  durable entry 0 Phase1 DONE
PASS  durable entry 0 Phase2 INTENT
PASS  durable entry 0 Phase2 DONE
PASS  durable entry 1 Phase1 INTENT
PASS  durable entry 1 Phase1 DONE
PASS  durable entry 1 Phase2 INTENT
PASS  durable entry 1 Phase2 DONE
PASS  durable execution completed checkpoint
PASS  durable completed transaction cannot be re-executed under same id
```

### case-only durable operation direction

```text
PASS  durable case-only execution completed
PASS  durable case-only exact target spelling
PASS  durable case-only journal event count
PASS  durable case-only journal operation direction
```

### Fail-closed Journal behavior

```text
PASS  durable INTENT failure stops before mutation
PASS  durable INTENT failure preserves source namespace
PASS  durable DONE failure requires recovery
PASS  durable DONE failure observes applied Phase1 move
PASS  durable DONE failure analyzable for auto rollback
PASS  durable DONE failure auto rollback completed
PASS  durable DONE failure source restored: done-fail
PASS  durable rolled-back transaction cannot be re-executed under same id
PASS  durable rollback INTENT journaled
PASS  durable rollback DONE journaled
```

### Crash-window recovery

```text
PASS  crash-before-move recovery makes no mutation
PASS  crash-before-move source preserved: before-move

PASS  recovery single-writer lease acquired
PASS  concurrent recovery blocked by session lease
PASS  busy recovery performs zero mutation
PASS  crash-after-move auto rollback completed
PASS  crash-after-move source restored: after-move

PASS  crash-Phase2 partial state auto-rollback eligible
PASS  crash-Phase2 auto rollback completed
PASS  crash-Phase2 source A restored: crash-p2-a
PASS  crash-Phase2 source B restored: crash-p2-b

PASS  crash-final missing DONE accepts completed filesystem state
PASS  crash-final target preserved: crash-final

PASS  crash-rollback classified rollback-in-progress
PASS  crash-rollback resumed automatically
PASS  crash-rollback source restored: crash-rollback
```

### Recovery fail-closed / external conflict

```text
PASS  recovery journal failure requires manual recovery
PASS  recovery journal failure performs zero unjournaled rollback mutations
PASS  recovery orchestrator external modification stays manual-only
PASS  recovery orchestrator preserves owned Temp on external conflict: owned-recovery-external
PASS  recovery orchestrator never overwrites foreign Target: foreign-recovery-external
```

## Final required line

The final line must be:

```text
All PreviewEngine + ValidationEngine + RenamePlanner + V0.6 A-E + V0.7 Journal/Recovery Analysis + V0.7.1 Durable Mutation/Recovery Orchestration smoke tests passed.
```

Process exit code must be:

```text
0 (0x0)
```

## What this Gate does NOT validate

V0.7.1 still does not wire the normal application UI to real rename.

This Gate does not yet validate:

```text
startup transaction discovery
app-start recovery prompt
normal Execute button
Undo UI
portable publish
```

Those remain gated behind the next stage.
