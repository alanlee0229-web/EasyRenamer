# BatchRenamer / easy重命名 — V1 权威冻结与 Productization Sprint 基线

**日期：2026-08-30**  
**状态：V1.0 RELEASE QUALIFIED / Productization Sprint ACTIVE / Dual Build Flavor FROZEN**  
**用途：当前项目最高优先级冻结文档之一。后续窗口、Codex/代码 Agent、发布维护均应以本文为准。**

---

# 0. 一句话状态

BatchRenamer 已不再是 UI 原型。

当前已经完成并通过 Windows 实机 Release Gate 的完整安全闭环：

```text
Import / Order
→ Preview
→ Validation
→ Immutable RenamePlan
→ Plan Persistence
→ Preflight
→ Phase1 Source→Temp
→ Phase2 Temp→Target
→ Durable Journal
→ Rollback
→ Crash Recovery
→ Startup Recovery
→ History
→ Undo
→ Portable Publish
→ 20,000 real-file stress qualification
```

当前阶段不再继续扩张事务核心，而进入：

> **Productization Sprint：把已经安全可用的 V1.0 做成真正适合 GitHub 开源、普通用户直接下载 EXE、长期维护和继续扩展的现代 Windows 产品。**

Productization Sprint 自 2026-08-30 起进一步冻结为：

> **同一权威源码、同一 Transaction Core、同一产品逻辑，构建两个明确 Flavor：Internal Test Edition 与 Public Release Edition。内部测试能力长期保留，但 Public 构建必须在编译产物层面彻底排除内部测试属性。**

---

# 1. 当前 Release-qualified 权威身份

## 1.1 代码基线

当前通过完整 Release Gate 的权威代码基线：

```text
BatchRenamer V0.11.1.1 codebase
```

对外产品版本可冻结为：

```text
BatchRenamer / easy重命名 v1.0.0
```

开发阶段编号 V0.11.1.1 不再作为对外版本号。

## 1.2 平台与发布方式

```text
OS: Windows x64
Runtime: .NET 10
UI: WPF + WPF-UI
Architecture: MVVM
Publish: Self-contained
Packaging: Single-file Portable EXE
```

普通用户目标使用方式：

```text
GitHub Releases
    ↓
BatchRenamer_V1.0_Portable_x64.zip
    ↓
解压
    ↓
BatchRenamer.exe
    ↓
双击使用
```

普通用户不需要：Visual Studio、源码、Python、dotnet CLI 或手工安装 .NET Runtime。

## 1.3 当前 Release Qualification

正式 Release Gate 已通过：

```text
Strict Release Build
(warnings treated as errors)
        ↓
Full SmokeTests
        ↓
20,000 real files
Planner
→ Durable Execute
→ Journal
→ Startup Recovery Gate
→ Durable Undo
→ Idempotence
        ↓
Strict Portable Publish
```

最终 Gate 输出：

```text
===== RELEASE GATE PASS =====
PASS: strict build + full smoke + 20k real transaction/undo stress + strict portable publish.
```

## 1.4 权威发布 SHA256

```text
EXE SHA256:
2E732CB03BF9470BF52043850919A5CC903CB3C4AFB8DC9AEE7ABDDB2D2FC660

Portable ZIP SHA256:
D9DC307432B5B5DD7A858FC82A1742A468572715F3A78903101BEA13B344D272
```

对应 Release-qualified Portable 包应原样归档。允许改文件名，但不得重新解压再压缩后仍宣称是同一 ZIP Hash。


## 1.5 双 Build Flavor — 正式冻结

V1.0 Productization Sprint 不采用“一个发布版里隐藏测试入口”的方式。

正式采用：

```text
Authoritative Source / Same Commit
        │
        ├─ Release-Internal
        │    └─ BatchRenamer Internal Test
        │
        └─ Release-Public
             └─ easy重命名 / BatchRenamer v1.0.0
```

两个 Flavor 必须满足：

- 同一权威源码；
- 同一 commit；
- 同一 Core / Planner / Transaction / Recovery / Undo；
- 不允许长期维护两个功能分支；
- 不允许 Internal 另造第二套 Rename 执行逻辑；
- Public 与 Internal 的差异仅限内部测试/诊断能力、构建标识、发布身份和必要资源。

### Release-Internal

用途：开发、自测、日常回归、性能基准、异常/恢复验证。

内部版本长期允许保留并继续增强：

