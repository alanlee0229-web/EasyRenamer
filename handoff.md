# EasyRenamer 接管摘要

## 当前目标

PS-03 Release Identity / Branding 已完成本地实现与验收，当前分支为 `productization/ps03-release-identity`，待中文提交、推送并创建 PR。

## 权威信息

- 基线：`main` 提交 `51b7b56`，已包含合并后的 PS-02。
- 外部版本：easy重命名 / BatchRenamer v1.0.0。
- 远程仓库：https://github.com/alanlee0229-web/EasyRenamer.git
- 正式约束：`BatchRenamer_V1_Authoritative_Freeze_20260830_DualFlavor_Update.md`。
- 当前任务：PS-03 Release Identity / Branding。

## 当前状态

- Public：窗口 `easy重命名`；Product/FileDescription `easy重命名 / BatchRenamer`；ProductVersion `1.0.0`；FileVersion `1.0.0.0`。
- Internal：窗口 `easy重命名 — INTERNAL TEST`；Product/FileDescription `BatchRenamer Internal Test`；ProductVersion `1.0.0-internal`。
- `BatchRenamer.App.csproj` 是版本与 Windows 文件元数据权威源；现有 inspector 已扩展为同时验证 Public/Internal。
- Canonical Gate 自动验证双版本元数据、Public 隔离、Internal QA 类型/快捷键路由和 Negative Control，结果 PASS。
- 结构化报告：`artifacts/gates/public_build_purity.json`，由 `.gitignore` 排除。
- Release-Internal / Release-Public 严格构建：均 0 警告、0 错误。
- 完整 SmokeTests：510 条 PASS，Skip 0。
- Public 单文件发布与自动 Gate：PASS；Internal QA Center 与 `Shift+Ctrl+P` 路由自动验证为存在。
- Public EXE SHA256：`B50A719F1CC5F34628F3A4292D2E61B0EA24CB678A8179FE8B97A0403E8800FC`；ZIP：`E8A28935F3B80BD5C34A80EF63E0243BC8EBB682E659A0BA1DC164C4D4CA7FD5`，仅为 PS-03 qualification。
- 仓库没有用户批准的正式 `.ico`；`ICON_ASSET_STATUS = PENDING`，接入边界见 `docs/PS03_RELEASE_IDENTITY.md`。
- Transaction Core 文件变更 0；Transaction 语义变更 0；第二 mutation 路径 0。
- EXE、ZIP、`bin/obj`、`artifacts` 均不提交。

## 固定决策

- 默认 Build Flavor 为 Public；未知口味或检查异常必须失败。
- 未经用户批准不得生成或接入最终 Logo/Icon；未来统一使用 `Assets/BatchRenamer.ico`。
- Internal QA 继续调用正式 Preview / Planner / Transaction 链，禁止第二套 mutation 路径。
- Git 提交、分支和 PR 文案使用中文；不直接推送 `main`；不自动合并 PS-02；不创建正式 Release。

## 下一步

完成 PS-03 PR 后进入 PS-04 GitHub Presentation / Community Foundation。本文只保留最新状态与关键决策。
