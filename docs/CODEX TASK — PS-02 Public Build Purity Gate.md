# CODEX TASK — PS-02 Public Build Purity Gate

## 任务身份

项目：easy重命名 / BatchRenamer
阶段：Productization Sprint
任务：PS-02 — Public Build Purity Gate

权威基线：

```text
PS-00: PASS
PS-01: PASS
PR #1: 产品化 PS-01：建立内部 QA Center
```

本任务开始前：

1. 确认 PR #1 已由用户批准并合并进 `main`；
2. 更新本地 `main`；
3. 从最新 `main` 创建：

```text
productization/ps02-public-purity-gate
```

不得继续直接在 PS-01 分支开发。

---

# 一、本轮唯一目标

把当前：

```powershell
tools\verify_public_build.ps1
```

升级为正式、可重复、fail-closed 的：

```text
PUBLIC_BUILD_PURITY GATE
```

以后任何 Public RC / GitHub Release 都必须经过它。

核心要求：

> Public 不只是“看不到 Internal QA”，而是必须有工程证据证明 Internal-only 代码、资源、命令和身份没有进入 Public artifact。

---

# 二、Gate 必须覆盖的检查

## A. Build Flavor

必须确认当前验证对象确实来自：

```text
Release-Public
BatchRenamerBuildFlavor=Public
```

如果输入是：

```text
Release-Internal
Internal artifact
Unknown flavor
```

Gate 必须：

```text
FAIL
exit code != 0
```

不能误报 PASS。

---

## B. Compile-time Isolation

必须验证 Public 编译输入中：

```text
InternalTools/**
```

没有进入：

- Compile items
- Resource
- EmbeddedResource
- Page
- Content
- ProjectReference / assembly dependency

优先采用 MSBuild/项目系统证据。

不要只靠：

```text
字符串搜索 EXE
```

字符串扫描只能作为补充证据。

如果适合当前工程，可增加一个集中式 MSBuild validation target，使 Public build 一旦包含 InternalTools 直接 build FAIL。

不要在大量正式源码里散布新的条件判断。

---

## C. Internal Type / Command Absence

必须验证 Public build 不存在 Internal-only 类型/命令，例如：

```text
InternalQaCenterWindow
InternalQaWorkspace
Internal QA shortcut routing
Shift+Ctrl+P internal command
Shift+Ctrl+D internal command
Shift+Ctrl+T internal command
```

验证方式可以组合：

```text
MSBuild compile evidence
+
assembly metadata/reflection evidence
+
command registration evidence
```

不要求人工点击 UI。

但必须是可自动重复的验证。

---

## D. Resource Purity

Public publish 中不得包含：

- Internal QA XAML/resource
- demo/test-only resource
- Internal test configuration
- Internal-only script/resource accidentally copied into product package
- QA Center resource

Gate 必须自动检查。

---

## E. Public Identity

当前 Public artifact 至少验证：

```text
Product identity:
easy重命名 / BatchRenamer

Version:
1.0.0

Build flavor:
Public
```

不得出现：

```text
INTERNAL TEST
1.0.0-internal
BatchRenamer Internal Test
```

最终 Icon / 品牌 metadata 细化属于 PS-03。

本轮只负责防止 Internal identity 泄漏。

---

## F. Publish Directory

必须验证 Public publish 目录：

```text
Single-file portable contract
```

且不存在：

```text
InternalTools
QA helper binaries
test workspace
test data
unexpected internal files
```

如果 publish contract 要求最终仅特定文件集合，按当前项目实际合同检查。

---

# 三、Negative Control — 必须新增

这是 PS-02 最重要的新要求之一。

不能只证明：

```text
Public → PASS
```

还必须证明 Gate 真能抓住错误。

至少执行：

```text
Release-Public artifact
→ Gate
→ PASS
```

以及：

```text
Release-Internal artifact
→ same Gate
→ FAIL
```

即：

```text
Positive Control:
Public = PASS

Negative Control:
Internal = REJECTED
```

如果 Internal artifact 也能通过 Public Purity Gate：

```text
PS-02 = FAIL
```

这是硬要求。

---

# 四、Fail-closed 行为

任何关键检查出现：

```text
unknown
unable to inspect
missing metadata
missing expected build evidence
ambiguous flavor
verification exception
```

不得：

```text
warning + PASS
```

必须：

```text
FAIL
```

除非该项在合同中明确属于非关键 informational check。

关键 Gate 默认 fail-closed。

---

# 五、Canonical Command

PS-02 完成后必须只有一个明确的标准验证入口。

推荐保留：

```powershell
powershell -ExecutionPolicy Bypass -File tools\verify_public_build.ps1
```

或在现有工程基础上形成一个更加稳定的等价命令。

README / docs / Release Gate 后续统一引用这个命令。

不要出现：

```text
开发者A跑脚本1
开发者B跑脚本2
正式发布手工检查
```

Public purity 必须有一个 Canonical Gate。

---

# 六、Public Publish 集成

目标是降低“忘记跑 purity gate”的可能。

优先研究是否可以让：

```text
python tools\publish_portable.py --flavor public
```

在 Public publish 完成后自动调用 Public Purity Gate。

理想行为：

```text
Public Publish
      ↓
Purity Verification
      ↓
PASS
      ↓
Publish command PASS
```

如果 Purity FAIL：

```text
Public Publish
      ↓
Purity Verification
      ↓
FAIL
      ↓
overall command FAIL
```