- `Shift + Ctrl + P` Internal QA 入口；
- 少量典型测试文件快速生成；
- 2,000 个真实文件中型压力测试；
- Undo / Recovery / Idempotence 测试辅助；
- 测试工作区初始化、Reset、Cleanup；
- Benchmark / Diagnostics；
- 后续经批准的 Crash / Fault Injection；
- 其他只服务于内部 QA 的工程工具。

建议身份：

```text
Product:
BatchRenamer Internal Test

Build Flavor:
Internal

Version:
1.0.0-internal
```

内部产物命名必须明显区别于正式发布包，例如：

```text
BatchRenamer-v1.0.0-internal-win-x64.zip
```

内部构建仍优先采用 Release 优化，不以 Debug 构建作为正式性能/时序依据。

### Release-Public

用途：GitHub Releases 面向普通用户公开分发。

正式版本必须：

- 不显示 Internal QA 入口；
- `Shift + Ctrl + P` 不注册内部测试命令；
- 不包含测试文件生成器；
- 不包含 2,000 文件测试入口；
- 不包含 Crash/Fault Injection；
- 不包含内部 Benchmark/Diagnostics 菜单；
- 不包含测试专用资源或内部测试 UI；
- 不包含仅供内部测试的隐藏命令。

关键要求：

> **Public 不是“把测试功能隐藏起来”，而是构建时从编译产物层面排除 InternalTools。**

优先采用独立 `InternalTools` 边界，并把 Flavor 条件集中到 Composition Root / Startup 注册点，避免在正式业务代码中散落大量条件编译。

任何 Internal 测试能力仍必须调用正式产品链路：

```text
Internal QA Helper
→ Preview
→ Validation
→ RenamePlanner
→ Frozen RenamePlan
→ Transaction
→ Journal / Recovery / Undo
```

不得为了测试方便实现第二套文件 mutation 路径。

### 三级测试结构

```text
Quick Smoke
少量典型真实文件
        ↓
Internal Stress
2,000 个真实文件
        ↓
Release Qualification
20,000 个真实文件
```

2,000 文件测试作为长期日常/阶段性内部压力测试；20,000 真实文件仍作为正式 Release Qualification Gate。

### Public Build Purity

正式 Release Gate 增加永久检查项：

```text
PUBLIC_BUILD_PURITY = PASS
```

至少验证：

- InternalTools 未编译进入 Public artifact；
- 内部测试快捷键无效；
- 测试资源不存在；
- QA Panel 不存在；
- Crash/Test Injection 不存在；
- Internal-only 命令和明显内部标识没有泄漏到 Public 产品表面；
- Public 仍使用同一正式安全执行链。


---

# 2. V1.0 已冻结产品能力

## 2.1 主界面/交互

已冻结：添加文件、添加文件夹、清空、搜索、只看问题、Natural Sort、排序、拖动调整顺序、上移/下移、勾选参与项目、基础名称、原名称组合、前缀、后缀、单套连续编号、起始值、步长、位数、编号位置、分隔符、实时 Preview、Validation 状态、执行重命名、Undo。

最关键交互合同继续冻结：

> **用户肉眼看到的列表顺序，就是连续编号顺序。**

## 2.2 可选高级模块（V1.0）

设置中可启用：查找替换、大小写转换。

```text
设置启用 → 主规则面板出现模块
关闭 → 模块完全隐藏 → 不影响 Preview → 参数可保留
```

## 2.3 UI 视觉冻结

主界面视觉基线：现代浅色 Windows SaaS / Productivity Tool；白色/浅灰底；蓝色主操作；圆角；轻边框；留白；表格为视觉主体；右侧规则栏；不重新卡片化；不为了“还能更漂亮”重新推翻主界面。

事务弹窗允许继续做风格统一，但属于产品化收尾，不应重新打开整个 UI。

---

# 3. V1.0 安全架构 — 不得破坏

## 3.1 分层

```text
BatchRenamer.App
    WPF UI / ViewModel

BatchRenamer.Core
    PreviewEngine
    ValidationEngine
    RenamePlanner
    Domain Models

BatchRenamer.FileSystem
    FileIdentity
    Namespace / Path probing
    PathSemantics

BatchRenamer.Transaction
    Plan Persistence
    Preflight
    Phase1 / Phase2
    Journal
    Rollback
    Recovery
    History
    Undo
```

## 3.2 永久边界

任何高级功能、插件、AI、CLI 都只允许负责：

