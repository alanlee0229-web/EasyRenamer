# BatchRenamer V0.11.0 — V1 Release Stress Audit / Windows Gate

## Purpose

This is the final scale-and-release gate before freezing V1.0 Portable. It does **not** add new rename features.

V0.11.0 validates, on a real Windows filesystem:

- strict Release build with compiler warnings promoted to errors;
- all accumulated functional/safety SmokeTests;
- 20,000 real sandbox files through Planner → durable Execute → Startup scan → durable Undo → idempotent second Undo;
- no leftover `.~br-*` namespace after Execute or Undo;
- sample file content preservation;
- exact file-count restoration;
- durable Journal event count and metadata size;
- self-contained single-file win-x64 Portable publish;
- final EXE/ZIP SHA256 manifest.

The stress tool creates files only under `%TEMP%\\BatchRenamer.ReleaseStress\\<run-id>`.
It does not inspect or modify user-selected folders.

## Final V1 gate — one command

From the repository root:

```text
python tools\run_v1_release_gate.py
```

Do **not** use `--quick` for the final qualification run. `--quick` is diagnostic only (2,000 files).

## Expected sequence

The script runs four hard gates in order:

```text
1/4 Strict Release solution build
2/4 Full Release smoke tests
3/4 Real filesystem transaction stress (20,000 files)
4/4 Strict self-contained portable publish
```

### Gate 1

Must complete with **zero warnings** because `TreatWarningsAsErrors=true`.
Any compiler warning is a release blocker.

### Gate 2

The accumulated SmokeTests must finish with exit code `0` and the normal all-tests-passed terminal line.

### Gate 3

The stress executable must report:

```text
PASS: release stress audit completed with exact namespace restoration and zero Temp residue.
```

Hard correctness conditions automatically checked by the tool:

- 20,000 files created in the private TEMP sandbox;
- 20,000 Frozen RenamePlan entries;
- durable Execute completes exactly 40,000 namespace moves (`Source→Temp` + `Temp→Target`);
- after Execute: exactly 20,000 target files, zero Source residue, zero `.~br-*` residue;
- sample content preserved;
- Journal after Execute contains exactly 80,000 events (`INTENT/DONE` × two phases × 20,000);
- Startup Gate remains Clear after Completed transaction;
- durable Undo completes exactly 40,000 namespace moves (`Target→Temp` + `Temp→Source`);
- after Undo: exactly 20,000 original Source files, zero Target residue, zero `.~br-*` residue;
- sample content preserved again;
- Startup Gate remains Clear after RolledBack transaction;
- second Undo succeeds with **zero namespace mutation**.

The tool prints progress every 1,000 real namespace moves so a long run does not appear hung.
Execution time is recorded but is not yet a hard pass/fail threshold because SSD, antivirus and Windows Defender behavior vary materially by machine. The JSON report is used for the final performance decision.

On success the TEMP sandbox is automatically removed. On failure it is preserved for forensic inspection.

### Gate 4

Strict Portable publish must complete with no warning and produce:

```text
artifacts\portable\win-x64\BatchRenamer.exe
artifacts\packages\BatchRenamer_Portable_x64.zip
```

The publish output must remain a single executable.

## Final output

A successful full run ends with:

```text
===== RELEASE GATE PASS =====
PASS: strict build + full smoke + 20k real transaction/undo stress + strict portable publish.
```

and produces:

```text
artifacts\release-gate\V1_RELEASE_GATE_MANIFEST.json
```

The manifest contains:

- `releaseGateQualified: true`
- stress count
- stress report path
- Portable EXE SHA256
- Portable ZIP SHA256

## What to send back

After the run, send either:

1. the final console block beginning with `===== RELEASE GATE PASS =====`, plus the stress timing lines; or
2. if it fails, the first error/failure block and the generated `artifacts\stress\release-stress-*.json` file.

Do not manually delete a failed stress sandbox before the failure has been reviewed.
