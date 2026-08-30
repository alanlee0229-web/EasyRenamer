# Internal Audit — V0.10.0 Release Candidate Foundation

## 主线变化

1. 新增 `TransactionRetentionService`：
   - V1 默认保留最近约 20 个 Completed/Undone 事务目录；
   - 放弃的 Prepared plan 默认保留 2 天后可清理；
   - Interrupted / ExternallyModified / SessionBusy / ManualRequired 永不自动清理；
   - 最新 UI Undo 候选永不因 retention 被清理；
   - 删除前重新取得 transaction session lease 并重新做 Recovery Analysis；
   - 仅允许删除 BatchRenamer 已知元数据文件；发现未知文件/子目录即 fail-safe 保留；
   - 不调用 `IRenameMutationFileSystem`，不接收 Source/Temp/Target mutation API。

2. 新增 win-x64 Portable 发布配置：
   - Self-contained；
   - Single-file；
   - `PublishTrimmed=false`；
   - x64；
   - Release；
   - Python `tools/publish_portable.py` 负责确定性输出、ZIP 与 SHA256。

3. 事务弹窗视觉顺手统一：
   - 普通 Execute / Undo / Startup Recovery 用户提示改为应用内 `AppDialog`；
   - 主界面 IA、规则区和冻结布局不变；
   - 隐藏开发诊断 MessageBox 不属于普通产品路径，暂不改动。

## 保持不变

- RenamePlanner Schema V1；
- Phase1 / Phase2 / Rollback executors；
- Journal durable mutation protocol；
- Recovery Analyzer / Orchestrator；
- Startup Recovery Coordinator；
- Durable Undo core；
- V1 高级能力范围冻结。

## 当前环境限制

当前非 Windows 环境没有 .NET 10 SDK，无法声称：

- WPF compile validated；
- V0.10 retention Windows runtime validated；
- Single-file publish validated；
- Portable EXE runtime validated。

以上只在 `docs/WINDOWS_ACCEPTANCE_V0.10.0.md` Gate 通过后升级为 WINDOWS_VALIDATED。