```text
Item Context → Name Generation → Concrete ProposedName
```

后续统一进入：

```text
Validation → RenamePlanner → Frozen RenamePlan → User Confirmation → Transaction
```

永久禁止：AI 直接 File.Move；Plugin 绕过 Planner；CLI 自己造第二套 Rename 执行逻辑；Transaction 依赖 UI；RenamePlanner 依赖 RenameRuleSet/WPF；Preview 和真实执行分别解释规则。

## 3.3 两阶段事务

```text
Phase1: Source → Temp
Phase2: Temp → Target
```

支持 A↔B、循环改名、case-only rename、file/directory、FileIdentity continuity、partial failure、apply-then-throw reconciliation。

## 3.4 Durable Journal

事务目录：

```text
transaction/<TransactionId>/
├─ plan.json
├─ events.jsonl
├─ state.json
└─ session.lock
```

Journal：

```text
INTENT → Flush(true) → Move → DONE → Flush(true)
```

Recovery 必须联合 Frozen Plan + Journal + Checkpoint + Real filesystem state + FileIdentity + PathSemantics，不能机械相信 Journal。

## 3.5 Undo / Recovery

已验证：completed transaction Undo；partial Phase1/Phase2 rollback；startup recovery；rollback crash recovery；second Undo idempotent；external occupancy fail-closed；ManualRequired；SessionBusy；per-transaction lease；global catalog lease。

---

# 4. 性能与规模状态

## 4.1 Preview

历史 Windows 实机：

```text
20,000 items Preview ≈ 76 ms
```

该架构禁止退回 UI 主线程逐行 Refresh。

## 4.2 Release Stress

20,000 个真实文件完整事务 / Undo Release Gate 已 PASS。

历史 V0.11.0 压测曾暴露：

```text
JournaledRenameMutationFileSystem
每次 Move 重新扫描 Frozen Plan
→ O(N²)
```

已在 V0.11.1 修复为 transition index / O(1) lookup，并最终通过 Release Gate。

## 4.3 后续性能主线

未来性能工程优先看感知延迟和流畅性：Import、Preview、Planner/Final Validation、Execute preparation latency、Startup、large-list scrolling、memory、cold-start time。

所有性能优化必须 profiler / benchmark 证据驱动；不凭感觉重写；不无故扰动 Release-qualified Transaction Core。

---

# 5. 产品定位升级

旧定位：一个安全、好用的 Windows 批量重命名工具。

新长期定位：

> **A modern, safe and extensible batch renaming toolkit for Windows.**

中文：

> **一个现代、安全、可扩展的 Windows 批量重命名工具。**

三个长期关键词：Modern / Safe / Extensible。

---

# 6. 发布与开源策略 — 当前推荐

## 6.1 源码可以公开

开源与易用分发不冲突。

普通用户入口：

```text
GitHub Releases → EXE / Portable ZIP
```

开发者入口：

```text
GitHub Repository → Source / Docs / SDK
```

README 第一屏必须强调“Download for Windows”，不能让普通用户误以为必须编译源码。

## 6.2 GitHub 目标

GitHub 不仅托管代码，还承担 Release、Issues、Discussions、Feature Requests、Security、Documentation、Roadmap、Community、Stars/visibility。

## 6.3 许可证

Productization Sprint 中正式评估 MIT、Apache-2.0、GPL 系列。当前尚未最终冻结。

---

# 7. EXE 体积与速度策略

用户已明确：100 MB 以内可以接受；通用、流畅、快速、高级感优先于追求几 MB。

优先级冻结为：

```text
1. 文件安全
2. UI 流畅 / responsiveness
3. 启动速度
4. 产品高级感
5. 单文件 / 免安装
6. 极致体积
```

当前约 64 MB self-contained single-file 路线可以接受。

Productization Sprint 应 benchmark：Compact Portable vs Fast Portable，实际比较 EXE size、ZIP size、cold start、warm start、first-window latency、working set。若 Fast <100 MB 且明显更快，优先 Fast。

禁止为了几 MB 重写 C++/Native 而丢失当前已资格化安全基线。

---

# 8. Simple Core + Progressive Power — 长期产品原则

产品不能变成功能垃圾场。

```text
普通用户 → 简单、漂亮、快速 GUI
高级用户 → Advanced Rule Engine / Rule Chain / Scope / Preset
开发者 → CLI / Plugin SDK
AI / Agent → 受控 Spec / ProposedName 生成
```

