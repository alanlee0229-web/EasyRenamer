# BatchRenamer V0.5 Windows 验收

V0.5 仍不会修改文件。正式“执行重命名”按钮继续禁用。

## A. 自动 SmokeTests

在 Visual Studio 2026：

1. 重新生成整个解决方案；
2. 将 `BatchRenamer.Core.SmokeTests` 设为启动项目；
3. 运行；
4. 最后一行应看到：

```text
All PreviewEngine + ValidationEngine + RenamePlanner smoke tests passed.
```

重点覆盖：

- 普通 Source → Target Plan；
- no-op / 未参与项目不进入 Plan；
- Synthetic 数据禁止进入 Plan；
- 扩展名锁定；
- A ↔ B；
- TempPath 唯一且占用时拒绝覆盖；
- Source 文件/文件夹类型变化；
- Final Validation owned by Planner。

---

## B. 真实文件 dry-run（不会改名）

### 准备

1. 新建一个临时测试目录；
2. 创建：

```text
A.txt
B.txt
C.txt
```

3. 启动 BatchRenamer；
4. 先点“清空”去掉演示数据；
5. 把这三个真实 txt 文件拖入程序；
6. 设置一个确定会变化的名称，例如基础名称 `Test` + 编号。

### 生成 Plan

按：

```text
Ctrl + Shift + P
```

预期弹出：

```text
RenamePlan 已通过最终校验并在内存中冻结
计划项：3
Transaction ID：...
Schema：1
V0.5 不会修改磁盘，也不会写入 Journal
```

然后回到 Explorer：

```text
A.txt / B.txt / C.txt 必须仍然原样存在
```

---

## C. 外部变化 Final Revalidation

1. 导入 `A.txt`；
2. 等预览正常；
3. 去 Explorer 删除 `A.txt`；
4. 回到程序按 `Ctrl + Shift + P`；
5. Plan 必须生成失败，列表应显示“源已丢失”或弹窗提示最终校验失败；
6. 不得创建任何新文件。

---

## D. 演示 / 20,000 项保护

直接用程序启动时的演示数据，或按：

```text
Ctrl + Shift + T
```

加载 20,000 条 Synthetic 数据，再按：

```text
Ctrl + Shift + P
```

必须拒绝生成真实 Plan，并提示演示/压力测试数据不可执行。

---

## E. V0.5 Gate

通过条件：

- VS2026 全解决方案编译通过；
- SmokeTests 全通过；
- 真实文件 Dry-run 能生成 Plan；
- Dry-run 前后文件系统完全不变；
- 外部删除能被 Final Validation 阻止；
- Synthetic 数据无法进入 Plan。

通过后进入 V0.6 Transaction / Journal，不再改 V1 基础 UI。
