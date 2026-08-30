# Contributing

感谢你帮助改进 easy重命名 / BatchRenamer。开始前请先确认改动风险等级，并只修改完成目标所需的最小范围。

## Change risk

| Level | 范围 | 最低验证 |
| --- | --- | --- |
| Level 0 | Docs / Repository | 文档链接、YAML、措辞检查 |
| Level 1 | UI / non-mutation | 严格构建、相关 UI 回归 |
| Level 2 | Rules / Preview / Planner | 严格构建、完整 SmokeTests、边界案例 |
| Level 3 | Transaction / Recovery / Undo | 完整安全设计审查、严格构建、全量测试、故障/恢复证据 |

Level 3 代码不得顺手重构。任何语义变化都必须解释 Frozen Plan、Journal、Rollback、Recovery、Undo 与 FileIdentity 的影响，并证明没有第二条 mutation 路径。

## Pull Request 必填信息

每个 PR 至少说明：

- **What changed**：改了什么。
- **Why**：为什么需要。
- **Tests**：执行了哪些验证及结果。
- **Risks**：风险和失败边界。
- **Behavior intentionally unchanged**：刻意保持不变的行为。

## 基本流程

1. 从最新 `main` 创建独立分支。
2. 保持提交聚焦，不格式化或重构无关文件。
3. 按风险等级执行测试。
4. 文档或社区变更运行：

```powershell
D:\DATA\tpredict\python.exe tools\validate_repository_docs.py
```

5. 生产构建至少使用 warnings-as-errors。

提交 Issue、日志或截图时遵守 [Support 隐私原则](SUPPORT.md#隐私原则)，不要包含私人文件、敏感文件名或个人路径。
