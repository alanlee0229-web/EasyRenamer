# EasyRenamer 接管摘要

## 当前目标

v1.0.0 已正式发布（2026-08-31）。项目进入公开发布后维护阶段。

## 权威信息

- 正式 Product Source Commit：`a42b5a887a7e79580473291614fac3a64825d3ab`（RC Manifest 记录值，正式 Release tag target，不得改为 merge commit）。
- PS-06 Release Docs Merge（PR #8）：`86702fa599cb4f0379c7d63a443bcb9de9d22501`（PR #8 状态 = MERGED）。
- 项目许可证：Apache License 2.0；SPDX：`Apache-2.0`。
- 远程仓库：https://github.com/alanlee0229-web/EasyRenamer.git
- 正式约束：`BatchRenamer_V1_Authoritative_Freeze_20260830_DualFlavor_Update.md`。

## PS-07 结果（2026-08-31）

```text
PS-07 = PASS
v1.0.0 = RELEASE QUALIFIED
Final Release = PUBLISHED（2026-08-31，https://github.com/alanlee0229-web/EasyRenamer/releases/tag/v1.0.0）
```

- Frozen RC Drift：pre-gate 与 post-gate 双重复核，五文件 SHA256 全部与 PS-06 一致，`RC_FREEZE=PASS`。
- 冻结 EXE / ZIP 检查：Public identity、Icon（7 帧逐字节对齐批准 ICO）、内部标记扫描 0 命中、ZIP 精确 5 文件、ZIP EXE 与 standalone byte-identical、0 字体文件。
- Launch Smoke：standalone EXE 与 ZIP EXE 均启动正常，窗口标题 `easy重命名`，优雅关闭 exit 0。
- Strict build：Release-Public 0 警告 / 0 错误；SmokeTests 510 PASS / 0 Skip。
- 20k 正式资格 Gate：`python tools\run_release_stress.py`（canonical）；20,000 真实文件、40,000 execute moves、40,000 undo moves、Journal 80,000 events、双 Startup Recovery 扫描、second Undo 0 mutations、namespace 精确复原、0 Temp residual；报告 `artifacts\stress\release-stress-20000-f1dd82bea03f44e39da7946329e7d123.json`。
- Build wording：RC artifact rebuild = NO；validation builds（Release-Public strict build、stress harness build）= YES；RC republished/modified/repackaged/signed = NO。

## 正式 Release Hash（v1.0.0）

- EXE SHA256：`7745D6FAFA48ABBE8D2789EE1E2E071D7FA3183F6FECBD5A5B552E4D21690702`
- ZIP SHA256：`9FA2C33A6D4B3339FD763FB4077F1FD9374DF3DB4846BFFB276808C847D0DECB`
- RC1 冻结目录：`artifacts/release/v1.0.0-rc1/`（继续只读，不得 rebuild / republish / re-zip / sign / 改名 / 修改 metadata）。

## GitHub 正式 Release（已发布）

- Tag：`v1.0.0`（已验证指向 `a42b5a887a7e79580473291614fac3a64825d3ab`）；发布时间 2026-08-31。
- 4 个冻结资产全部 uploaded，远端 SHA256 digest 与冻结 Hash 完全一致（EXE / ZIP / SHA256SUMS / RELEASE_MANIFEST）。
- Release body = 冻结 Release Notes + Unsigned/SmartScreen 提示（中英双语）；冻结 `RELEASE_NOTES_v1.0.0.md` 本体未改动。
- 不得重新上传或替换已有 assets；如需变更须走新版本流程。

## 固定决策

- RC1 五文件 bytes 冻结；任何漂移必须 `BLOCKED_ARTIFACT_DRIFT`，禁止覆盖 RC1。
- 正式 EXE/ZIP 不提交 Git；Authenticode 保持 UNSIGNED（签名会改变 EXE bytes）。
- Git 提交、分支和 PR 文案使用中文；不直接推送 `main`。
- PR #8 已合并（merge commit `86702fa`），仅承载 PS-06 release docs；不改变正式 v1.0.0 Product Source Commit。

## 发布后可选待办

- Screenshot：READY（真实 Public RC 主界面截图已 9/9 视觉审查通过，冻结于 `docs/assets/main-window.png`，README 已引用）。
- Demo GIF：PENDING。
- Discussions：USER_ACTION_REQUIRED（用户在 GitHub Settings 手动启用）。

## DOCUMENTATION CLEANUP

- Completed：2026-08-31（`maintenance/v1-doc-cleanup` 分支）。
- 已创建唯一工程资格总结：`docs/releases/V1_ENGINEERING_QUALIFICATION.md`。
- 已清理工作树中的历史阶段性文档（任务提示词、INTERNAL_AUDIT / WINDOWS_ACCEPTANCE 系列、PS 阶段报告、旧 UI / 架构 / roadmap 文档、frozen_reference、根目录历史 SHA256SUMS.txt）；永久约束已迁入 `docs/SAFETY_ARCHITECTURE.md`。
- 历史过程文档一律通过 Git history 查阅；不修改 Release assets 与 tag。

## 下一步

v1.0.0 已上线。后续可选：Demo GIF、GitHub Discussions 启用（用户手动）、网站图标 icon-512.png。下一个版本（v1.0.1+）走正常 PR → 修复 → RC → Gate 流程，禁止直接修改已发布 v1.0.0 资产。本文只保留最新状态与关键决策。
