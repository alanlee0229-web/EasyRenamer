# BatchRenamer Transaction Foundation V0.6.0

## 1. 分层

```text
BatchRenamer.App
    ↓
BatchRenamer.Core
    Preview / Validation / RenamePlanner
    ↓
Frozen RenamePlan Schema V1
    ↓
BatchRenamer.Transaction
    RenamePlanIntegrity
    RenamePlanPersistence
    TransactionPreflight
    ↓
BatchRenamer.FileSystem（由 App 注入只读 Provider）
```

`BatchRenamer.Transaction` 不依赖 WPF，不接收 `RenameRuleSet`，不重新生成目标名称。

## 2. V0.6-A Persistence

事务目录：

```text
%LOCALAPPDATA%\BatchRenamer\transactions\<transaction-id-N>\
    plan.json
```

`plan.json` 是不可变执行计划。若同一 TransactionId 目录已经存在，拒绝覆盖。

写入流程：

```text
RenamePlanIntegrity
    ↓
serialize UTF-8
    ↓
SHA256(memory bytes)
    ↓
.plan.json.tmp-<nonce>  CreateNew + WriteThrough
    ↓
Flush(true)
    ↓
atomic File.Move(staging, plan.json, overwrite:false)
    ↓
Load(plan.json)
    ↓
RenamePlanIntegrity again
    ↓
TransactionId == directory name
    ↓
SHA256(read-back) == SHA256(memory bytes)
```

此处 `File.Move` 仅用于 BatchRenamer 自己的事务元数据提交，绝不接收 `RenamePlanEntry` 的 Source/Temp/Target。

## 3. Plan Integrity

校验的对象是 Frozen Plan 自身，不解释命名规则。

关键 invariant：

- SchemaVersion = 1；
- TransactionId 非空；
- Entries 非空；
- DirectorySemantics 非空；
- Ordinal 从 0 连续且唯一；
- ItemId 唯一；
- Source / Temp / Target 均非空；
- V1 三者必须处于同目录；
- Source 与 Target 不允许完全相同；
- Temp 必须位于 `.~br-` 保留 namespace；
- Sources 唯一；
- Temps 唯一；
- Targets 唯一；
- Temp 不得与任意 Source / Target 冲突；
- A↔B / A→B→C→A 的 Source/Target 交叉合法。

## 4. V0.6-B Preflight

Preflight 是未来 TransactionEngine 在 Phase 1 前必须调用的最后只读 Gate。

它使用：

```text
Frozen RenamePlan
+
当前 IReadOnlyFileSystem
+
当前 IPathSemanticsProvider
+
当前 IFileIdentityProvider
```

重新检查：

- Source existence / kind；
- 已冻结 FileIdentity；
- Temp occupancy；
- Target occupancy；
- VacatingSourceSet；
- PathSemantics；
- 当前 path/component length。

### FileIdentity

若 Plan 中曾成功冻结 FileIdentity，但 Preflight 时无法再确认：

```text
SOURCE_IDENTITY_UNVERIFIABLE → Error
```

如果 Plan 本身没有 FileIdentity（常见于部分网络/远程环境），不伪装 Strong Recovery：

```text
SOURCE_IDENTITY_BEST_EFFORT → Warning
```

### PathSemantics

以下属于关键变化并阻止执行：

- CaseSensitive 标志变化；
- 计划生成时语义可靠，但执行前无法可靠确认；
- 当前文件名 / 路径长度限制已经不足以容纳冻结路径。

对持续无法可靠确认的网络/特殊文件系统明确标记 Best-effort Warning。

## 5. 当前 mutation boundary

V0.6.0 生产代码中允许的写操作只限事务元数据：

```text
File.Move(stagingPath, planPath)     // plan.json 原子提交
File.Delete(stagingPath)             // 失败清理
Directory.Delete(transactionDirectory) // 仅清理空的失败事务目录
```

明确不存在：

```text
File.Move(entry.SourcePath, ...)
Directory.Move(entry.SourcePath, ...)
Source → Temp
Temp → Target
```

因此 V0.6.0 仍然不是实际重命名版本。

## 6. 下一阶段边界

V0.6.0 Windows Gate 通过后，下一步只进入：

```text
V0.6-C — Phase 1 Source → Temp
```

V0.6-C 必须继续使用持久化后重新读取的 Frozen Plan，并在真实 mutation 前通过 TransactionPreflight。
