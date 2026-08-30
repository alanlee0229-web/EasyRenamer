# CODEX FORMAL TASK — PS-00 Dual Build Flavor & Internal QA Isolation

**Project:** easy重命名 / BatchRenamer  
**Sprint:** Productization Sprint  
**Task ID:** PS-00  
**Authority Date:** 2026-08-30  
**Priority:** HIGHEST / execute before PS-01 and before V1.1 feature work

---

## 0. 你的角色

你是本项目的主要代码实现 Agent。

ChatGPT 主窗口承担：

- 产品经理；
- 总体架构师；
- Safety / Release Gate Authority；
- Productization Sprint 主路线冻结；
- 代码审查与最终接受。

你负责：

- 在权威源码上实施本任务；
- 运行测试；
- 给出精确 diff / build / publish / risk 回报；
- 在用户授权和本地 GitHub 凭据可用时，配合用户把本次内容提交到 GitHub 分支并创建/更新 PR；
- 不得自行扩大产品范围。

---

# 1. 必须先读取的权威材料

开始改代码前，必须读取：

1. `BatchRenamer_V1_Authoritative_Freeze_20260830_DualFlavor_Update.md`
2. 当前 **Release-qualified authoritative source/repo**
3. 如仓库中存在：
   - Window Handover
   - Codex Execution Protocol
   - Release Gate / SmokeTests 文档
   - Productization scaffold / README / GitHub templates

当前权威代码基线应能确认对应：

```text
BatchRenamer V0.11.1.1 codebase
```

对外目标：

```text
easy重命名 / BatchRenamer v1.0.0
```

如果你发现当前目录明显不是 V0.11.1.1 Release-qualified 基线：

> STOP CODE MODIFICATION.

先报告：

```text
STATUS: BLOCKED_WRONG_BASELINE
CURRENT BASELINE EVIDENCE:
EXPECTED BASELINE:
FILES / VERSION EVIDENCE:
```

不得拿 V0.5 / V0.7 / 其他旧版本猜改。

---

# 2. 本轮唯一核心目标

建立正式双 Build Flavor：

```text
Authoritative Source / Same Commit
        │
        ├─ Release-Internal
        │    └─ BatchRenamer Internal Test
        │
        └─ Release-Public
             └─ easy重命名 / BatchRenamer v1.0.0
```

核心原则：

> **同一源码、同一 Core、同一 Transaction、安全链路不分叉；只在构建层决定是否编译 Internal QA 能力。**

不得维护两套长期代码分支。

不得复制一份 Public App 再手工删除测试代码。

---

# 3. Release-Internal 必须保留的能力

先审计当前已有内部测试入口，尤其：

```text
Shift + Ctrl + P
```

以及与之相关的：

- 初始少量测试文件/场景；
- 2,000 文件重命名测试；
- 测试工作区生成；
- Reset / Cleanup；
- Undo / Recovery / Idempotence 测试辅助；
- Benchmark / Diagnostics；
- 其他当前已经存在、只服务测试的内部能力。

要求：

1. **优先保留现有行为。**
2. 不要为了“架构更漂亮”重写已经工作的测试工具。
3. 可以做必要的隔离/迁移，使它们进入清晰的 `InternalTools` 边界。
4. 所有测试必须继续调用正式产品链路：

```text
Internal QA Helper
→ Preview
→ Validation
→ RenamePlanner
→ Frozen RenamePlan
→ Transaction
→ Journal / Recovery / Undo
```

绝对禁止创建第二套：

```text
TestRenameEngine
TestFileMoveDirectly
InternalDirectFileMove
```

之类绕过正式 Transaction 的执行逻辑。

---

# 4. Release-Public 必须彻底不存在的内容

Public 构建不是“隐藏测试功能”。

正式要求：

> **Internal QA 必须在编译/打包层面从 Public artifact 排除。**

Release-Public 中不得包含/暴露：

- `Shift + Ctrl + P` Internal QA 注册；
- Internal QA Panel；
- 测试文件生成器；
- 2,000 文件测试入口；
- Crash / Fault Injection；
- Internal-only Benchmark 菜单；
- Internal-only Diagnostics 菜单；
- 测试专用 UI；
- 测试专用资源；
- 内部 QA 隐藏命令；
- 明显的 `INTERNAL TEST` 产品身份。

如果当前实现只是：

```csharp
if (InternalMode)
{
    ShowQaPanel();
}
```

但 QA 代码仍进入 Public EXE，不满足本任务。

需要调整为真正的 build-time isolation。

---

# 5. 推荐架构，但先服从现有工程结构

优先目标：

