# Windows Acceptance — V0.7.0 Journal + Recovery Analysis

本 Gate 只验证 V0.7 新增的 Journal / state.json / Recovery Analyzer，同时完整回归 V0.6 已验证事务层。

## 1. Build

Visual Studio 2026：

```text
重新生成解决方案
```

预期：

```text
0 Error
```

如有 Warning，请完整复制 Warning 文本，不要忽略 nullable / file API 相关 warning。

## 2. SmokeTests

运行：

```text
BatchRenamer.Core.SmokeTests
```

测试只在 `%TEMP%` disposable sandbox 中执行 namespace mutation，不需要手工创建文件，也不要拿重要资料测试。

V0.7 关键新增 PASS 应至少包含：

```text
PASS  journal identity available
PASS  journal plan persisted
PASS  journal INTENT append
PASS  journal DONE append
PASS  journal round-trip event count
PASS  journal preserves append order
PASS  journal rejects item mismatch
PASS  journal ignores crash-truncated final tail
PASS  journal reports truncated tail warning
PASS  state checkpoint write
PASS  state checkpoint round-trip
PASS  state checkpoint replace
PASS  state checkpoint latest wins
PASS  recovery classifies not-started
PASS  recovery classifies partial Phase1
PASS  recovery partial Phase1 auto-rollback eligible
PASS  recovery classifies completed Phase1
PASS  recovery classifies partial Phase2
PASS  recovery partial Phase2 auto-rollback eligible
PASS  recovery classifies completed transaction
PASS  recovery distinguishes rolled-back from never-started
PASS  recovery detects foreign target occupancy
PASS  recovery refuses auto rollback after external modification
PASS  recovery case-only exact source spelling
PASS  recovery case-only exact target spelling
```

最终必须看到：

```text
All PreviewEngine + ValidationEngine + RenamePlanner + V0.6-A/B Foundation + V0.6-C Phase1 + V0.6-D Phase2 + V0.6-E Rollback + V0.7 Journal/Recovery Analysis smoke tests passed.
```

并且：

```text
进程退出代码 = 0
```

## 3. 本 Gate 不要求

- 不操作主 UI 的“执行重命名”；
- 不手工制造 crash；
- 不手工编辑 events.jsonl / state.json；
- 不测试用户真实资料。

通过后进入 V0.7.1：把 durable INTENT/DONE 接入真实 mutation 边界，并实现 recovery orchestration。
