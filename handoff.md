# EasyRenamer 接管摘要

## 当前目标

PS-03B Final App Icon Integration 已完成实现与本机验收；当前分支为 `productization/ps03b-final-app-icon`，PR #6 待评审，不自动合并。

## 权威信息

- 基线：`main` 提交 `3c01254`，已包含合并后的 PS-05。
- 外部版本：easy重命名 / BatchRenamer v1.0.0。
- 远程仓库：https://github.com/alanlee0229-web/EasyRenamer.git
- 正式约束：`BatchRenamer_V1_Authoritative_Freeze_20260830_DualFlavor_Update.md`。
- 当前任务：PS-03B Final App Icon Integration。

## 当前状态

- 正式图标单一权威路径：`src/BatchRenamer.App/Assets/BatchRenamer.ico`。
- ICO SHA256：`467DF074F455504261CE35B7B8F0B5494A575DB8EEE41084F8515F3AB97306D1`；批准输入未修改。
- ICO 包含 16、24、32、48、64、128、256 像素的 7 个 32-bit PNG frame，ICONDIR 与数据边界 PASS。
- Public/Internal 共用 `ApplicationIcon` 与 WPF `MainWindow.Icon`；EXE / Explorer / Window / Taskbar / Alt+Tab wiring PASS。
- Public/Internal 普通构建 EXE 和 Portable EXE 的 PE 图标资源均与批准 ICO 逐 frame、逐字节一致。
- Public 窗口与 metadata 保持 `easy重命名` / `easy重命名 / BatchRenamer` / `1.0.0`。
- Internal 窗口与 metadata 保持 `easy重命名 — INTERNAL TEST` / `BatchRenamer Internal Test` / `1.0.0-internal`；Internal QA regression PASS。
- 双口味严格构建 0/0；Smoke 510 PASS / Skip 0；Public Purity 与 Negative Control PASS。
- Qualification hashes 已写入 `docs/PS03B_FINAL_APP_ICON.md`，明确不是最终 Release hash。
- Transaction Core diff NONE；语义变化 NONE；第二 mutation path NONE。
- EXE、ZIP、`bin/obj`、`artifacts` 和本地批准输入包均不提交。
- PS-03B 实现提交：`3ee234a`；PR：https://github.com/alanlee0229-web/EasyRenamer/pull/6。

## 固定决策

- Release-Public 与 Release-Internal 必须共用同一正式 ICO，身份差异只由冻结标题和 metadata 表达。
- Canonical Public Publish Profile 继续为 Compact；PS-03B 不改变发布性能策略。
- 文件安全永久优先；不得为了资源接入修改 Transaction Core 或新增 mutation path。
- Screenshot 与 Demo GIF 仍为 PENDING；网站用 `icon-512.png` 不用未批准衍生图替代。
- Git 提交、分支和 PR 文案使用中文；不直接推送 `main`；不自动合并 PS-03B；不创建正式 Release。

## 下一步

完成 PS-03B PR 后进入 PS-06 Public RC / Release Packaging。本文只保留最新状态与关键决策。
