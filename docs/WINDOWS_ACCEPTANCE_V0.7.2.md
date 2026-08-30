# Windows Acceptance — V0.7.2 Startup Discovery + Recovery Gate

## Why this Gate is required

V0.7.2 changes C# transaction contracts and adds WPF app-start behavior. The current build environment has no .NET 10 / Windows runtime, so Windows compilation plus filesystem/lease behavior must be verified before Startup Recovery is allowed to mutate anything.

## Step 1 — Rebuild

Open `BatchRenamer.UIPrototype.sln` in Visual Studio 2026 and run **Rebuild Solution**.

Required:

```text
0 Error
```

Prefer:

```text
0 Warning
```

If any warning/error references nullable flow, startup discovery, session lease, journal, checkpoint or recovery, report the exact text.

## Step 2 — Run SmokeTests

Run:

```text
tools/BatchRenamer.Core.SmokeTests
```

All old tests must remain PASS. New V0.7.2 critical lines include:

```text
PASS  startup missing root is clear
PASS  startup missing root permits new transaction
PASS  startup scan does not create missing root
PASS  startup prepared plan persisted
PASS  startup stale prepared source changed externally
PASS  startup completed transaction executed
PASS  startup completed target later changed externally
PASS  startup rolled-back recovery completed
PASS  startup rolled-back source later changed externally
PASS  startup terminal/prepared catalog is clear
PASS  startup terminal/prepared catalog permits new transaction
PASS  startup discovers prepared transaction
PASS  startup discovers completed transaction
PASS  startup discovers rolled-back transaction
PASS  startup ignores non-transaction directory with warning
PASS  startup recovery INTENT durable
PASS  startup gate detects recoverable transaction
PASS  startup recovery gate blocks new transaction
PASS  startup recovery-required count
PASS  startup recoverable candidate proves auto rollback eligibility
PASS  startup busy lease acquired
PASS  startup gate detects live session
PASS  startup live session blocks new transaction
PASS  startup gate detects transaction metadata loss
PASS  startup manual state blocks new transaction
PASS  startup manual state dominates recoverable state
PASS  startup mixed gate preserves candidate counts
```

Final required line:

```text
All PreviewEngine + ValidationEngine + RenamePlanner + V0.6 A-E + V0.7 Journal/Recovery Analysis + V0.7.1 Durable Mutation/Recovery Orchestration + V0.7.2 Startup Discovery/Recovery Gate smoke tests passed.
```

Process exit code:

```text
0 (0x0)
```

## Step 3 — Launch the normal WPF app once

Launch `BatchRenamer.App` normally.

Because previous UI tests only created prepared `plan.json` metadata and never executed real user-file transactions, expected normal behavior is:

```text
Main window opens normally.
No “检测到遗留事务” dialog appears.
No user file name changes.
```

If the recovery dialog appears, do not delete transaction metadata manually. Capture the dialog and report it; the scanner may have found an unexpected/malformed historical transaction that must be audited.

## Not part of this Gate

Do not manually simulate a crash in `%LOCALAPPDATA%` and do not use important files.
SmokeTests already create isolated `%TEMP%` scenarios for RecoveryRequired / SessionBusy / ManualRequired.

V0.7.2 still does not auto-recover at startup and still does not expose normal UI Execute.
