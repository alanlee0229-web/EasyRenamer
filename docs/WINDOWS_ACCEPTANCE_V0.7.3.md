# BatchRenamer V0.7.3 — Windows Acceptance

本 Gate 是必须的：V0.7.3 首次让 WPF 正常启动流程在确认 `RecoveryRequired` 后真正调用 durable rollback。

## 1. Build Gate

Visual Studio 2026：

```text
重新生成解决方案
0 Error
```

期望同时 `0 Warning`。若有 nullable / File API / async 相关 warning，请保留原文。

## 2. SmokeTests Gate

运行：

```text
BatchRenamer.Core.SmokeTests
```

新增 V0.7.3 关键 PASS 应包含：

```text
PASS  startup coordinator auto-recovers eligible catalog
PASS  startup coordinator recovered count
PASS  startup coordinator final gate clear
PASS  startup coordinator restores source content: partial
PASS  startup coordinator second pass is no-op
PASS  startup coordinator second pass performs zero recovery mutation
PASS  startup coordinator respects live session
PASS  startup busy coordinator performs zero recovery mutation
PASS  startup coordinator manual state dominates
PASS  startup manual catalog performs zero automatic recovery mutation
PASS  startup manual catalog leaves recoverable object untouched
PASS  startup coordinator restores multiple transactions
PASS  startup coordinator multi final gate clear
PASS  startup recovery failure keeps gate closed
PASS  startup recovery failure never reports clear
PASS  startup recovery failure preserves unresolved owned temp
```

最终必须为：

```text
All PreviewEngine + ValidationEngine + RenamePlanner + V0.6 A-E + V0.7 Journal/Recovery Analysis + V0.7.1 Durable Mutation/Recovery Orchestration + V0.7.2 Startup Discovery/Recovery Gate + V0.7.3 Startup Recovery Coordinator smoke tests passed.
```

并且：

```text
exit code = 0 (0x0)
```

所有 SmokeTests mutation 都发生在 `%TEMP%` 一次性沙箱，不需要手工准备测试文件。

## 3. Normal Startup Gate

正常启动 BatchRenamer 一次。

如果本机 `%LOCALAPPDATA%/BatchRenamer/transactions` 没有真实未完成事务，预期：

```text
主窗口正常出现
无恢复弹窗
磁盘文件无变化
```

如果出现“事务已自动恢复”或“事务恢复需要处理”，不要删除 transaction 目录；截图并保留该目录用于诊断。

## 禁止事项

- 不要人为制造 crash 测试真实重要文件。
- 不要用用户重要资料测试 V0.7.3。
- 主 UI “执行重命名”仍未开放，不应尝试绕过。
