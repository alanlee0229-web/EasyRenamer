# V0.6.1 Internal Audit — Phase 1 Source → Temp

## Status

- V0.6.0 Windows Gate：USER CONFIRMED PASS
- V0.6-C implementation：IMPLEMENTED
- Source-level static audit：STATIC_CHECKED
- Windows compile/runtime Gate：PENDING USER WINDOWS VALIDATION

## Production mutation surface

本版新增的用户 namespace mutation 仅位于：

```text
SystemRenameMutationFileSystem.MoveFileNoOverwrite
SystemRenameMutationFileSystem.MoveDirectoryNoOverwrite
```

对应：

```text
File.Move(source, temp, overwrite:false)
Directory.Move(source, temp)
```

不存在 production `Temp → Target`。

## UI isolation

`TransactionPhase1Executor` 未被 `BatchRenamer.App` 引用或调用。
主执行按钮没有被启用。

## Failure semantics

- Preflight Error：`NotStarted` / zero mutation；
- first-entry JIT failure：`NotStarted` / zero mutation；
- later JIT/move failure：`FailedPartial`；
- `AppliedEntries` 返回已成功 mutation 的精确 ordinal prefix；
- move API 抛异常后立即重读 Source/Temp，不从 exception 本身推断是否已应用；
- 已确认 Temp 生效但 API 抛异常：当前 entry 计入 AppliedEntries，并返回 `PHASE1_MOVE_EXCEPTION_AFTER_APPLY`；
- 状态无法确认：返回 `PHASE1_MOVE_STATE_AMBIGUOUS` + `FailedPartial`，后续恢复必须看真实磁盘；
- post-move verification failure：当前 entry 已计入 AppliedEntries 并返回 `FailedPartial`；
- 不执行自动覆盖、自动跳过或自动目标改写；
- 不提供伪 rollback。Rollback Foundation 留在 V0.6-E。

## Cancellation rule

Cancellation 只在 mutation 开始前生效。
第一项 move 成功以后不再主动检查 cancellation，以免人为把可继续完成的 Phase 1 切成 partial state。

## Windows-only runtime proof

当前执行环境没有 .NET 10 / Windows WPF runtime，无法声称 `WINDOWS_VALIDATED`。
SmokeTests 已新增 disposable `%TEMP%` sandbox 的真实文件/文件夹 move、FileIdentity 连续性和 partial-failure 注入测试，待 Windows Gate。