Internal publish 不运行 Public Purity PASS 流程。

如果当前 publish 架构不适合安全集成，允许保持独立 Gate，但必须说明原因。

不要为了集成重写整个 publish pipeline。

---

# 七、结构化 Gate Report

建议生成机器可读结果，例如：

```text
artifacts/gates/public_build_purity.json
```

示例：

```text
BuildFlavor: Public
CompileIsolation: PASS
InternalTypesAbsent: PASS
InternalCommandsAbsent: PASS
InternalResourcesAbsent: PASS
PublicIdentity: PASS
PublishDirectory: PASS
NegativeControl: PASS

PUBLIC_BUILD_PURITY: PASS
```

该文件属于构建 artifact：

```text
不要 commit
```

以后 PS-06 / PS-07 可以直接纳入 Release evidence。

---

# 八、严格禁止修改

本轮不得修改 Transaction Core 语义。

包括但不限于：

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
- FileIdentity

要求：

```text
Transaction Core semantic diff = NONE
Second mutation path = NONE
```

PS-02 只属于：

```text
Build / Verification / Release Infrastructure
```

---

# 九、本轮不做

不要顺手做：

- App Icon
- Logo
- README 大改
- Demo GIF
- Dark Mode
- Regex
- Template
- Plugin
- CLI
- Fault Injection QA
- GitHub Release
- 20,000 final qualification

这些不是 PS-02。

---

# 十、必须执行的验收

## 1. Strict Build

```text
Release-Internal
PASS / 0 warnings / 0 errors

Release-Public
PASS / 0 warnings / 0 errors
```

---

## 2. Full SmokeTests

```text
PASS
Skip = 0
```

---

## 3. PS-01 Regression

确认：

```text
Release-Internal:
QA Center available

Release-Public:
QA Center absent
```

---

## 4. Public Publish

```text
Release-Public portable publish = PASS
```

---

## 5. Positive Purity Test

对 Public artifact：

```text
PUBLIC_BUILD_PURITY = PASS
exit code = 0
```

---

## 6. Negative Purity Test

对 Internal artifact 使用同一个 Public Gate：

```text
PUBLIC_BUILD_PURITY = FAIL
Internal artifact correctly rejected
exit code != 0
```

注意：

> Negative Control 的“FAIL”代表测试成功。

最终报告中应写：

```text
Negative Control = PASS
Reason: Internal artifact was correctly rejected.
```

---

## 7. Public checks

必须全部确认：

```text
[ ] InternalTools compile exclusion
[ ] Internal types absent
[ ] Internal shortcuts absent
[ ] Internal resources absent
[ ] Internal identity absent
[ ] Public identity correct
[ ] Publish directory clean
```

---

## 8. Safety Diff

```text
Transaction Core files changed = NONE
Transaction semantic change = NONE
Second mutation path = NONE
```

---

# 十一、Git / GitHub

完成后：

```text
branch:
productization/ps02-public-purity-gate
```

中文 commit，例如：

```text
产品化：固化 Public 构建纯净度 Gate
```

push 后创建 PR：

```text
产品化 PS-02：固化 Public Build Purity Gate
```

Base：

```text
main
```

不要自动 merge。

不要上传 EXE/ZIP。

不要创建 v1.0.0 Release。

---

# 十二、最终验收标准

只有以下全部满足：

```text
[ ] PS-01 已进入 main
[ ] 独立 PS-02 branch
[ ] Canonical Public Purity Gate 建立
[ ] Public strict build PASS
[ ] Internal strict build PASS
[ ] Full SmokeTests PASS / Skip 0
[ ] Public publish PASS
[ ] Public artifact → Purity PASS
[ ] Internal artifact → Purity correctly FAIL
[ ] Negative Control PASS
[ ] InternalTools compile exclusion verified
[ ] Internal types absent
[ ] Internal shortcuts absent
[ ] Internal resources absent
[ ] Internal identity absent
[ ] Public identity correct
[ ] Publish directory clean
[ ] Critical unknown/error → fail-closed
[ ] Transaction Core semantic diff NONE
[ ] Second mutation path NONE
[ ] build/test artifacts not committed
[ ] branch pushed
[ ] PR created
```

才允许：

```text
PS-02 = PASS
```

---

# 十三、最终回报格式

```text
PS-02 FINAL REPORT

STATUS:
PASS / PARTIAL / BLOCKED

BASELINE:
main:
PS-02 branch:
HEAD:

IMPLEMENTATION:
- Canonical gate:
- Compile-time verification:
- Assembly/type verification:
- Shortcut verification:
- Resource verification:
- Identity verification:
- Publish directory verification:
- Publish integration:
- Structured report:

POSITIVE CONTROL:
Public artifact:
Exit code:
PUBLIC_BUILD_PURITY:

NEGATIVE CONTROL:
Internal artifact:
Exit code:
Rejected because:
Negative Control result:

BUILD / TEST:
Internal strict build:
Public strict build:
SmokeTests:
Public publish:

PUBLIC PURITY:
Compile isolation:
Types:
Shortcuts:
Resources:
Identity:
Publish directory:

SAFETY:
Transaction Core files changed:
Semantic change:
Second mutation path:

GIT/GITHUB:
Branch:
Commit:
Push:
PR:
Working tree:

RISKS:
DEVIATIONS:

NEXT:
PS-03 Release Identity / Branding
```

任何 PASS 必须有实际命令、输出或工程证据支持。
