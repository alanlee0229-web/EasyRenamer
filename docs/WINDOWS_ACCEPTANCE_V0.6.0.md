# BatchRenamer V0.6.0 Windows Gate

本 Gate 是进入第一次真实 `Source → Temp` 之前的最后一个**只读用户文件 Gate**。

V0.6.0 允许写：

```text
%LOCALAPPDATA%\BatchRenamer\transactions\...\plan.json
```

但**绝不允许改变测试目录中任何用户文件名**。

## A. 全解决方案编译

Visual Studio 2026：

```text
重新生成解决方案 BatchRenamer.UIPrototype.sln
```

预期：

```text
0 Error
```

新增工程 `BatchRenamer.Transaction` 必须一起编译成功。

## B. Core SmokeTests

将：

```text
BatchRenamer.Core.SmokeTests
```

设为启动项目并运行。

最后一行必须是：

```text
All PreviewEngine + ValidationEngine + RenamePlanner + V0.6 Transaction Foundation smoke tests passed.
```

任何 FAIL / exception 都不能进入下一阶段。

## C. 真实文件 Persistence + Preflight

只使用专门临时目录，例如：

```text
Desktop\BatchRenamer_V060_Test\
    A.txt
    B.txt
    C.txt
```

1. 启动程序；
2. 清空演示数据；
3. 导入 A/B/C；
4. 设置确定会变化的规则，例如基础名 `Test` + 3 位连续编号；
5. 记录 Explorer 中仍是 `A.txt / B.txt / C.txt`；
6. 按：

```text
Ctrl + Shift + P
```

### 成功预期

弹窗标题：

```text
V0.6 Transaction Foundation
```

正文应明确包含：

```text
计划项：3
Transaction ID：...
Schema：1
SHA256：64位十六进制字符串
plan.json：%LOCALAPPDATA%\BatchRenamer\transactions\...\plan.json
Preflight：PASS
当前版本只写事务元数据 plan.json，不会修改任何用户文件名。
```

### Explorer 必须满足

执行前后始终是：

```text
A.txt
B.txt
C.txt
```

不允许出现：

```text
.~br-...
Test_001.txt
Test_002.txt
Test_003.txt
```

## D. plan.json 人工核对

打开成功弹窗中的 `plan.json` 路径。

必须看到：

- `transactionId` 与弹窗一致；
- `schemaVersion = 1`；
- `entries` 为 3 项；
- 每项有 `sourcePath`；
- 每项有 `temporaryPath`，名称以 `.~br-` 开头；
- 每项有 `targetPath`；
- `directorySemantics` 存在；
- `plan.json` 中不应出现 `renameCount` 这种计算型 UI/便利字段。

## E. 状态栏 Bug 回归

1. 导入两个真实文件；
2. 从 Explorer 外部删除其中一个；
3. 回程序按 `Ctrl + Shift + P`。

预期：

- Final Validation 拒绝；
- 对应行显示源丢失；
- 底部状态栏必须显示 `1 错误`，不能再残留 `0 错误`；
- 不生成新的可执行 plan。

## F. Gate 判定

只有以下全部成立才 PASS：

- Solution 0 Error；
- SmokeTests 最终 PASS；
- 真实 A/B/C 成功生成并持久化 plan.json；
- read-back + Preflight 显示 PASS；
- `plan.json` 内容符合冻结 Schema；
- 用户测试文件前后完全没改名；
- 状态栏 Final Validation 计数 Bug 已修复。

通过后才进入：

> **V0.6-C — Phase 1 Source → Temp**
