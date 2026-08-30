# BatchRenamer Advanced Capability Roadmap — Frozen

## 状态

本文件冻结的是 **未来高级能力路线**，不是 V1.0 当前实现范围。

执行原则：

> 先完成一个安全、完整、可放桌面长期使用的 V1.0，再按真实使用反馈迭代高级能力。

V1.0 不因为未来能力继续扰动已通过的主 UI。

---

## V1.0 — 当前主线

- 基础命名；
- 原名称组合；
- 前缀 / 后缀；
- 单套连续编号；
- 查找替换（可选模块）；
- 大小写转换（可选模块）；
- Natural Sort / 人工拖动；
- 实时 Preview；
- Validation；
- RenamePlanner；
- Two-phase Transaction；
- Journal / Rollback / Undo / Recovery；
- Portable EXE。

---

## V1.1 — 第一批高价值高级能力

优先级较高：

1. Regex 正则查找 / 捕获 / 替换；
2. Template Engine；
3. `{日期}` / `{创建日期}` / `{修改日期}`；
4. `{父文件夹}`；
5. Rule Scope：全部 / 文件 / 文件夹 / 扩展名 / 目录；
6. 分组编号：按扩展名 / Parent Directory / Item Type；
7. 文件夹展开与递归；
8. 修改扩展名（独立风险开关）。

UI 原则仍然是：

```text
设置勾选某能力
        ↓
主工作区才显示对应规则模块
```

未启用能力不占主界面空间。

---

## V1.2 — 专业规则系统

- 自定义 Rule Chain；
- 规则开启 / 关闭；
- 调整执行顺序；
- 每一步中间结果 Preview；
- 多 Sequence Definition；
- 更复杂的条件 Scope。

此阶段才考虑从固定 `RenameRuleSet` 进一步升级为真正的 `RenamePipeline`。

---

## V2 — 重型 / 专业生态能力

- EXIF 元数据；
- ID3 音频元数据；
- 视频元数据；
- PDF Metadata；
- 字母编号 / 罗马数字 / 自定义编号组；
- 单项锁号；
- Plugin / Extension API。

---

## 当前必须保留的架构边界

未来所有高级功能都只负责：

```text
Item Context
    ↓
Name Generation
    ↓
Concrete ProposedName
```

后面的安全链保持稳定：

```text
Concrete Source → Target
        ↓
Validation
        ↓
RenamePlanner
        ↓
Transaction
        ↓
Journal / Recovery
```

因此 V1.0 **不提前实现** Regex/Template/Metadata，也**不为了未来功能过度设计事务层**。
