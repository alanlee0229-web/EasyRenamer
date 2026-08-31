# EasyRenamer / BatchRenamer v1.0 Engineering Qualification

本文集中保存 v1.0 发布前各阶段资格测试中值得长期保留的最终结论。
阶段性过程报告已从工作树清理，可通过 Git 历史查阅。

## Release Identity

- Product：`easy重命名 / BatchRenamer`（Public）；`BatchRenamer Internal Test`（Internal）
- Version：`1.0.0`（开发阶段编号 V0.11.1.1，不作为对外版本号）
- Platform：Windows x64
- Runtime：.NET 10（WPF + WPF-UI 4.3.0，MVVM）
- Publish：Self-contained、Single-file Portable EXE
- Public profile = `COMPACT`（PS-05 基准结论；win-x64、self-contained、single-file、compression enabled、ReadyToRun false、trimming false）
- License：Apache-2.0（ZIP 内 LICENSE SHA256 `CFC7749B96F63BD31C3C42B5C471BF756814053E847C10F3EB003417BC523D30`，为 Apache-2.0 官方文本）
- Authenticode = UNSIGNED（签名会改变 EXE bytes，正式发布保持未签名）

## Safety Architecture

所有 mutation 必须经过同一条正式链，禁止建立第二条文件移动路径：

```text
Validation
→ RenamePlanner
→ Frozen RenamePlan
→ Transaction
→ Journal / Recovery / Undo
```

细节见 [SAFETY_ARCHITECTURE](../SAFETY_ARCHITECTURE.md)。

## Dual Build Flavor

```text
Authoritative Source / Same Commit
        │
        ├─ Release-Internal
        │    └─ BatchRenamer Internal Test
        └─ Release-Public
             └─ easy重命名 / BatchRenamer v1.0.0
```

- 同一权威源码、同一 commit、同一 Core / Planner / Transaction / Recovery / Undo。
- Public 不是"隐藏测试功能"，而是编译阶段从产物层面排除 `InternalTools`（compile-time isolation）。
- Internal 测试能力只能调用正式 Preview / Planner / Transaction API。

## Public Purity

- Public Build Purity Gate = PASS（InternalTools 未进入 Public artifact；类型、命令、资源、依赖、身份与发布目录全部验证）。
- Negative Control = PASS（Internal artifact 被 BUILD_FLAVOR 正确拒绝，exit code 1）。

## Final App Icon

- 权威 ICO：`src/BatchRenamer.App/Assets/BatchRenamer.ico`
- Icon SHA256：`467DF074F455504261CE35B7B8F0B5494A575DB8EEE41084F8515F3AB97306D1`
- ICO 结构：7 个 32-bit PNG frame（16/24/32/48/64/128/256）。
- Public / Internal 普通 EXE 与 Portable EXE 的 PE 图标资源均与批准 ICO 逐字节一致。
- Public 窗口标题：`easy重命名`；Internal 窗口标题：`easy重命名 — INTERNAL TEST`。

## Qualification

Release-Public strict build（warnings as errors）：

```text
0 warnings / 0 errors
```

SmokeTests：

```text
510 PASS / 0 Skip
```

20,000 real-file gate（`python tools\run_release_stress.py`，2026-08-31）：

- Planner = PASS
- Execute = PASS（20,000 真实文件，40,000 execute moves）
- Journal = PASS（80,000 events）
- Startup Recovery = PASS（双次扫描）
- Undo = PASS（40,000 undo moves）
- Second Undo mutations = 0
- temp residual = 0
- namespace restored（精确复原）

报告：`artifacts\stress\release-stress-20000-f1dd82bea03f44e39da7946329e7d123.json`（本机 artifact，不入 Git）。

## Final Release Identity

- Product Source Commit：`a42b5a887a7e79580473291614fac3a64825d3ab`
- EXE SHA256：`7745D6FAFA48ABBE8D2789EE1E2E071D7FA3183F6FECBD5A5B552E4D21690702`
- ZIP SHA256：`9FA2C33A6D4B3339FD763FB4077F1FD9374DF3DB4846BFFB276808C847D0DECB`
- Tag：`v1.0.0`（指向上述 Product Source Commit）
- Release status：Published / Release Qualified（2026-08-31，https://github.com/alanlee0229-web/EasyRenamer/releases/tag/v1.0.0）

## Evidence Sources

- `handoff.md`（PS-07 最终结果与正式 Hash）
- `BatchRenamer_V1_Authoritative_Freeze_20260830_DualFlavor_Update.md`（Dual Flavor 与安全架构冻结）
- Git 历史中的 PS-03B / PS-05 / PS-06 / PS-07 阶段报告（本工作树已清理）