```text
BatchRenamer
│
├─ Production Code
│   ├─ App
│   ├─ Core
│   ├─ FileSystem
│   └─ Transaction
│
└─ InternalTools
    ├─ InternalQaPanel
    ├─ TestWorkspaceGenerator
    ├─ QuickSmokeScenario
    ├─ Batch2000Scenario
    ├─ RecoveryScenarios
    ├─ BenchmarkTools
    └─ InternalDiagnostics
```

不要为了匹配这个示意图而大规模改项目。

更重要的设计原则：

### 条件构建必须集中

优先将 Flavor 差异集中到：

- `.csproj` / MSBuild property；
- Composition Root；
- App startup；
- Command / shortcut registration；
- Internal-only project/reference/resource inclusion。

避免整个代码库散落几十处：

```csharp
#if INTERNAL_TEST_TOOLS
```

少量集中使用可以接受。

---

# 6. Build Flavor Contract

你需要结合当前 solution/project structure，选择最小侵入实现。

目标至少提供两个可复现命令/配置：

```text
Release-Internal
Release-Public
```

可以采用：

- Configuration；
- MSBuild property；
- Directory.Build.props；
- conditionally included project/reference/resource；

但必须满足：

```text
Same source
Same commit
Different flavor property
```

推荐显式属性：

```text
BatchRenamerBuildFlavor=Internal
BatchRenamerBuildFlavor=Public
```

或者等价的、更加符合当前工程的机制。

无论采用何种方式，都必须文档化最终命令。

---

# 7. Internal 产品身份

Internal 构建必须肉眼明显区别，避免误发。

至少实现一个明显标识，优先：

### Window title

```text
easy重命名 — INTERNAL TEST
```

或与现有窗口标题机制兼容的等价形式。

### Build metadata / About

若当前已有对应位置，可显示：

```text
Product:
BatchRenamer Internal Test

Build Flavor:
Internal

Version:
1.0.0-internal
```

不要为了这一步新造一个复杂 About 页面。

Internal package 推荐：

```text
BatchRenamer-v1.0.0-internal-win-x64.zip
```

---

# 8. Public 产品身份

Public 继续保持：

```text
Primary display brand:
easy重命名

Technical / English:
BatchRenamer

Executable:
BatchRenamer.exe

External Version:
v1.0.0
```

PS-00 不要求完成最终 Logo/Icon/metadata 美化。

那属于后续 PS-03。

本轮只需要确保：

```text
Public != Internal Test
```

并且没有 Internal QA 泄漏。

---

# 9. Transaction Core — 绝对禁止改语义

除非为了编译恢复而出现极小、不可避免的引用调整，否则不要修改下列安全域：

- `ValidationEngine`
- `RenamePlanner`
- `PlanPersistence`
- `Preflight`
- `TransactionPhase1Executor`
- `TransactionPhase2Executor`
- `TransactionRollbackExecutor`
- `TransactionJournal`
- `JournaledRenameMutationFileSystem`
- `RecoveryAnalyzer`
- `RecoveryOrchestrator`
- `StartupRecoveryCoordinator`
- `UndoOrchestrator`
- file identity / durable recovery semantics

永久链路：

```text
Preview
→ Validation
→ RenamePlanner
→ Frozen RenamePlan
→ User Confirmation
→ Transaction
```

本任务不能成为 Transaction Core 重构机会。

---

# 10. 先做代码审计，再改

实施前输出一个短审计：

```text
PS-00 PRE-IMPLEMENTATION AUDIT

AUTHORITATIVE BASELINE:
SOLUTION:
APP PROJECT:
CURRENT BUILD CONFIGURATIONS:
CURRENT Shift+Ctrl+P IMPLEMENTATION:
CURRENT TEST HELPER FILES:
CURRENT 2000-FILE TEST PATH:
CURRENT TEST RESOURCES:
CURRENT PRODUCT VERSION SOURCE:
CURRENT PUBLISH PROFILE(S):

PROPOSED FILES TO CHANGE:
PROPOSED NEW FILES:
FILES EXPLICITLY NOT TO CHANGE:
IMPLEMENTATION STRATEGY:
```

然后直接实施，不需要等待 ChatGPT 再设计。

如果用户正在交互式监督，你可以先展示审计结果后继续。

---

# 11. 三级测试长期合同

完成 PS-00 后，结构应支持：

```text
Quick Smoke
少量典型真实文件
        ↓
Internal Stress
2,000 real files
        ↓
Release Qualification
20,000 real files
```

本轮：

- 必须确保当前 Quick/Test capability 未因隔离而损坏；
- 必须确保当前 2,000 文件测试在 Internal 可运行；
- 本轮不强制重新实现 20,000 Gate；
- 但不得破坏现有 20,000 Release Gate 入口/脚本。

---

# 12. 必须执行的测试

根据仓库实际命令进行适配，但至少完成：

## A. Internal strict build

