# Internal Audit — V0.7.3 Startup Recovery Coordinator

## 基线

- 输入基线：V0.7.2 Startup Discovery / Recovery Gate（用户 Windows 实机 PASS）。
- 本环境无 Windows/.NET 10 SDK，不能声明 WINDOWS_VALIDATED。

## 修改范围

生产代码只新增/修改：

```text
BatchRenamer.Transaction/TransactionStartupRecoveryCoordinator.cs   NEW
BatchRenamer.Transaction/TransactionContracts.cs                    coordinator contracts only
BatchRenamer.App/ViewModels/MainViewModel.cs                        startup coordinator call
BatchRenamer.App/MainWindow.xaml.cs                                 startup status behavior
```

V0.7.1 已 Windows 验证的 mutation core 未修改：

```text
TransactionPhase1Executor.cs
TransactionPhase2Executor.cs
TransactionRollbackExecutor.cs
JournaledRenameMutationFileSystem.cs
SystemRenameMutationFileSystem.cs
TransactionExecutionOrchestrator.cs
TransactionRecoveryOrchestrator.cs
TransactionSessionLease.cs
```

V0.7.2 `TransactionStartupDiscovery.cs` 未修改。

## Safety assertions

- Coordinator 自身不直接调用 `File.Move` / `Directory.Move`。
- 自动 mutation 只通过 `TransactionRecoveryOrchestrator`。
- Global Gate=ManualRequired/SessionBusy 时，Coordinator 不调用 Recover。
- RecoveryRequired candidate 的 Recover 内部仍会重新取得 lease 并 re-analyze。
- recovery 非 Success 后停止，不继续处理后续 transaction。
- 返回前再次 Startup Scan；最终非 Clear 时 `CanStartNewTransaction=false`。
- Normal UI Execute 仍 `IsEnabled=False` 且无 Execute click wiring。

## 当前状态

```text
V0.7.3 implementation       IMPLEMENTED
Source-level audit          STATIC_CHECKED
Windows build/runtime       PENDING
Normal UI Execute           NOT WIRED
Undo                        NOT IMPLEMENTED
```
