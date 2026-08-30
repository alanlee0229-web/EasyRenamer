# BatchRenamer V0.10.0 — Windows Release Candidate Gate

本 Gate 只验证 V1 发布候选主线：事务元数据保留策略、既有安全事务回归、以及 win-x64 Self-contained Single-file Portable 发布。
弹窗视觉已顺手统一，但不单独设视觉 Gate。

## 1. Build

Visual Studio 2026：重新生成整个解决方案。

预期：

```text
0 Error
```

最好同时 0 Warning；若存在 nullable / File API / XAML 警告，请保留原文反馈。

## 2. Full SmokeTests

运行：

```text
BatchRenamer.Core.SmokeTests
```

V0.10 新增关键预期：

```text
PASS  V0.10 default retention keeps approximately 20 terminal transactions
PASS  V0.10 retention deletes only two old terminal records plus stale prepared metadata
PASS  V0.10 retention keeps newest terminal metadata window
PASS  V0.10 retention removes terminal metadata beyond configured window
PASS  V0.10 retention removes abandoned stale prepared plan
PASS  V0.10 retention keeps fresh prepared plan during grace period
PASS  V0.10 retention refuses transaction directory containing unknown metadata
PASS  V0.10 retention reports unknown metadata safety skip
PASS  V0.10 retention never touches user target 0
...
PASS  V0.10 retention never touches stale prepared source
```

最终必须为：

```text
All PreviewEngine + ValidationEngine + RenamePlanner + V0.6 A-E + V0.7 Journal/Recovery Analysis + V0.7.1 Durable Mutation/Recovery Orchestration + V0.7.2 Startup Discovery/Recovery Gate + V0.7.3 Startup Recovery Coordinator + V0.8 Transaction History/Durable Undo + V0.9 UI Execute/Undo Integration + V0.10 Release Candidate Retention smoke tests passed.
```

进程退出代码：

```text
0 (0x0)
```

## 3. Portable publish

在项目根目录运行：

```text
python tools\publish_portable.py
```

预期至少出现：

```text
[ok] BatchRenamer.exe: ... MiB
[ok] SHA256: ...
[ok] portable package: ...\artifacts\packages\BatchRenamer_Portable_x64.zip
[ok] package SHA256: ...
```

目标是 single executable；如果脚本报告存在附加 publish 文件，不视为自动失败，但请把完整输出发回，不要手工删除文件。

## 4. Portable runtime Gate

不要从 VS 启动。解压：

```text
artifacts\packages\BatchRenamer_Portable_x64.zip
```

把整个解压目录复制到一个新的普通目录，再双击 `BatchRenamer.exe`。

预期：

- 不要求预装单独 .NET Runtime；
- 正常打开主窗口；
- 若无遗留事务，不出现 Recovery/ManualRequired 告警；
- 设置、导入、Preview 正常；
- 弹窗视觉应已与主界面统一，但无需单独截图验收。

使用专用测试目录执行一次最小真实闭环：

```text
A.txt / B.txt / C.txt
→ Test_001.txt / Test_002.txt / Test_003.txt
→ 撤销
→ A.txt / B.txt / C.txt
```

硬性结果：

- Rename 成功；
- Undo 成功；
- 内容不变；
- 无 `.~br-*` 残留；
- 关闭再启动 Portable EXE 后无异常 Recovery 告警。

## 5. 不需要测试的内容

本版不重新测试冻结基础 UI，不重新做 20,000 Preview 性能测试，也不测试 V1.1 高级功能。

如果 1～4 全部通过，V0.10.0 可关闭，下一步进入 V1.0 Release Candidate 最终审计与正式 Portable 包冻结。