主界面保持轻量，高级能力渐进展开。

---

# 9. Productization Sprint — 重构后正式主路线

在实现 V1.1 前，先完成 Productization Sprint。当前执行顺序冻结如下。

## PS-00 — Dual Build Flavor & Internal QA Isolation

当前最高优先级。

目标：

```text
同一权威源码 / 同一 commit
        │
        ├─ Release-Internal
        │    └─ 保留 Shift+Ctrl+P + 全部内部 QA 能力
        │
        └─ Release-Public
             └─ 编译级排除内部测试能力
```

任务：

- 建立 Internal / Public Flavor；
- 明确构建属性和输出命名；
- 将测试能力收敛到清晰的 InternalTools 边界；
- 将 Flavor 注册尽量集中在 Composition Root / Startup；
- 保留现有 `Shift + Ctrl + P` 测试能力；
- Public 构建彻底排除 InternalTools；
- Internal 仍调用正式 Preview / Validation / Planner / Transaction 链；
- 明确 Internal/Public 版本与窗口标识，避免误发；
- 为后续 GitHub Release 构建可验证的 Public artifact。

Gate：

```text
Release-Internal:
Shift+Ctrl+P = available

Release-Public:
Shift+Ctrl+P = unavailable
InternalTools = not compiled / not packaged
```

且不得修改 Transaction Core 语义。

## PS-01 — Internal QA Center

把现有内部测试能力正式收编为长期工程资产，不再作为临时代码。

建议结构：

```text
Shift+Ctrl+P
    ↓
Internal QA Center
    ├─ Quick Smoke
    ├─ 2,000 Real Files
    ├─ Undo Test
    ├─ Recovery Test
    ├─ Idempotence Test
    ├─ Benchmark
    ├─ Diagnostics
    └─ Cleanup / Reset
```

原则：

- 优先迁移/整理现有测试入口，不重新发明测试框架；
- 测试文件必须进入隔离工作区；
- Cleanup 不得触碰用户真实目录；
- 所有 Rename/Undo/Recovery 验证继续走正式执行链；
- 后续 Regex / Template / Plugin 等新功能均应在 Internal QA 中增加对应场景。

## PS-02 — Public Build Purity Gate

为 Release-Public 建立正式纯净度 Gate。

检查至少包括：

- InternalTools 未进入编译产物；
- Internal QA 菜单/快捷键不存在；
- Test Workspace Generator 不存在；
- 2,000 文件测试入口不存在；
- Crash/Fault Injection 不存在；
- Internal-only Benchmark/Diagnostics 不对公开用户暴露；
- 测试资源不随 Public publish 输出；
- Public metadata 不出现 Internal Test 身份。

最终输出：

```text
PUBLIC_BUILD_PURITY = PASS / FAIL
```

该 Gate 以后属于每次正式 Release 的永久项目。

## PS-03 — Release Identity / Branding

在双 Flavor 边界稳定后完成：

- 正式产品名称；
- 英文名/中文名；
- Logo；
- App Icon；
- EXE Version Metadata；
- Product/File Description；
- Internal/Public identity；
- v1.0.0 Release naming。

当前推荐身份：

```text
Primary display brand:
easy重命名

English / technical name:
BatchRenamer

Public executable:
BatchRenamer.exe
```

Internal artifact 与 Public artifact 必须明显区分。

## PS-04 — GitHub Presentation / Community Foundation

完成：

- 高质量 README；
- README Hero；
- 真实主界面 Screenshot；
- Demo GIF；
- Features；
- Safety Architecture；
- 20k stress qualification evidence；
- Download 入口；
- Roadmap；
- Bug Report；
- Feature Request；
- File Safety / Recovery Problem 专用模板；
- Compatibility template；
- Discussions categories；
- SUPPORT.md；
- SECURITY.md；
- CONTRIBUTING.md；
- Code of Conduct（如决定需要）。

README 第一屏必须服务普通用户下载，不以源码构建说明或 C# 架构作为首要内容。

## PS-05 — Fast vs Compact Benchmark

基于同一 Release-qualified 产品逻辑，对比：

```text
Compact Portable
vs
Fast Portable
```

至少比较：

- EXE size；
- ZIP size；
- cold start；
- warm start；
- first-window latency；
- large-list Preview；
- 2,000-file internal stress；
- working set / memory。

