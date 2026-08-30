# BatchRenamer V0.8.0 — Windows Acceptance

本 Gate 必须在 Windows 完成，因为 V0.8.0 首次增加“用户显式 Undo” orchestration，SmokeTests 会在 `%TEMP%` 沙箱执行真实 Target → Temp → Source namespace mutation。

## 1. Build Gate

Visual Studio 2026：

```text
重新生成解决方案
0 Error
```

期望同时 `0 Warning`。若有 nullable / File API / scope 声明 warning/error，请保留完整原文。

## 2. SmokeTests Gate

运行：

```text
BatchRenamer.Core.SmokeTests
```

新增关键 PASS 应包含：

```text
PASS  history prepared identity available
PASS  history keeps never-started stale plan as prepared
PASS  history never exposes prepared plan as undoable
PASS  undo setup durable transaction completed
PASS  history classifies completed transaction
PASS  history exposes safe completed transaction as undoable
PASS  durable user Undo completed
PASS  Undo restores source A: undo-a
PASS  Undo restores source B: undo-b
PASS  Undo vacates final targets
PASS  Undo leaves no temp namespace
PASS  Undo journals rollback Target->Temp INTENT
PASS  Undo journals rollback Temp->Source DONE
PASS  history classifies undone transaction
PASS  history never offers Undo twice
PASS  second Undo is idempotent no-op
PASS  explicit Undo restores A-B swap
PASS  swap Undo restores A: swap-undo-a
PASS  swap Undo restores B: swap-undo-b
PASS  explicit Undo restores case-only rename
PASS  case-only Undo exact source spelling restored
PASS  history detects externally modified completed transaction
PASS  history suppresses unsafe Undo
PASS  Undo refuses externally modified transaction
PASS  Undo never overwrites foreign target: foreign-target
PASS  concurrent Undo blocked by session lease
PASS  busy Undo leaves target untouched: busy-undo
PASS  Undo journal INTENT failure stops before mutation
PASS  Undo journal INTENT failure preserves target namespace: undo-intent-fail
PASS  Undo retry succeeds after journal INTENT failure
PASS  Undo retry restores source: undo-intent-fail
PASS  Undo DONE failure requires recovery after applied move
PASS  Undo DONE failure exposes recoverable temp state
PASS  Undo DONE-failure state auto-recovers
PASS  Undo DONE-failure recovery restores source: undo-done-fail
```

最终必须出现：

```text
All PreviewEngine + ValidationEngine + RenamePlanner + V0.6 A-E + V0.7 Journal/Recovery Analysis + V0.7.1 Durable Mutation/Recovery Orchestration + V0.7.2 Startup Discovery/Recovery Gate + V0.7.3 Startup Recovery Coordinator + V0.8 Transaction History/Durable Undo smoke tests passed.
```

并且：

```text
exit code = 0 (0x0)
```

## 3. 本轮不要求重复 UI 实机测试

V0.8.0 没有修改 `BatchRenamer.App`，主窗口和已经通过 V0.7.3 Windows Gate 的启动恢复接线保持原样，因此本轮不要求再次人工启动/点击 UI。

仍然成立：

```text
“执行重命名”保持禁用
没有新增 Undo/History 控件
普通 UI 不会触发 V0.8.0 Undo mutation
```

V0.8.0 不要求用户手工创建 A/B/C 测试文件；所有 Undo mutation 都由 SmokeTests 在 `%TEMP%` 自动创建和清理。
