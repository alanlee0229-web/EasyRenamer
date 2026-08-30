# CODEX TASK — PS-01 Internal QA Center

## 任务身份

项目：easy重命名 / BatchRenamer  
阶段：Productization Sprint  
任务：PS-01 — Internal QA Center  
权威基线：BatchRenamer V0.11.1.1  
PS-00 PASS Commit：`33a7bcd7f744f6cc19e20dafa32b07571089ce12`

当前已经冻结：

```text
Release-Internal
Release-Public
```

Internal 保留内部 QA；Public 在 MSBuild 编译层排除整个 `InternalTools`。

本轮不得重新设计双 Flavor，不得修改 Transaction Core。

---

# 一、开始前先规范 Git 基线

如果仓库尚无 `main`：

以 PS-00 PASS commit：

```text
33a7bcd7f744f6cc19e20dafa32b07571089ce12
```

建立：

```text
main
```

然后从 `main` 创建：

```text
productization/ps01-internal-qa-center
```

后续代码只提交到该任务分支。

不要直接向 main 开发。

---

# 二、本轮唯一目标

把当前已经存在的 Internal 测试能力：

```text
Shift+Ctrl+P
Shift+Ctrl+D
Shift+Ctrl+T
tools\run_release_stress.py --quick
```

收编成一个长期可维护的：

```text
Internal QA Center
```

原则：

> 收编现有能力，不重新发明第二套测试框架。

Internal QA Center 只能存在于 `Release-Internal`。

`Release-Public` 必须继续编译级完全排除。

---

# 三、第一版 QA Center 范围

建议入口：

```text
Shift + Ctrl + P
```

打开 Internal-only QA Center。

第一版至少包含以下能力。

## 1. Quick Smoke

提供少量真实测试文件的隔离测试工作区。

用于快速人工验证：

- Import
- Preview
- Sort
- Sequence
- Prefix / Suffix
- Execute
- Undo

优先复用现有 demo/test data generator。

不要制造新的 Rename Engine。

---

## 2. Demo Data

把当前：

```text
Shift+Ctrl+D
```

对应能力挂到 QA Center。

原快捷键可以继续保留。

不得复制实现。

---

## 3. 20k Preview

把当前：

```text
Shift+Ctrl+T
```

对应的 20,000-item Preview stress 入口挂到 QA Center。

继续调用现有实现。

不得另写第二个 20k Preview generator。

---

## 4. 2,000 Real File Stress

提供 QA Center 入口或明确可调用动作，对应现有：

```powershell
python tools\run_release_stress.py --quick
```

正式验证链继续保持：

```text
2,000 real files
→ Planner
→ Execute
→ Journal
→ Startup Recovery Gate
→ Undo
→ Idempotence
→ Cleanup
```

优先复用现有 ReleaseStressTests / Python harness。

不得在 WPF InternalTools 中复制一套事务压力测试逻辑。

如果从 GUI 直接安全调用现有 harness 需要过度复杂的进程管理，本轮允许：

- QA Center 显示准确命令；
- 提供 Copy Command / Open Terminal Location；
- 或做轻量 wrapper。

不要为了“一个按钮”引入新的复杂基础设施。

---

# 四、测试工作区合同

Internal QA 必须使用隔离 Sandbox。

例如：

```text
%TEMP%\BatchRenamer\InternalQA\
```

或当前已有等价安全目录。

QA Center 至少应能够明确展示：

```text
Current Test Workspace
```

建议提供：

```text
Open Workspace
Reset Workspace
Cleanup Workspace
```

永久要求：

> Cleanup 只能删除 Internal QA 自己创建并拥有的测试目录。

禁止：

- 对用户任意选择目录执行 Cleanup；
- 删除 Desktop / Documents / 用户真实工作目录；
- 根据模糊路径猜测测试目录；
- 测试功能直接操作用户真实数据。

必要时使用 ownership marker / fixed sandbox root 等方式确保安全。

---

# 五、结果展示

测试执行后不要只显示：

```text
Completed
```

至少给出结构化结果。

例如 2,000 文件测试：

```text
Files:        2000
Plan:         PASS
Execute:      PASS
Journal:      PASS
Startup Gate: PASS
Undo:         PASS
Idempotence:  PASS
Temp Left:    0
Elapsed:      xx.x s

RESULT: PASS
```

如果当前 harness 已产生结构化结果，优先直接展示/复用，不重复计算。

---

# 六、Internal QA UI 原则

这是工程工具，不是新的产品主界面。

要求：

- 简洁；
- 清晰；
- Internal-only；
- 明显标识 `INTERNAL TEST`；
- 不追求复杂视觉设计；
- 不增加主界面普通用户复杂度；
- 不做大量高级 Fault Injection。

第一版不要加入：

```text
Phase1 Kill
Phase2 Kill
Journal Corruption
Random Crash Injection
复杂 Recovery Matrix
```

这些以后可进入 Advanced / Dangerous QA。

本轮只做高频、可靠、长期有价值的内部 QA。

---

# 七、永久安全边界

所有内部测试必须继续使用正式产品路径：

```text
Preview
→ Validation
→ RenamePlanner
→ Frozen RenamePlan
→ Transaction
→ Journal / Recovery / Undo
```

严禁：

```text
Internal File.Move
Test Rename Engine
QA-only Transaction Engine
绕过 Planner
绕过 Validation
```

