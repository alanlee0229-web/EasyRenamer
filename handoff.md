# EasyRenamer 接管摘要

## 当前目标

PS-05 Fast vs Compact Portable Benchmark 已完成实现与本机验收；当前分支为 `productization/ps05-fast-vs-compact`，待中文提交、推送并创建 PR，不自动合并。

## 权威信息

- 基线：`main` 提交 `6cc766f`，已包含合并后的 PS-04。
- 外部版本：easy重命名 / BatchRenamer v1.0.0。
- 远程仓库：https://github.com/alanlee0229-web/EasyRenamer.git
- 正式约束：`BatchRenamer_V1_Authoritative_Freeze_20260830_DualFlavor_Update.md`。
- 当前任务：PS-05 Fast vs Compact Portable Benchmark。

## 当前状态

- 唯一结论：`RECOMMENDED_PUBLIC_PROFILE = COMPACT`。
- 默认 `python tools\publish_portable.py --flavor public` 直接使用 Compact；Fast 只保留显式实验参数 `--profile fast`。
- Compact：64.347 MiB EXE / 58.880 MiB ZIP；Fast：156.565 MiB EXE / 63.708 MiB ZIP。
- Fast Warm startup median / p90 改善 18.82% / 20.29%，但 EXE +143.31% 且超过 100 MB。ReadyToRun + compression 探索为 69.394 MiB，但 Warm median 约慢 2.50%，不满足收益阈值。
- 15 次/候选启动和 15 次/候选 20K Preview 均按固定种子交错；窗口可见、Dispatcher 响应、Working Set 和 Private Memory 均有重复采样。
- Compact 与 Fast 的 2K 真实文件 Planner / Execute / Journal / Startup Gate / Undo / Idempotence 均 PASS，Temp residual 0，沙箱已删除。
- 两候选严格发布与 Public Purity PASS；最终默认 Public 严格构建 0/0、Smoke 510 PASS / Skip 0、Public Purity 与 Negative Control PASS。
- PS-05 qualification hash 已写入 `docs/PS05_FAST_VS_COMPACT_BENCHMARK.md`，明确不是最终 Release hash。
- Product / Transaction Core 文件变更 0；语义变更 NONE；第二 mutation path NONE。
- 原始 benchmark EXE、ZIP、CSV、JSON 留在被忽略的 `artifacts/benchmarks/ps05/`，不得提交或上传 Release。

## 固定决策

- Canonical Public Publish Profile 为 Compact：ReadyToRun=false、single-file compression=true、Trimmed=false。
- 文件安全永久优先于性能；不得为了 benchmark 修改 Transaction Core 或新增 mutation path。
- 未经用户批准不得生成或接入最终 Logo/Icon；Screenshot、Demo GIF、Icon 仍为 PENDING。
- Git 提交、分支和 PR 文案使用中文；不直接推送 `main`；不自动合并 PS-05；不创建正式 Release。

## 下一步

提交并创建 PS-05 PR；合并后进入 PS-06 Public RC / Release Packaging。本文只保留最新状态与关键决策。
