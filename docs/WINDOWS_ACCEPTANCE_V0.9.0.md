# BatchRenamer V0.9.0 — Windows Acceptance

这是**第一次正式开放普通 UI 真实重命名**的 Gate，因此必须在 Windows 使用专用临时测试目录完成。不要用重要资料做首次验收。

## Gate 1 — Build

Visual Studio 2026：

```text
重新生成解决方案
0 Error
```

期望同时：

```text
0 Warning
```

若出现任何 C# / nullable / XAML 编译错误或警告，停止后续 UI Rename 测试并保留完整原文。

## Gate 2 — SmokeTests

运行：

```text
BatchRenamer.Core.SmokeTests
```

除历史全部 PASS 外，V0.9 新增关键项应包含：

```text
PASS  V0.9 UI command identity available
PASS  V0.9 transaction catalog lease acquired
PASS  V0.9 concurrent new transaction blocked by catalog lease
PASS  V0.9 catalog-busy execution performs zero mutation
PASS  V0.9 coordinated real execution completed
PASS  V0.9 coordinated execution target content: ui-execute
PASS  V0.9 coordinated execution persisted frozen transaction
PASS  V0.9 completed UI transaction advertised as safely undoable
PASS  V0.9 Undo catalog lease acquired for contention test
PASS  V0.9 concurrent Undo blocked by catalog lease
PASS  V0.9 catalog-busy Undo performs zero mutation: ui-execute
PASS  V0.9 coordinated user Undo completed
PASS  V0.9 coordinated Undo restores source: ui-execute
PASS  V0.9 failure-recovery identities available
PASS  V0.9 partial execution auto-rolls back under UI command boundary
PASS  V0.9 auto-rollback restores first source: fail-a
PASS  V0.9 auto-rollback preserves second source: fail-b
```

最终必须出现：

```text
All PreviewEngine + ValidationEngine + RenamePlanner + V0.6 A-E + V0.7 Journal/Recovery Analysis + V0.7.1 Durable Mutation/Recovery Orchestration + V0.7.2 Startup Discovery/Recovery Gate + V0.7.3 Startup Recovery Coordinator + V0.8 Transaction History/Durable Undo + V0.9 UI Execute/Undo Integration smoke tests passed.
```

进程：

```text
exit code = 0 (0x0)
```

## Gate 3 — 第一次真实 UI Execute + Undo

新建专用目录，例如：

```text
D:\BatchRenamer_V09_ACCEPTANCE\
    A.txt
    B.txt
    C.txt
```

三个文件可写入任意简单文本，便于确认内容未变。

### 3.1 启动

正常启动 BatchRenamer。

预期：

- 主窗口正常显示；
- 没有遗留事务时，不出现恢复告警；
- 底部现在存在 `撤销上次` 与 `执行重命名`；
- 初始 synthetic 示例不能执行。

### 3.2 导入真实文件

点击“添加文件”，一次选中 A/B/C。

预期：

- 启动 synthetic 示例自动被真实文件替换，而不是与 A/B/C 混在一起；
- Preview 正常；
- 设置一个确定的规则，例如：基础名称 `Test`，连续编号从 1 开始、3 位、名称后；
- 预览应为：

```text
Test_001.txt
Test_002.txt
Test_003.txt
```

- 无红色错误时，“执行重命名”启用。

### 3.3 Execute

点击“执行重命名”。

预期先出现明确确认框，并显示将重命名 3 项。选择“是”。

成功预期：

```text
A.txt / B.txt / C.txt 消失
Test_001.txt / Test_002.txt / Test_003.txt 出现
```

同时：

- 三个文件内容保持原样；
- 目录中没有残留 `.~br-*`；
- UI 当前名称同步到 Test_001/002/003；
- 刚完成后当前批次应显示 `0 项待重命名` / 行状态“无变化”，不得立即出现二次规则叠加；
- “撤销上次”启用；
- 成功提示包含 Transaction ID。

### 3.4 Undo

点击“撤销上次”并确认。

预期：

```text
A.txt / B.txt / C.txt 恢复
Test_001.txt / Test_002.txt / Test_003.txt 消失
```

同时：

- 文件内容保持原样；
- 无 `.~br-*`；
- UI 当前名称恢复为 A/B/C；
- Preview 可重新显示现有规则对应的 Test_001/002/003；
- 刚撤销的事务自身不能被重复 Undo。

## Gate 4 — 再次启动

关闭程序，重新打开。

预期：

- 不出现遗留事务恢复警告；
- 不发生任何自动文件修改；
- 已 Undo 的 V0.9 测试事务不会阻塞 Startup Gate。

## Fail-fast 条件

出现以下任一情况立即停止，不要手工删除 Temp 或 transaction metadata：

```text
出现 ~br 临时文件长期残留
只改了部分文件但程序报告成功
发生外部文件覆盖
Undo 后内容改变/丢失
启动提示 ManualRequired / RecoveryIncomplete
执行后 UI 路径与 Explorer 不一致
```

保留现场并提供截图/错误原文即可。
