# Security Policy

## 私有报告入口

可能造成任意文件覆盖、错误文件 mutation、路径逃逸或安全边界绕过的问题，请使用 GitHub 的 [Private vulnerability reporting](https://github.com/alanlee0229-web/EasyRenamer/security/advisories/new)。不要先创建公开 Issue，也不要发布可利用细节。

如果仓库尚未启用私有漏洞报告，请仅向仓库所有者说明需要建立私密沟通渠道，不要把敏感复现公开化。

## 安全范围

优先私下报告以下问题：

- 文件被覆盖、删除，或 mutation 作用于错误对象。
- Validation 或 RenamePlanner 可以被绕过。
- Source、Temp、Target 路径存在 path traversal 或越界访问。
- Frozen RenamePlan、Journal 或事务证据可被不安全替换或重放。
- Recovery / Undo 在身份或占用状态不明确时仍覆盖文件。
- Public artifact 意外包含 Internal-only 能力或测试身份。

永久安全边界是：

```text
Validation
    ↓
RenamePlanner
    ↓
Frozen RenamePlan
    ↓
Transaction
```

未来的 CLI、自动化、插件或智能能力都不得绕过这条链。

## 安全报告内容

请提供受影响版本、Windows 版本、Build Flavor、预期/实际行为，以及使用中性文件名重写的最小复现。不要上传私人文件，不要公开敏感文件名或路径。若必须提供日志，请先彻底脱敏。

普通功能 Bug、兼容性问题或无法证明为安全漏洞的 Recovery 状态，请使用 [Support](SUPPORT.md) 指定的 Issue Form。
