# BatchRenamer V0.6.1 — Windows Acceptance

## 目的

验证 V0.6-C `Source → Temp` 在真实 Windows 文件系统上成立。

这是第一次运行真实 namespace mutation，因此本 Gate **只允许 SmokeTests 操作其自行创建的 `%TEMP%` sandbox**。不要使用重要资料，也不需要在主 UI 中导入任何文件。

---

## Gate 1 — 全解决方案编译

Visual Studio 2026：

```text
重新生成解决方案
```

预期：

```text
0 Error
```

如果有任何编译错误，本 Gate 失败，停止后续测试。

---

## Gate 2 — 运行 BatchRenamer.Core.SmokeTests

直接运行：

```text
BatchRenamer.Core.SmokeTests
```

V0.6.1 SmokeTests 会自动：

1. 在 `%TEMP%` 创建独立 `BatchRenamer-Phase1-Smoke-<GUID>` 目录；
2. 创建测试文件 A/B；
3. 获取真实 Windows FileIdentity；
4. 执行两项真实 `Source → Temp`；
5. 验证 Source 已 vacate；
6. 验证 Temp 存在、内容未变、FileIdentity 未变；
7. 验证最终 Target 完全没有被创建；
8. 验证已有 Temp 时 Phase 1 在零 mutation 前拒绝；
9. 验证真实文件夹 `Source → Temp`；
10. 注入第二项 move 失败，验证精确 partial prefix / `RequiresRecovery`；
11. 模拟“move 已生效但 API 随后抛异常”，验证磁盘状态 reconciliation；
12. 仅在 disposable sandbox 中做测试清理。

必须看到以下关键 PASS：

```text
PASS  phase1 real file batch completed
PASS  phase1 applied entry count
PASS  phase1 sources vacated
PASS  phase1 temp entries exist
PASS  phase1 identity A preserved
PASS  phase1 identity B preserved
PASS  phase1 never creates final targets
PASS  phase1 occupied temp blocks before mutation
PASS  phase1 real directory completed
PASS  phase1 directory identity preserved
PASS  phase1 partial failure state
PASS  phase1 partial failure exact prefix
PASS  phase1 partial failure never creates targets
PASS  phase1 apply-then-throw recovery state
PASS  phase1 apply-then-throw reconciles applied entry
PASS  phase1 apply-then-throw issue
PASS  phase1 apply-then-throw observes temp state
```

最后一行必须严格为：

```text
All PreviewEngine + ValidationEngine + RenamePlanner + V0.6-A/B Foundation + V0.6-C Phase1 smoke tests passed.
```

出现 Exception、FAILED、编译错误或最后一行不一致，均视为 Gate 失败。

---

## Gate 3 — 主 UI 不得出现真实执行能力

启动 BatchRenamer 主程序。

预期：

- 主界面“执行重命名”仍不可用于真实执行；
- `Ctrl + Shift + P` 仍是 V0.6.0 的 Plan Persistence + Preflight 诊断链；
- 不应通过主 UI 触发 `Source → Temp`；
- 不需要用用户真实文件测试 V0.6.1。

---

## 通过标准

三个 Gate 全部符合即记为：

```text
WINDOWS_VALIDATED — V0.6-C PHASE1 SOURCE_TO_TEMP
```

下一阶段：V0.6-D `Temp → Target`。
