# EasyRenamer 接管摘要

## 当前目标

PS-01 Internal QA Center 已实现并通过本地 Gate；成果位于 `productization/ps01-internal-qa-center`，Transaction Core 语义未修改。

## 权威信息

- 基线：BatchRenamer V0.11.1.1 codebase。
- 外部版本：easy重命名 / BatchRenamer v1.0.0。
- 远程仓库：https://github.com/alanlee0229-web/EasyRenamer.git
- 正式约束：`BatchRenamer_V1_Authoritative_Freeze_20260830_DualFlavor_Update.md`。
- 当前任务：`BatchRenamer_PS00_Codex_Execution_Prompt_20260830.md`。

## 当前状态

- 项目已整理到工作区根目录；`回收站`、`bin/obj`、`artifacts` 不进入 Git。
- 已增加 `Release-Internal` / `Release-Public` 配置。
- Internal QA 已迁入 `src/BatchRenamer.App/InternalTools`。
- Public 通过 MSBuild 条件排除整个 InternalTools 边界。
- Transaction Core 文件未修改。
- `Release-Internal` / `Release-Public` 严格构建：均 0 警告、0 错误。
- 完整 SmokeTests：PASS。
- Internal 2,000 个真实文件 Execute / Startup Gate / Undo / Idempotence：PASS，沙箱已清理。
- Internal / Public 单文件发布：PASS；产物身份和版本元数据已分离。
- `PUBLIC_BUILD_PURITY = PASS`：Public 编译项和 publish 目录均不含 InternalTools。
- `main` 已固定在 PS-00 PASS commit `33a7bcd`；当前分支为 `productization/ps01-internal-qa-center`。
- `Shift+Ctrl+P` 已改为打开 Internal-only QA Center；原 `Shift+Ctrl+D/T` 保留。
- Quick Smoke、Demo Data、20k Preview、事务准备检查、2k 命令/结构化结果均已收编。
- QA Workspace 固定在 `%TEMP%\BatchRenamer\InternalQA`，Cleanup 校验固定路径、ownership marker 与 reparse point。
- PS-01 完整 SmokeTests、2,000 文件真实压力测试、Public publish/purity：PASS；Temp 残留 0。

## 固定决策

- 默认 Build Flavor 为 Public，避免误发内部能力。
- Internal 窗口标题必须显示 `INTERNAL TEST`。
- 所有 QA 继续调用正式 Preview / Planner / Transaction 链，禁止第二套 mutation 路径。
- Git 提交、分支和 PR 文案使用中文；不直接推送 `main`；不自动发布正式 Release。

## 下一步

PS-01 后续仅处理评审反馈；主路线进入 PS-02 Public Build Purity Gate。只维护最新状态和关键决策，不追加流水日志。
