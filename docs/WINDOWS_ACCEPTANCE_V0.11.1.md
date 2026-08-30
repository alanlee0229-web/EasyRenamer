# Windows Acceptance — V0.11.1 Stress Performance Hotfix

## Why this hotfix exists

V0.11.0 can appear permanently stuck at `[phase] durable_execute ...` for a 20,000-item real transaction because live Journal transition resolution was O(N) per move. This is a known code-path defect, not a reason to wait indefinitely.

## Stop the old V0.11.0 run

If an old V0.11.0 terminal is still inside `durable_execute`, press `Ctrl+C`. The stress program only operates inside the sandbox path printed at startup (`%TEMP%\BatchRenamer.ReleaseStress\<runId>`). After the process is stopped, that old sandbox may be deleted as a whole.

## Formal V1 Gate

From the V0.11.1 project root run:

```text
python tools\run_v1_release_gate.py
```

Do not use `--quick` for final qualification.

Expected behavior during long phases:

```text
[alive] durable_execute: elapsed 15s
[alive] durable_execute: elapsed 30s
...
[execute] moves 1,000/40,000, elapsed ...
```

The exact elapsed time is hardware/filesystem dependent. The important distinction is that the process now emits heartbeats, and once mutation starts, move progress must advance.

Final success remains:

```text
===== RELEASE GATE PASS =====
PASS: strict build + full smoke + 20k real transaction/undo stress + strict portable publish.
```

and `artifacts\release-gate\V1_RELEASE_GATE_MANIFEST.json` must record `releaseGateQualified = true` and `stressCount = 20000`.