本轮 Transaction Core：

```text
SEMANTIC CHANGE = NONE
```

不得借 PS-01 重构：

- ValidationEngine
- RenamePlanner
- PlanPersistence
- Preflight
- Phase1 / Phase2
- Journal
- Rollback
- Recovery
- StartupRecovery
- Undo
- FileIdentity semantics

---

# 八、Public Build 要求

`Release-Public` 必须继续满足：

```text
QA Center absent
Shift+Ctrl+P absent
InternalTools excluded
Internal test resources absent
Internal test identity absent
```

PS-01 不能导致 Public Build Purity 回退。

---

# 九、必须执行的验收测试

## A. Git

```text
main = PS-00 PASS baseline
development branch = productization/ps01-internal-qa-center
```

工作区最终干净。

---

## B. Strict Builds

必须实际执行：

```text
Release-Internal
Release-Public
```

要求：

```text
0 warnings
0 errors
```

---

## C. SmokeTests

完整现有 SmokeTests：

```text
PASS
SKIP = 0
```

---

## D. Internal QA

必须验证：

```text
Shift+Ctrl+P → QA Center available
Quick Smoke available
Demo Data available
20k Preview available
2k Real File Stress available
Workspace isolation works
Cleanup works
```

其中 2k real-file stress 必须至少实际跑一次。

要求：

```text
Execute = PASS
Startup Gate = PASS
Undo = PASS
Idempotence = PASS
Temp residual = 0
```

---

## E. Public Purity

重新执行：

```powershell
powershell -ExecutionPolicy Bypass -File tools\verify_public_build.ps1
```

要求：

```text
PUBLIC_BUILD_PURITY = PASS
```

并确认：

```text
QA Center = absent
Internal shortcuts = absent
InternalTools = excluded
```

---

## F. Safety Diff

必须检查：

```text
Transaction Core semantic diff = NONE
Second mutation path = NONE
```

如果出现非必要 Transaction Core 修改，本任务不得报告 PASS。

---

# 十、GitHub 提交

完成全部 Gate 后：

在：

```text
productization/ps01-internal-qa-center
```

提交并 push。

提交信息使用中文。

建议：

```text
产品化：建立内部 QA Center
```

如果 `main` 已正常建立，则创建 PR：

```text
Productization PS-01: Internal QA Center
```

不要自动 merge。

不要发布 EXE/ZIP。

不要创建正式 v1.0.0 Release。

---

# 十一、禁止提交

不得提交：

- 2,000/20,000 测试生成文件；
- Internal QA sandbox；
- transaction/recovery 数据；
- bin/obj；
- artifacts；
- secrets；
- 用户隐私路径；
- 测试过程中产生的临时日志，除非是经过清洗且正式需要的 fixture。

---

# 十二、PS-01 验收标准

只有全部满足才允许：

```text
PS-01 = PASS
```

验收清单：

```text
[ ] main 已建立在 PS-00 PASS baseline
[ ] PS-01 使用独立开发分支
[ ] Internal QA Center 已建立
[ ] Shift+Ctrl+P 可打开 QA Center
[ ] 原有 Shift+Ctrl+D 能力保留
[ ] 原有 Shift+Ctrl+T 能力保留
[ ] Quick Smoke 可用
[ ] 2,000 real-file stress 可用且实际 PASS
[ ] Execute PASS
[ ] Startup Recovery Gate PASS
[ ] Undo PASS
[ ] Idempotence PASS
[ ] Temp residual = 0
[ ] QA Workspace 完全隔离
[ ] Cleanup 只能操作 QA 自有 sandbox
[ ] Release-Internal strict build PASS
[ ] Release-Public strict build PASS
[ ] Full SmokeTests PASS / 0 Skip
[ ] PUBLIC_BUILD_PURITY = PASS
[ ] Public 不存在 QA Center
[ ] Public 不存在 Internal shortcuts
[ ] Public 编译级排除 InternalTools
[ ] Transaction Core semantic diff = NONE
[ ] 第二套 mutation path = NONE
[ ] 测试数据/构建产物未进入 Git
[ ] branch push 成功
[ ] PR 已创建或明确报告为何未创建
```

---

# 十三、最终回报格式

```text
PS-01 FINAL REPORT

STATUS:
PASS / PARTIAL / BLOCKED

BASELINE:
PS-00 commit:
main commit:
PS-01 branch:

IMPLEMENTATION:
- QA Center:
- Quick Smoke:
- Demo Data:
- 20k Preview:
- 2k Stress:
- Workspace:
- Cleanup:
- Result reporting:

INTERNAL:
- Strict build:
- Shift+Ctrl+P:
- QA Center:
- 2k test:

PUBLIC:
- Strict build:
- QA Center:
- Internal shortcuts:
- InternalTools:
- PUBLIC_BUILD_PURITY:

TESTS:
- Exact commands:
- SmokeTests:
- 2k:
- PASS:
- FAIL:
- SKIP:

SAFETY:
- Transaction Core files changed:
- Transaction semantic change:
- Second mutation path:
- Sandbox isolation:

GIT/GITHUB:
- main:
- branch:
- commit:
- push:
- PR:
- working tree:

RISKS:
DEVIATIONS:

NEXT:
PS-02 Public Build Purity Gate
```

任何 `PASS` 都必须有真实测试或可核验的工程证据，不接受“理论上应该可以”。