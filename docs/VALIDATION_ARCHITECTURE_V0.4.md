# V0.4 Validation Architecture

## 1. 目标

把“预览看起来像对的”升级为“候选名称经过 Rev.A 结构化只读校验”。

ValidationEngine 允许读取文件系统，但没有任何写 API。

## 2. Pipeline

```text
UI mutable state
    ↓ snapshot
PreviewEngine
    ↓ proposed names
ValidationEngine
    ├─ filename legality
    ├─ duplicate targets
    ├─ VacatingSourceSet / TARGET_EXISTS
    ├─ source existence
    ├─ FileIdentity guard
    ├─ parent-child restriction
    └─ path semantics
    ↓
PreviewRowState
    ↓
DataGrid status / tooltip / issue filter
```

## 3. 身份合同

### NamespaceIdentity

用于：

- 导入路径去重；
- 目标路径判等；
- 批次冲突；
- VacatingSourceSet。

### FileIdentity

用于：

- 判断“预览后源路径是否被另一个对象替换”。

不得用于导入去重。因此两个 Hard Link 即使共享同一个 FileIdentity，只要路径不同仍是两个 RenameItem。

## 4. Path Semantics

`WindowsPathSemanticsProvider` 尝试通过 `FileCaseSensitiveInfo` 查询目录级大小写敏感标志。

查询失败不会悄悄伪装成可靠结果，而会：

- 返回保守 fallback；
- `IsReliable=false`；
- 对真实对象产生 `FILESYSTEM_SEMANTICS_UNKNOWN` Warning。

V0.4 不因此执行任何写操作。

## 5. TARGET_EXISTS

不能使用：

```text
File.Exists(Target) => Error
```

而是：

```text
Target occupied
  ├─ self case-only occupancy        → allow
  ├─ occupant ∈ VacatingSourceSet    → allow
  └─ otherwise                       → TARGET_EXISTS
```

因此 `A↔B` 与三向循环不会被误杀；未勾选的 B 则不会“假装会腾空”。

## 6. 性能

重复目标检测使用按 PathSemantics 构建的 Namespace Key，目标是 O(n)，避免 20,000 项 pairwise O(n²)。

真实文件系统存在性 / FileIdentity 检查在 worker thread 执行，并继承 Preview generation cancellation，不阻塞 WPF UI thread。

网络盘 / NAS 的实际 I/O 延迟仍可能明显高于本地 SSD；后续会根据实测决定是否增加目录快照缓存和 Validation tier。
