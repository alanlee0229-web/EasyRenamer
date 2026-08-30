# EasyRenamer 接管摘要

## 当前目标

PS-05.5 Open Source License Freeze 已完成实现与本机验收；当前分支为 `productization/ps055-license`，待中文提交、推送并创建 PR，不自动合并。

## 权威信息

- 基线：`main` 提交 `bad2661`，已包含合并后的 PS-03B。
- 项目许可证：Apache License 2.0；SPDX：`Apache-2.0`。
- 远程仓库：https://github.com/alanlee0229-web/EasyRenamer.git
- 正式约束：`BatchRenamer_V1_Authoritative_Freeze_20260830_DualFlavor_Update.md`。
- 当前任务：PS-05.5 Open Source License Freeze。

## 当前状态

- 根目录 `LICENSE` 与 Apache 官方 `LICENSE-2.0.txt` 完全同字节：11,358 bytes，SHA256 `CFC7749B96F63BD31C3C42B5C471BF756814053E847C10F3EB003417BC523D30`。
- README 明确 `License: Apache-2.0` 并链接根 LICENSE；第三方依赖不因本项目许可证而被重新授权。
- 仓库原先没有根级 LICENSE / NOTICE；本轮未编造 NOTICE。
- 应用直接外部依赖：WPF-UI 4.3.0（MIT）；传递依赖：WPF-UI.Abstractions 4.3.0（MIT）。
- WPF-UI NuGet 包附带 `LICENSE.md` 与 `ThirdPartyNotices.txt`，后者列出四项 MIT notice 和 Segoe Fluent Icons Font 的 Microsoft Platform 使用/禁止再分发条款。
- PS-06 / PS-07 打包必须从实际锁定的 WPF-UI 4.3.0 包原样保留这两个上游文件，并确认不独立分发 Segoe 字体文件；PS-05.5 不复制、改写未知 notice，不修改打包配置。
- Markdown 21 个本地链接 PASS；Public 严格构建 0/0；Smoke 510 PASS / Skip 0；Public Purity 与 Negative Control PASS。
- Production / Transaction Core 文件变更 0；语义变化 NONE；UI、构建、发布配置变更 0。
- 详细审查见 `docs/PS055_LICENSE_FREEZE.md`。

## 固定决策

- 项目原始作品许可证固定为 Apache-2.0，不改为 MIT / GPL / AGPL。
- 第三方组件保留各自许可证与 notice，根 Apache-2.0 不覆盖第三方授权。
- Release-Public 与 Release-Internal 继续共用正式 ICO；Canonical Public Publish Profile 继续为 Compact。
- Git 提交、分支和 PR 文案使用中文；不直接推送 `main`；不自动合并 PS-05.5；不创建正式 Release。

## 下一步

提交并创建 PS-05.5 PR；合并后进入 PS-06 Public RC / Release Packaging。本文只保留最新状态与关键决策。
