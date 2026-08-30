# V1.0 Rev.A 修订说明

修订日期：2026-08-26

Rev.A 不改变产品核心定位，主要完成“冻结前最后一次安全与范围收敛”。

## P0 已修复

1. **TARGET_EXISTS 与交换/循环 Rename 的矛盾**  
   引入 `VacatingSourceSet`。目标被本次会在 Phase 1 腾空的 Source 占用时允许执行；只有外部或不会让出的占用才报错。

2. **File ID 被错误用于导入去重**  
   拆成 `NamespaceIdentity` 与 `FileIdentity`。Hard Link 的不同目录项保留为独立 RenameItem。

3. **Crash Recovery 只有概念、没有闭环状态机**  
   Recovery 改为读取 `plan.json + events.jsonl + 当前文件系统`，按 Source/Temp/Target 事实分类，支持幂等恢复。

4. **50,000 项 Journal 写放大风险**  
   不再每项重写整个 Journal。Plan 只完整安全写入一次，进度采用 append-only JSONL，Phase 边界强制 Flush。

5. **多编号规则语义不清**  
   V1.0 收敛为单一 `SequenceConfig`；多序列与分组编号下放 V1.1。

6. **普通/高级 UI 可能形成双状态源**  
   冻结单一 `RenameRuleSet`。为 V1.1 预留 `SimpleCompatible`，但 V1.0 不开放任意高级 Rule Chain。

7. **Windows 路径大小写语义被过度简化**  
   增加 `IPathSemanticsProvider`，支持目录级 Case Sensitivity 与网络文件系统差异。

## UI 高价值修订

- 表头全选/全不选；
- 跨目录导入默认可辨认父目录/完整路径；
- “只看问题”快速定位 Error/Warning；
- 新名称作为主要视觉信息，无变化项弱化。

## V1.0 范围收敛

下放 V1.1：
- Regex；
- 复杂 Template；
- 任意 Rule Chain；
- 复杂 Scope；
- 多套/分组编号；
- 修改扩展名；
- 日期命名变量。

V1.0 集中完成：
> 排序 → 人工微调 → 单一连续编号 → 实时预览 → 全量校验 → 两阶段安全 Rename → 回滚/恢复/撤销。
