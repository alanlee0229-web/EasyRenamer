# Windows Acceptance — V0.6.2 Phase2 + Rollback Foundation

## 为什么这次必须 Windows 实测

V0.6.2 新增真实：

```text
Temp → Target
Target → Temp
Temp → Source
```

并依赖 Windows FileIdentity、目录 case-sensitivity 与实际 namespace spelling，因此不能仅靠非 Windows 静态审计放行。

所有真实 mutation 均封闭在 SmokeTests 自动创建的 `%TEMP%` sandbox 中。**不要拿重要资料手工测试。**

---

## Gate 1 — 编译

Visual Studio 2026：

```text
重新生成解决方案
```

预期：

```text
0 Error
```

---

## Gate 2 — SmokeTests

运行：

```text
BatchRenamer.Core.SmokeTests
```

程序必须以：

```text
exit code 0
```

结束，最后一行必须是：

```text
All PreviewEngine + ValidationEngine + RenamePlanner + V0.6-A/B Foundation + V0.6-C Phase1 + V0.6-D Phase2 + V0.6-E Rollback smoke tests passed.
```

新增关键 PASS 至少应包括：

```text
PASS  phase2 real file batch completed
PASS  phase2 target A identity preserved
PASS  phase2 target B identity preserved
PASS  rollback completed Phase2 restores sources
PASS  rollback idempotent second run
PASS  phase2 directory finalization completed
PASS  rollback directory completed
PASS  phase2 A-B swap completed
PASS  rollback A-B swap completed
PASS  phase2 case-only exact target spelling
PASS  rollback case-only exact source spelling
PASS  phase2 external target blocks before mutation
PASS  phase2 partial failure state
PASS  rollback partial Phase2 completed
PASS  phase2 apply-then-throw recovery state
PASS  rollback apply-then-throw reports partial recovery
PASS  rollback retry after apply-then-throw succeeds
PASS  rollback partial Phase1 completed
PASS  rollback external source occupancy blocks overwrite
PASS  rollback retry succeeds after conflict removed
```

---

## Gate 3 — 不测试主 UI 真 Rename

本版主界面 Execute 仍未接入 Phase1/Phase2/Rollback。

因此本次**不要求，也不允许**使用真实重要文件手工点击“执行重命名”验证。

通过标准只有：

```text
Solution Build = 0 Error
SmokeTests      = exit code 0
Final PASS line = present
```

全部通过后，V0.6-D / V0.6-E 可标记为 `WINDOWS_VALIDATED`。
