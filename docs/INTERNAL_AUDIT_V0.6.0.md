# BatchRenamer V0.6.0 Internal Audit

## 当前环境

当前交付环境不是 Windows，且没有安装 `dotnet` CLI，因此本地不能声称：

- WPF / .NET 10 已编译；
- 新增 C# Transaction 工程已由真实 Roslyn 编译器验证；
- Windows FileIdentity / PathSemantics 已实机运行。

这些必须由 Windows Gate 最终确认。

## 已完成内部检查

### IMPLEMENTED

- V0.5 Final Validation 状态栏计数同步修复；
- 已知 ExpectedFileIdentity 读取失败时禁止静默降级；
- `BatchRenamer.Transaction` 独立工程；
- RenamePlan structural integrity；
- plan.json staging + flush-to-disk + atomic metadata commit；
- read-back；
- SHA256 写前/写后比对；
- TransactionId 与事务目录绑定；
- Transaction Preflight；
- SmokeTests 源码新增 V0.6-A/B 测试场景；
- V0.6 Windows Acceptance 文档。

### STATIC_CHECKED

已执行：

- 所有 `.csproj` XML 解析；
- ProjectReference 文件存在性检查；
- `.sln` 新工程引用检查；
- 所有本次修改 C# 文件 delimiter / lexical balance 检查；
- Transaction 层禁止 `System.Windows` / `BatchRenamer.App` / `RenameRuleSet` 依赖扫描；
- 生产代码 mutation API 扫描。

生产代码 mutation 扫描结果：

```text
File.Move      = 1  （仅 staging plan.json → plan.json）
Directory.Move = 0
File.Replace   = 0
File.Delete    = 1  （仅 staging metadata cleanup）
Directory.Delete = 1（仅空失败 transaction directory cleanup）
File.WriteAll* = 0
File.AppendAll*= 0
```

另外确认不存在：

```text
File.Move(entry....)
Directory.Move(entry....)
File.Move(plan....)
Directory.Move(plan....)
```

### NOT_WINDOWS_VALIDATED

以下等待用户一次性 Windows Gate：

- VS2026 Solution build；
- V0.6 扩展 SmokeTests；
- LocalAppData plan.json 实际落盘；
- read-back + Preflight；
- `plan.json` Schema 人工抽检；
- 测试目录用户文件绝不改名；
- V0.5 状态栏 Bug Windows 回归。

## Gate 原则

V0.6-C 是第一次真实修改 namespace。上述 Windows Gate 未通过前，不实现/启用 Phase 1。