优先在 Release-Internal 的 QA/Benchmark Harness 上完成测量，再将胜出 publish 配置应用到 Public。

若 Fast <100 MB 且在启动/流畅性上有明确收益，优先 Fast。

不得为了体积扰动 Transaction Core。

## PS-06 — Release Packaging / Public RC

形成正式：

```text
BatchRenamer v1.0.0 RC
```

RC 只能来自 `Release-Public`。

完成：

- Release Notes；
- Portable EXE；
- Portable ZIP；
- SHA256SUMS；
- 发布目录与命名统一；
- GitHub Release draft / upload structure；
- metadata / icon / version check；
- README 下载链接核对。

当前已通过资格测试的历史 EXE/ZIP SHA256 必须原样归档作为历史 qualification evidence。

只要 icon、metadata、version、resource、publish 设置导致二进制变化，新的 Public RC 就是新的 artifact identity，必须重新计算 SHA256，绝不能沿用旧 Hash。

## PS-07 — Final Public Release Gate

针对“实际准备上传 GitHub Releases 的最终 Public artifact”执行：

```text
Strict Release Build
(warnings as errors)
        ↓
Full SmokeTests
        ↓
PUBLIC_BUILD_PURITY
        ↓
20,000 real files
Planner
→ Durable Execute
→ Journal
→ Startup Recovery Gate
→ Durable Undo
→ Idempotence
        ↓
Strict Portable Publish
        ↓
Metadata / Icon / Package Verification
        ↓
SHA256
        ↓
GitHub Release Artifact Verification
```

全部 PASS 后：

```text
PUBLIC RELEASE READY
```

## PS-08 — Diagnostics Foundation 规划

建议 V1.0.x 增加“设置 → 导出诊断信息”。

诊断包不得包含用户文件内容，可包含：

- app version；
- build flavor；
- Windows version；
- environment；
- transaction status；
- error code；
- sanitized log；
- optionally hashed/redacted paths。

Public diagnostics 必须坚持 privacy-by-default。

## PS-09 — Roadmap Formalization

正式整理 V1.1、V1.2、V1.3、V1.5、V2.0，并根据 V1.0 实际 Issue/Discussion 反馈允许调整候选功能排序。

## PS-10 — Extension Architecture Review

只做预留审查，不立即实现：

- Plugin SDK boundary；
- CLI engine boundary；
- AI/Agent boundary；
- Skill/MCP boundary；
- Preset/Rule package format。

永久不变：

```text
External capability
→ Spec / ProposedName
→ Validation
→ RenamePlanner
→ Frozen RenamePlan
→ Transaction
```
---

# 10. Roadmap — 当前冻结版本

## V1.0 — Core Product

已完成：Modern GUI、Preview、Validation、safe two-phase rename、Journal、Crash Recovery、Startup Recovery、Undo、Portable、20k qualification。

## V1.1 — Power User

候选：Template Engine、Regex、Rule Chain、Rule Scope、Grouped Sequence、Recursive Folder、extension risk mode。进入 V1.1 前，允许根据 V1.0 真实使用重新排序优先级。

## V1.2 — Personalization

Dark Mode、Accent Color、Density（Comfortable/Compact）、Custom Theme、Preset Management、Rule Preset Export/Import。自定义皮肤必须走有限 Theme Token，不允许无限 CSS 化。

## V1.3 — Automation

CLI、config files、headless plan、rule import/export、scripting。

## V1.5 — Extension Platform

Plugin SDK、Metadata Provider API、Rule Provider API、Import Provider、Plugin Manager。

潜在社区插件：EXIF、ID3、PDF Metadata、Pinyin、Hash、Anime/TV rename、Photo organizer。

## V2.0 — Intelligent Renaming

AI Rename、natural-language rule generation、image/content understanding、Agent interface、Skill、MCP。

永久要求：AI/Agent 只生成 Spec/ProposedName，绝不直接执行文件 mutation。

---

# 11. Preset / Recipe 生态

优先级较高。用户可保存摄影整理、论文整理、电视剧整理、项目截图整理、发票整理等 Preset。未来格式可考虑 `*.brrule`，支持 Save / Import / Export / Share / GitHub community presets。

Preset 可能比 Plugin 更早形成生态。

---

# 12. GitHub README 建议第一屏

```text
easy重命名 / BatchRenamer

A modern, safe and extensible batch renaming toolkit for Windows.

[Download for Windows] [Features] [Roadmap]
```

