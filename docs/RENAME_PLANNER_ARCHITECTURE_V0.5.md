# BatchRenamer V0.5 RenamePlanner Architecture

## 1. 阶段目标

V0.5 只建立 **Preview / Validation → Frozen RenamePlan** 的最后安全边界。

本阶段明确不做：

- `File.Move` / `Directory.Move`；
- 两阶段真实重命名；
- `plan.json` 持久化；
- `events.jsonl`；
- Rollback / Undo / Crash Recovery。

因此 V0.5 仍然不会修改任何真实文件。

---

## 2. 核心边界

```text
Name Generation
PreviewEngine
    ↓
Concrete ProposedName
    ↓
ValidationEngine
    ↓
RenamePlanner  ← 执行前再次读取真实文件系统
    ↓
Immutable RenamePlan
    ↓
[ V0.6 TransactionEngine ]
```

`RenamePlanner` **不接收 `RenameRuleSet`**，也不读取任何 WPF 控件。

它只关心已经生成好的事实：

```text
SourcePath
ProposedName
IsIncluded
IsDirectory
ExpectedFileIdentity
```

这保证以后增加 Regex、Template、EXIF、AI 命名等 Name Generation 能力时，不需要重写 Transaction 安全链。

---

## 3. RenamePlan V1 Schema

```text
RenamePlan
├─ TransactionId
├─ CreatedAt
├─ SchemaVersion = 1
├─ DirectorySemantics[]
└─ Entries[]
```

每个 `RenamePlanEntry` 冻结：

```text
Ordinal
ItemId
SourcePath
TemporaryPath
TargetPath
IsDirectory
ExpectedFileIdentity (nullable)
```

TransactionEngine 后续不得重新解释命名规则。

---

## 4. Final Revalidation

Planner 自己拥有执行前最终 Validation，不能信任 UI 上几秒前显示的 Validation 结果。

生成 Plan 前重新检查：

- Source 是否仍存在；
- 文件 / 文件夹类型是否改变；
- 能取得 FileIdentity 时，是否仍为导入时同一对象；
- DuplicateTarget；
- VacatingSourceSet；
- TARGET_EXISTS；
- 父子路径限制；
- 文件系统大小写语义；
- V1 扩展名锁定。

若存在任何 Error：

```text
Plan = null
```

---

## 5. FileIdentity / PathSemantics 快照

最终 Validation 和 RenamePlan 必须使用同一批文件系统事实。

因此 Planner 内部使用 Snapshotting Provider：

```text
Validation 第一次读取 FileIdentity / PathSemantics
            ↓
          缓存
            ↓
RenamePlan 复用同一快照
```

避免出现：

```text
Validation 检查的是对象 A
↓
对象被外部替换
↓
Plan 却把对象 B 的 FileIdentity 当成合法身份冻结
```

V0.6 TransactionEngine 在真正执行前仍需再次做 preflight，因为外部文件系统随时可能变化。

---

## 6. 临时名称

V0.5 已由 Planner 为每个执行项冻结唯一 TempPath：

```text
.~br-{transaction-guid}-{ordinal}-{random}
```

要求：

- 与 Source 不同；
- 与 Target 不同；
- 批次内部不重复；
- 当前文件系统不存在；
- 使用安全字符；
- 尽量满足当前目录 component-length 限制。

普通 UI 永远不展示 TempPath。

---

## 7. 哪些项目进入 Plan

进入：

```text
IsIncluded = true
AND 最终 Validation 无 Error
AND CurrentName != ProposedName
AND 非 Synthetic
AND 未修改扩展名
```

不进入：

- 未勾选项目；
- 无变化项目；
- 演示 / 20,000 压力测试 Synthetic 数据；
- 任意 Validation Error 项；
- V1 扩展名变化项。

如果最终没有任何 Changed Entry，返回 `NO_CHANGES`，不创建空 Transaction。

---

## 8. UI 状态失效合同

V0.5 在 ViewModel 中维护 `PreparedPlan`，但只存在内存。

以下任何变化立即废弃 PreparedPlan：

- 改命名参数；
- 勾选状态变化；
- 导入 / 删除；
- 排序；
- 拖动；
- 上移 / 下移。

如果 Planner 计算期间 UI 输入发生变化，结果返回：

```text
INPUT_CHANGED_DURING_PLANNING
```

旧计划不得进入下一阶段。

---

## 9. V0.5 开发诊断入口

为了不改变已经冻结的正式 UI，V0.5 使用隐藏测试入口：

```text
Ctrl + Shift + P
```

仅执行：

```text
最新规则快照
→ Preview
→ Final Validation
→ RenamePlan in memory
→ 显示计划项数量 / Transaction ID
```

不会写磁盘，不会改名。

正式版本不会把这个开发入口作为用户功能。