```text
Release-Internal
warnings as errors
PASS
```

## B. Public strict build

```text
Release-Public
warnings as errors
PASS
```

## C. Existing full SmokeTests

至少对与 Flavor 改动相关的正式基线跑完整 SmokeTests。

不得把 Skip 当 PASS。

## D. Internal QA visibility

证明：

```text
Release-Internal:
Shift+Ctrl+P = available
Internal QA = available
2000-file test = available
```

如自动 UI 测试困难，可以：

- 静态注册测试；
- 单元/集成测试；
- Windows 手工 Gate；

但必须明确证据类型。

## E. Public QA absence

证明：

```text
Release-Public:
Shift+Ctrl+P = unavailable
Internal QA = unavailable
```

## F. Compile/package purity

尽可能自动检查 Public artifact：

- InternalTools assembly/reference absent；
- internal-only resources absent；
- internal-only strings/types/commands 不应以可执行测试入口存在；
- publish directory 不含 QA 工具文件。

如果 single-file 模式使简单字符串扫描不可靠，不要伪造“100% 二进制证明”。

应同时使用：

- MSBuild inclusion evidence；
- project/reference evidence；
- publish file inspection；
- behavior test；

形成组合证明。

最终输出：

```text
PUBLIC_BUILD_PURITY = PASS / FAIL
```

## G. Production behavior regression

至少确认：

- Preview still works；
- Rename planning still works；
- Rename execution still works；
- Undo still works；
- no new alternate mutation path。

---

# 13. GitHub 协作任务

PS-00 本身完成并通过本地 Gate 后，配合用户把内容发布到 GitHub。

## 13.1 先检查 Git 状态

运行并回报：

```text
git status
git branch --show-current
git log -n 5 --oneline
git remote -v
```

如果当前目录不是 Git repo：

- 不要擅自创建错误远程；
- 可以初始化本地 git；
- 但在 push 前需要使用用户提供/现有的 GitHub repository URL。

如果已有 remote：

- 确认 remote 指向正确的 BatchRenamer repo；
- 不要把代码推到无关仓库。

## 13.2 分支策略

默认不要直接改/推 `main` 或 `master`。

创建：

```text
productization/ps00-dual-flavor
```

如果该分支已存在，则安全复用或创建带时间/序号的新分支，并报告。

## 13.3 GitHub 本轮建议提交内容

本次 branch 可以包含：

1. PS-00 代码实现；
2. 对应 build/config 文档；
3. 更新后的权威冻结文档：
   `BatchRenamer_V1_Authoritative_Freeze_20260830_DualFlavor_Update.md`
4. 如仓库尚未存在且用户希望同时落地，可以合入已经批准的 productization docs scaffold：
   - README
   - SUPPORT
   - SECURITY
   - CONTRIBUTING
   - `.github/ISSUE_TEMPLATE`
   - `docs/ROADMAP.md`
   - `docs/SAFETY_ARCHITECTURE.md`

但是：

> 不要为了 GitHub 好看而扩大 PS-00 的生产代码 Scope。

文档合入与 PS-00 代码要在 diff 中清晰区分。

## 13.4 严禁提交

不得提交：

- 本地测试生成的 2,000/20,000 个文件；
- transaction/recovery 用户数据；
- 本地缓存；
- build output；
- `bin/obj`；
- secrets/token；
- 用户隐私路径；
- Internal 测试 ZIP/EXE，除非用户明确要求专门存放到非 Release 的测试资产位置；
- Public Release artifact，在正式 Final Release Gate 前不要当作 v1.0.0 final 发布。

检查 `.gitignore`，必要时只做合理补充。

## 13.5 Commit 前回报

在 commit 前必须给用户展示：

```text
GIT PRE-COMMIT REVIEW

BRANCH:
CHANGED FILES:
NEW FILES:
DELETED FILES:
PRODUCTION CODE DIFF SUMMARY:
INTERNAL-ONLY DIFF SUMMARY:
DOCUMENTATION DIFF SUMMARY:
TEST RESULTS:
PUBLIC_BUILD_PURITY:
KNOWN RISKS:
```

如果存在 Transaction Core 非必要 diff：

> 不要 commit，先处理掉或明确阻塞。

## 13.6 Commit

建议拆成 1–3 个逻辑清晰的 commit，例如：

```text
build: add internal and public product flavors
test: isolate internal QA tooling from public build
docs: freeze dual-flavor productization route
```

不要把大量临时文件混在同一个 commit。

## 13.7 Push

当用户授权 push 且凭据可用：

```text
git push -u origin productization/ps00-dual-flavor
```

如果 push 失败：

- 报告真实错误；
- 不要编造成功；
- 给出用户下一条可执行命令。

## 13.8 Pull Request

