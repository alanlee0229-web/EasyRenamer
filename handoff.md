# EasyRenamer 接管摘要

## 当前目标

PS-06 Public RC / Release Packaging 已完成本机冻结、验收、提交和推送；PR #8 待评审，不自动合并。

## 权威信息

- 基线：`main` 提交 `a42b5a887a7e79580473291614fac3a64825d3ab`，已包含合并后的 PS-05.5。
- 项目许可证：Apache License 2.0；SPDX：`Apache-2.0`。
- 远程仓库：https://github.com/alanlee0229-web/EasyRenamer.git
- 正式约束：`BatchRenamer_V1_Authoritative_Freeze_20260830_DualFlavor_Update.md`。
- 当前任务：PS-06 Public RC / Release Packaging。

## 当前状态

- 唯一 RC：`BatchRenamer-v1.0.0-RC1`；路径：`artifacts/release/v1.0.0-rc1/`；这是 PS-07 的冻结输入。
- Canonical Public Profile：COMPACT；win-x64、self-contained、single-file、compression enabled、ReadyToRun false、trimming false；SDK `10.0.400`。
- Public strict build 0/0；Smoke 510 PASS / Skip 0；Public Purity 与 Negative Control PASS。
- EXE metadata、Public identity 和 PE 图标 PASS；Approved ICO SHA256 `467DF074F455504261CE35B7B8F0B5494A575DB8EEE41084F8515F3AB97306D1`；Authenticode UNSIGNED。
- ZIP 原样包含 Apache-2.0 LICENSE、WPF-UI 4.3.0 notices 及两个 WPF-UI license；没有单独 Segoe 字体文件或开发/内部内容。
- EXE SHA256：`7745D6FAFA48ABBE8D2789EE1E2E071D7FA3183F6FECBD5A5B552E4D21690702`；ZIP SHA256：`9FA2C33A6D4B3339FD763FB4077F1FD9374DF3DB4846BFFB276808C847D0DECB`。
- `SHA256SUMS_SELF_VERIFY`、两轮重读、`RC_FREEZE` 均 PASS；完整 snapshot 见 `docs/PS06_PUBLIC_RC.md`。
- Production / Transaction Core 文件变更 0；语义变化与 second mutation path NONE。
- PS-06 实现提交：`a62bcf5`；PR：https://github.com/alanlee0229-web/EasyRenamer/pull/8。

## 固定决策

- RC1 五个文件 bytes 已冻结；PS-07 只能重读验证，不得 rebuild、repackage、改名、签名或修改。
- 若任何 artifact bytes 漂移，PS-07 必须 `BLOCKED_ARTIFACT_DRIFT`，返回 PS-06 创建 RC2，禁止覆盖 RC1。
- 正式 EXE/ZIP 不提交 Git；Draft Release 延后 PS-07，正式 Release 仍为 NO。
- Git 提交、分支和 PR 文案使用中文；不直接推送 `main`；不自动合并 PS-06。

## 下一步

完成 PS-06 PR #8 评审与合并后，由 PS-07 首先运行 `python tools\verify_frozen_rc.py`。本文只保留最新状态与关键决策。