Hero 后重点展示：Modern UI、Fast Preview、Safe Transactions、Undo、Crash Recovery、Portable、Extensible。

核心传播证据：

```text
20,000 real-file transaction/undo stress tested
```

不要一上来展示大量 C# 架构文字。

---

# 13. 维护 / Issue 规范

建议 Issue 分类：Bug、File Safety / Recovery Problem、Feature Request、Compatibility。

严重度：

```text
P0: 文件丢失/覆盖/Recovery破坏
P1: 无法执行/崩溃/严重兼容
P2: 普通功能 Bug / UI 状态
Enhancement: Roadmap
```

P0/P1 必须重新做相关安全 Gate。不要要求用户上传私人文件。

---

# 14. 协作模式：Main Brain + Codex

后续推荐：

> ChatGPT 主窗口 = 产品大脑 / 产品经理 / 总体架构师 / Gate Authority  
> Codex = 主要代码实现 Agent

主脑负责：产品方向、架构、API/数据合同、不可破坏约束、任务拆解、代码审查、测试策略、Release Gate、Freeze/Handover。

Codex 负责：按任务包修改代码、局部重构、测试实现、文档同步、回传 diff/test result/risks。

Codex 在获得用户明确授权且本地 GitHub 凭据/远程仓库可用时，还可以配合完成：

```text
inspect git status / remote
→ create task branch
→ implement
→ local tests / gates
→ present diff
→ commit
→ push branch
→ optionally create / update PR
```

GitHub 协作规则：

- 默认不要直接向 `main/master` 强推；
- 优先使用独立任务分支；
- push 前必须先展示关键 diff、测试结果和风险；
- 不得上传 secrets、私有路径、测试生成的大量临时文件或本地 recovery/transaction 隐私数据；
- 不得把 Internal artifact 当作 Public Release 上传；
- 未通过 Public Build Purity 与最终 Release Gate 前，不得创建“正式 v1.0.0 已发布”的事实状态；
- Release 上传必须区分 Internal 测试包与 Public 正式包；
- 若需要 GitHub Release，优先先建 Draft Release，最终 publish 由用户确认。

禁止让 Codex 自行改产品方向、扩大 scope、重写 Transaction Core、改冻结 UI、绕过 Gate。

---

# 15. 窗口管理

当前长窗口应在 Productization Sprint 正式执行前迁移。

新窗口必须读取：

1. `BatchRenamer_V1_Authoritative_Freeze_20260830.md`
2. `BatchRenamer_Window_Handover_20260830.md`
3. 如使用 Codex：`BatchRenamer_Codex_Execution_Protocol_20260830.md`
4. 当前 Release-qualified source package / Git repo
5. 原始 `BatchRenamer_Authoritative_Handover_20260828.md` 仅作为历史基线，不应覆盖本文更新状态。

---

# 16. 当前停止点

**停止继续改 Transaction Core。**

当前最高优先级任务已经从原 P0/P1 调整为：

> **PS-00 — Dual Build Flavor & Internal QA Isolation**

必须先完成并验证：

```text
Authoritative Source
        │
        ├─ Release-Internal
        │    └─ Shift+Ctrl+P + internal QA available
        │
        └─ Release-Public
             └─ internal QA compile-time excluded
```

PS-00 Gate PASS 后，依次推进：

```text
PS-01 Internal QA Center
→ PS-02 Public Build Purity Gate
→ PS-03 Release Identity / Branding
→ PS-04 GitHub Presentation / Community
→ PS-05 Fast vs Compact Benchmark
→ PS-06 Public RC / Packaging
→ PS-07 Final Public Release Gate
```

在 Productization Sprint 主线完成前：

- 不启动 V1.1 Regex / Template 等高级功能；
- 不把 Internal 测试能力混入 Public 发布包；
- 不从旧源码基线实施 PS-00；
- 不把历史 Release-qualified SHA256 冒充新 Public RC Hash。
---

# 17. 一句话接管原则

> **V1.0 已经安全、可执行、可恢复、可撤销并通过 20k 真实文件资格测试。现在先用同一权威源码建立 Internal/Public 双 Flavor：内部测试能力长期保留，公开构建编译级彻底排除；随后再完成品牌、GitHub、性能选择、Public RC 与最终 Release Gate，把它做成普通用户愿意下载、开发者愿意 Star、社区愿意贡献、未来能够扩展的现代开源 Windows 产品。**