如果 `gh` CLI / GitHub integration 可用，可创建 PR。

PR title 建议：

```text
Productization PS-00: Dual Build Flavor & Internal QA Isolation
```

PR description 至少包括：

```text
## Goal
Internal/Public dual build flavor from the same authoritative source.

## Internal
- Shift+Ctrl+P retained
- QA/test helpers retained
- 2,000-file stress retained

## Public
- Internal QA compile-time excluded
- Public build purity verified

## Safety
Transaction Core semantics unchanged.

## Validation
- Internal strict build:
- Public strict build:
- SmokeTests:
- Internal QA visibility:
- Public QA absence:
- PUBLIC_BUILD_PURITY:

## Follow-up
PS-01 Internal QA Center
PS-02 Public Build Purity hardening
PS-03 Release Identity / Branding
```

PR 可以创建。

不要自动 merge，除非用户明确要求。

---

# 14. GitHub Release — 本轮边界

PS-00 ≠ 正式 v1.0.0 Release。

本轮可以：

- push code；
- push docs；
- 创建 branch；
- 创建 PR；
- 创建未来 Release 所需目录/文档；
- 如用户明确要求，可创建 **Draft Release** 作为准备。

本轮不得：

- 宣布 `v1.0.0 final` 已发布；
- 上传 Internal build 当作 Public；
- 把历史 qualification Hash 用到新构建；
- 在未经 Final Public Release Gate 时 publish final Release。

正式发布顺序仍然是：

```text
PS-00
→ PS-01
→ PS-02
→ PS-03
→ PS-04
→ PS-05
→ PS-06 Public RC
→ PS-07 Final Public Release Gate
→ GitHub Release Publish
```

---

# 15. 历史 Release-qualified Hash 规则

当前历史 qualification evidence：

```text
EXE SHA256:
2E732CB03BF9470BF52043850919A5CC903CB3C4AFB8DC9AEE7ABDDB2D2FC660

Portable ZIP SHA256:
D9DC307432B5B5DD7A858FC82A1742A468572715F3A78903101BEA13B344D272
```

它们必须原样归档。

如果 PS-00 / 后续产品化改变任何：

- build configuration；
- resource；
- icon；
- metadata；
- version；
- publish setting；

新 EXE/ZIP 就是新 artifact identity。

必须重新 SHA256。

不得把旧 Hash 写成新 Public build 的 Hash。

---

# 16. 完成标准

只有以下全部满足，PS-00 才可以报告 PASS：

```text
[ ] Authoritative V0.11.1.1 baseline confirmed
[ ] Same source / same commit supports Internal and Public
[ ] Release-Internal strict build PASS
[ ] Release-Public strict build PASS
[ ] Full relevant SmokeTests PASS
[ ] Shift+Ctrl+P works in Internal
[ ] Existing small-file QA survives
[ ] Existing 2,000-file QA survives
[ ] Shift+Ctrl+P absent in Public
[ ] Internal QA absent from Public UI
[ ] Internal-only tooling compile/package exclusion verified
[ ] PUBLIC_BUILD_PURITY = PASS
[ ] Transaction Core semantic diff = NONE
[ ] No second rename execution path introduced
[ ] Git working tree reviewed
[ ] GitHub branch prepared
[ ] User receives exact push/PR state
```

---

# 17. 最终回报格式

最终必须严格按以下结构回报：

```text
PS-00 FINAL REPORT

STATUS:
PASS / PARTIAL / BLOCKED

AUTHORITATIVE BASELINE:
COMMIT / SOURCE ID:

IMPLEMENTATION:
- Build flavor mechanism:
- Internal configuration:
- Public configuration:
- InternalTools isolation:
- Shortcut isolation:
- Resource isolation:

INTERNAL TEST EDITION:
- Build:
- Shift+Ctrl+P:
- Quick QA:
- 2,000-file QA:
- Identity:

PUBLIC RELEASE EDITION:
- Build:
- Shift+Ctrl+P:
- Internal QA:
- Public artifact inspection:
- PUBLIC_BUILD_PURITY:

TRANSACTION CORE:
- Files changed:
- Semantic change:
- Alternate mutation path introduced:

TESTS:
- Exact commands:
- PASS:
- FAIL:
- SKIP:

CHANGED FILES:
NEW FILES:
DELETED FILES:

RISKS:
DEVIATIONS:

GIT:
- Repository:
- Branch:
- Commits:
- Remote:
- Push status:
- PR:
- Uncommitted files:

GITHUB PUBLISHING:
- What is now on GitHub:
- What is intentionally not yet published:
- User action still needed:

NEXT:
PS-01 Internal QA Center
```

不要只回复“完成了”。

所有 PASS 都必须带真实命令或可核验证据。
