# 批量自定义重命名工具 V1.0
## 冻结版程序设计方案

**文档状态：已冻结（Rev.A）**  
**修订日期：2026-08-26**  
**目标平台：Windows 10 / Windows 11 x64**  
**最终交付：免安装、Self-contained 单文件 `.exe`**  
**冻结原则：后续编码以本文件作为唯一需求与设计基准。**  
**Rev.A 原则：收紧 V1.0 功能面，同时补齐冲突、身份、事务恢复与文件系统语义合同。**

---

# 1. 项目定位

开发一个 Windows 本地批量重命名工具。

核心流程：

```text
导入文件 / 文件夹
        ↓
自动排序
        ↓
用户拖动微调
        ↓
确定最终顺序
        ↓
配置重命名规则
        ↓
实时生成新名称
        ↓
完整冲突与合法性检查
        ↓
安全执行重命名
        ↓
支持失败恢复及撤销
```

核心产品价值：

> 用户当前看到的列表顺序，就是后续顺序编号的依据。

程序优先服务：
- 同类型文件批量改名
- 文件夹批量改名
- 顺序连续编号
- 排序后人工调整
- 原名保留 / 新名称替换
- 批量文字清理

高级能力作为增量功能存在，不干扰普通用户。

---

# 2. 产品基本原则

## 2.1 顺序即编号依据
任何涉及序号的规则，都基于当前最终列表顺序，而不是最初文件顺序、文件系统枚举顺序、新名称排序结果或隐藏优先级。

```text
界面顺序
=
预览编号顺序
=
最终重命名编号顺序
```

## 2.2 扩展名默认受保护

常规模式只处理文件主名称，不修改扩展名。

例如：
```text
IMG_001.JPG
```

内部拆分：
```text
主名称：IMG_001
扩展名：JPG
```

V1.0 的所有可执行规则只修改 `IMG_001`，最终自动拼回 `.JPG`。

**V1.0 不开放修改扩展名。** 修改扩展名属于 V1.1 的高风险高级能力；底层数据模型和验证接口可以预留，但 V1.0 UI、Preset 与执行计划不得生成扩展名修改操作。

---

## 2.3 预览即执行计划
用户预览看到：
```text
A.jpg → Photo_001.jpg
```

实际执行目标必须也是：
```text
Photo_001.jpg
```

执行阶段禁止偷偷：
- 自动追加 `(1)`
- 自动修改序号
- 自动改变名称
- 自动覆盖
- 自动跳过冲突

## 2.4 安全优先于强行完成
遇到无法安全判断或执行的情况：
```text
停止
↓
回滚
↓
报告问题
```

---

# 3. V1 核心使用场景

## 3.1 同类文件连续编号
```text
IMG_8362.jpg
IMG_8365.jpg
IMG_8370.jpg
```

排序并调整后：
```text
旅行_001.jpg
旅行_002.jpg
旅行_003.jpg
```

## 3.2 文件夹连续改名
```text
新建文件夹
新建文件夹 (2)
新建文件夹 (3)
```

变为：
```text
项目_01
项目_02
项目_03
```

## 3.3 保留原名称
```text
北京.jpg
上海.jpg
广州.jpg
```

可生成：
```text
001_北京.jpg
002_上海.jpg
003_广州.jpg
```

或：
```text
旅行_北京_001.jpg
旅行_上海_002.jpg
旅行_广州_003.jpg
```

## 3.4 清理原名称后重新编号
```text
IMG_001_副本.jpg
IMG_002_副本.jpg
IMG_003_副本.jpg
```

规则：
```text
删除 IMG_
删除 _副本
新增名称：照片
增加序号
```

得到：
```text
照片_001.jpg
照片_002.jpg
照片_003.jpg
```

---

# 4. 导入系统

## 4.1 基本行为
```text
拖入文件
→ 文件本身成为改名对象

拖入文件夹
→ 文件夹本身成为改名对象
```

V1 不因为拖入文件夹就自动读取其内部文件。

## 4.2 支持方式
V1 支持：
- 单个文件拖入
- 多个文件拖入
- 单个文件夹拖入
- 多个文件夹拖入
- 文件与文件夹混合拖入
- “添加文件”按钮
- “添加文件夹”按钮

## 4.3 文件夹内部处理
V1 不实现：
- 自动展开文件夹
- 递归遍历
- 子文件批量加入
- 父目录变量
- 父子目录关联重命名

底层数据模型为上述能力预留扩展空间。

## 4.4 重复对象

导入去重判断的是“同一个可重命名目录项（namespace entry）”，不是“底层是否指向同一个物理文件对象”。

V1.0 明确区分两种身份：

```text
NamespaceIdentity
= 用于导入去重、路径冲突、RenamePlan 的目录项身份
```

```text
FileIdentity
= 用于执行前复核、TOCTOU 防护、Recovery 时确认对象是否被外部替换
```

### NamespaceIdentity
优先由以下信息构成：
```text
规范化完整路径
+
所在目录的实际大小写语义
```

要求：
- 不通过解析 Symbolic Link / Junction 目标来合并路径；
- 不因为两个路径拥有相同 File ID 就把它们合并；
- 同一目录项以不同文本形式重复导入时应识别为重复；
- 路径比较必须通过 `IPathSemanticsProvider`，不得全局写死 `OrdinalIgnoreCase`。

### FileIdentity
能可靠获取时记录：
```text
Volume ID + File ID
```

无法可靠获取时允许为 Unknown，并在执行前采用更保守的路径/属性复核策略。

### Hard Link 明确规则
如果：
```text
A.txt
B.txt
```
是同一个 NTFS 文件对象的两个 Hard Link，它们仍然是两个独立的可重命名目录项，因此允许同时加入列表并分别改名。

**结论：Path/Namespace Identity 用于去重；File Identity 用于防止对象被悄悄替换。**

不使用文件内容 Hash。

---

## 4.5 内容相同不等于重复对象
不同路径下内容完全相同的两个文件仍然是两个独立重命名对象。Hash 重复检测属于未来独立功能。

---

# 5. 排序系统

排序系统只决定谁在前、谁在后，不负责编号分组，也不负责生成名称。

## 5.1 V1 内置排序
支持：
- 原始导入顺序
- 名称
- 扩展名
- 创建时间
- 修改时间
- 文件大小

方向：
- 升序
- 降序

对象排列：
- 文件和文件夹混合
- 文件夹优先
- 文件优先

## 5.2 名称自然排序
采用 Natural Sort。

例如：
```text
1.jpg
2.jpg
10.jpg
20.jpg
100.jpg
```

而不是：
```text
1.jpg
10.jpg
100.jpg
2.jpg
20.jpg
```

同时支持：
```text
S1E2
S1E10
S2E1
S10E1
```

## 5.3 大小写
排序比较可忽略英文字母大小写，但排序逻辑与文件系统名称冲突判断是两套独立逻辑。

## 5.4 中文
V1 不实现中文拼音排序，采用稳定、可预测的系统 / Unicode 比较逻辑。

## 5.5 扩展名排序
同扩展名内部继续使用名称自然排序。

扩展名比较忽略大小写：
```text
.JPG
.jpg
.Jpg
```

视为同一扩展名。

## 5.6 文件夹大小
V1 不计算文件夹实际容量。文件夹大小值视为无值。

按大小排序且采用“混合”模式时：
> 无值项目固定放在有值项目之后。

文件夹之间保持稳定自然排序。

---

# 6. 手动顺序调整

## 6.1 行拖动
支持：
- 单项拖动
- 多选批量拖动

拖动后：
```text
SortMode = Custom
```

界面显示：
```text
排序：自定义
```

## 6.2 批量拖动
如果当前拖动项属于多选集合，则拖动整个选择集合，并保持被拖项目之间原有相对顺序。

## 6.3 辅助移动
支持：
- 移到顶部
- 上移
- 下移
- 移到底部

## 6.4 重新自动排序
用户再次主动选择自动排序规则时，新自动排序覆盖之前的人工顺序；之后仍可再次手动调整。

## 6.5 原始导入顺序永久保存
每个项目保存：
```text
OriginalImportIndex
```

不因后续排序、拖动而修改。

---

# 7. 新项目加入时的排序行为

当前处于自动排序状态时，新项目自动进入正确排序位置。

当前处于：
```text
排序：自定义
```

新项目追加到列表末尾，不破坏人工整理结果。

---

# 8. 列表勾选与选择

列表中的“选中状态”与“是否参与改名”是两个概念。

## 8.1 选择
用于：
- 拖动
- 批量移动
- 从列表移除
- 右键操作

支持：
- 单击
- Ctrl + 点击
- Shift + 点击
- Ctrl + A

## 8.2 勾选

决定对象是否参与本次 RenamePlan。

未勾选对象：
- 保留在列表；
- 可参与排序显示；
- 不占 V1.0 连续编号；
- 不执行磁盘修改。

列表表头必须提供批量勾选控件：
```text
☑ 全选参与改名
☐ 全不选
```

允许三态显示“全部 / 部分 / 全不选”。

V1.0 中表头批量勾选默认作用于整个当前列表，不因“只看问题”等显示过滤而改变语义，避免用户误以为隐藏项目没有被修改。

---

# 9. 重命名规则体系

底层只有一个权威配置对象：
```text
RenameRuleSet
```

UI、预览、Preset、RenamePlanner 均从同一个 `RenameRuleSet` 读取，不允许普通界面、高级界面分别保存一份独立规则状态。

V1.0 使用**固定顺序规则管线**，不向用户开放任意 Rule Chain 重排。

普通 UI 是 `RenameRuleSet` 的主要编辑投影；“高级设置”在 V1.0 仅增加少量参数（例如编号起始值、步长），仍然修改同一对象。

为 V1.1 预留：
```text
SimpleRuleProjection
SimpleCompatible
```

如果未来自定义高级 Rule Chain 已无法无损映射回普通控件，则：
```text
SimpleCompatible = false
```
普通区域必须显示“当前使用自定义高级规则”，不得伪造或覆盖高级规则状态。

---

# 10. 常规模式

程序默认主打单一、直接的 V1.0 工作流。

适用于：
- 同类型文件；
- 一批文件夹；
- 文件 / 文件夹混合列表；
- 连续编号；
- 简单文字清理。

V1.0 的“高级设置”不是另一套模式，只开放不改变规则模型的少量高级参数。

以下复杂能力下放 V1.1：
- 任意规则链及规则重排；
- Regex；
- 复杂模板；
- 多套 / 分组编号；
- 修改扩展名；
- 复杂作用范围。

---

# 11. 常规模式初始安全状态

程序首次打开：
```text
名称：空
序号：关闭
额外规则：无
扩展名：锁定
```

因此：
```text
A.jpg → A.jpg
B.jpg → B.jpg
```

默认不产生任何修改。

如果所有项目都无变化，“执行改名”按钮禁用。

---

# 12. 常规名称处理顺序

V1.0 固定为：
```text
当前磁盘主文件名
        ↓
1. 原名称清理
   - 查找替换
   - 删除文字
   - 删除前/后 N 个字符
   - 保留前/后 N 个字符
   - 大小写
        ↓
2. 名称构造
   - 完全使用新名称
   - 新名称 + 当前处理后名称
   - 当前处理后名称 + 新名称
        ↓
3. 附加前缀 / 后缀
        ↓
4. 单一连续序号
        ↓
5. 拼回受保护扩展名
```

该顺序是 V1.0 产品合同，用户不可重排。任意 Rule Chain 作为 V1.1 能力处理。

---

# 13. 常规规则

V1 支持：

## 13.1 基础名称
例如：
```text
旅行
项目
照片
```

## 13.2 原名称组合
支持：
```text
不保留
原名称在前
原名称在后
```

常规 UI 中的“原名称”指前置清理规则执行后的当前名称。

## 13.3 查找替换
普通查找替换不是正则，默认按字面量处理。

## 13.4 前缀 / 后缀
支持普通文本前缀、后缀。

## 13.5 删除指定文字
支持删除指定文本。

## 13.6 字符处理
支持：
- 删除前 N 个字符
- 删除后 N 个字符
- 保留前 N 个字符
- 保留后 N 个字符

Unicode 字符处理按照 Grapheme Cluster / 用户视觉字符，不得拆坏复合 Emoji。

## 13.7 大小写
V1：
- 全部大写
- 全部小写

---

# 14. 编号系统

编号是实时计算结果，不是项目永久属性。

V1.0 一个 `RenameRuleSet` 最多只有**一套活动 SequenceConfig**；不存在多个并行计数器，也不存在多条 SequenceRule 竞争同一个 `{序号}` 的问题。

基本流程：
```text
当前最终列表顺序
        ↓
过滤未勾选项目
        ↓
按视觉顺序连续分配编号
```

因此：
```text
界面顺序 = 预览序号顺序 = 最终序号顺序
```

未勾选对象不占编号。

---

# 15. 常规编号

默认：
```text
起始值 = 1
步长 = 1
作用域 = 当前统一批次
```

用户只需要控制：
- 是否开启
- 位置
- 补零位数
- 分隔符

## 15.1 位数
支持：
```text
1
01
001
0001
```

位数代表最小补零宽度，不截断数字。

## 15.2 位置
支持：
```text
名称前
名称后
```

## 15.3 分隔符
支持：
- 无
- `_`
- `-`
- 空格
- `.`
- 自定义

---

# 16. 高级编号

V1.0 “高级编号”只开放：
- 起始值；
- 支持从 0 开始；
- 步长。

仍然只有一个统一计数器：
```text
scope = ALL_CHECKED_ITEMS
```

V1.0 不开放：
- 文件 / 文件夹分别编号；
- 按扩展名分别编号；
- 多条编号规则；
- 自定义编号组；
- 单项锁号。

这些能力进入 V1.1 后必须通过显式 `SequenceDefinitionId` / Scope 定义，禁止让一个含糊的 `{序号}` 同时指向多套计数器。

---

# 17. 文件扩展名规则

## 17.1 普通文件
扩展名取最后一个点之后的部分：
```text
archive.tar.gz
→ Stem = archive.tar
→ Extension = gz
```

V1.0 不做复杂复合扩展名识别。

## 17.2 文件夹
文件夹没有扩展名保护概念，名称整体作为主名称处理。

## 17.3 点号文件
例如：
```text
.gitignore
```
默认视为无扩展名主名称，避免把整个名称错误识别成扩展名。

## 17.4 V1.0 锁定规则
文件对象的真实扩展名在 V1.0 中只读：
- RuleEngine 不修改；
- Preset 不修改；
- Preview 不生成不同扩展名；
- RenamePlan 不接受不同扩展名。

修改扩展名下放 V1.1，并需要单独风险确认与验证合同。

---

# 18. 高级规则链

**V1.0 不向用户开放任意高级 Rule Chain。**

Domain 层可以保留 `RenameRule` 抽象，但 V1.0 由 `RenameRuleSet` 生成固定顺序规则管线，用户不能：
- 任意新增多条规则；
- 拖动规则改变执行顺序；
- 建立多套 SequenceRule；
- 让模板、Regex 与普通规则形成复杂依赖。

任意 Rule Chain 进入 V1.1，届时必须遵守第 9 节的单一真相源与 `SimpleCompatible` 合同。

---

# 19. 规则作用范围

V1.0 不开放复杂规则作用范围。

所有可执行的常规名称规则与单一 SequenceConfig 默认作用于当前**已勾选对象**。

V1.1 可增加：
```text
全部对象
仅文件
仅文件夹
按扩展名
自定义 Scope
```

增加 Scope 时只影响规则适用性，不得暗中改变列表视觉顺序。

---

# 20. 模板功能

**V1.0 不开放模板编辑器。**

原因：模板与多编号、规则顺序、扩展名修改之间存在语义耦合，首版不为低频能力扩大解释器和验证面。

V1.1 如开放模板，至少预留变量语义：
```text
{原名}
{当前名称}
{序号}
{扩展名}
{日期}
{创建日期}
{修改日期}
```

其中 `{序号}` 必须绑定到明确的 Sequence Definition；禁止在存在多套序列时使用未限定、语义不明确的编号变量。

---

# 21. 正则表达式

**V1.0 不开放 Regex Replace。**

V1.1 再加入，并必须满足：
- 捕获语法错误；
- 设置正则执行超时；
- Regex Timeout 视为结构化规则错误；
- 禁止灾难性回溯长期锁死 UI / Preview Worker。

---

# 22. 日期

V1.0 仍读取文件创建时间、修改时间用于排序和显示，但**不把日期作为重命名模板变量开放给用户**。

日期命名变量进入 V1.1。

V1.0 不解析：
- EXIF；
- 视频 Metadata；
- ID3；
- PDF Metadata。

---

# 23. 修改扩展名

**V1.0 不实现修改扩展名。**

文件扩展名在 V1.0 全程受保护。

V1.1 如实现，必须作为：
```text
高级设置
→ 风险操作
→ 修改扩展名
```
并明确提示：
> 更改扩展名不会转换文件真实格式。

同时增加独立风险确认和扩展名合法性校验。

---

# 24. 实时预览

主列表默认显示：

| 列 | 说明 |
|---|---|
| 勾选 | 是否参与改名；表头支持全选/全不选 |
| 顺序 | 编号依据 |
| 原名称 | 当前实际源名称 |
| 新名称 | 实时预览，作为主视觉信息 |
| 状态 | 正常 / 警告 / 错误 |

跨目录导入时必须能辨认来源，V1.0 至少提供以下两种方式之一，并推荐同时提供：
- 原名称下方以较弱次级文字显示父目录；
- Hover Tooltip 显示完整路径。

可选列可显示：完整路径、大小、创建时间、修改时间。

视觉原则：
- 新名称比原名称更突出；
- “无变化”项目弱化；
- Error 使用明确图标/状态，而不仅依赖颜色；
- 不做复杂逐字符 Diff Editor。

---

# 25. 状态系统

## 正常
可执行。

## 警告
例如名称无变化，不一定阻止执行。

## 错误
例如：
- 名称为空
- 非法字符
- 名称冲突
- 目标已存在
- 源对象消失

存在任意 Error 时，执行按钮禁用。

---

# 26. 无变化项目

```text
A.jpg → A.jpg
```

状态为“无变化”。

执行计划自动跳过。

如果全部项目无变化，执行按钮禁用。

---

# 27. 文件名合法性验证

实时检查：
- 空名称
- Windows 非法字符
- 名称末尾空格
- 名称末尾句点
- Windows 保留名称
- 文件名长度
- 路径长度
- 实际文件系统限制

典型非法字符：
```text
< > : " / \ | ? *
```

---

# 28. 冲突检查

Validation 必须先建立当前候选 `RenamePlan` 的路径集合，再判断冲突，不能简单使用：
```text
File.Exists(Target) => Error
```

## 28.1 批次内部目标冲突
同一 Path Semantics 下，如果两个或更多计划项产生同一个最终目标 NamespaceIdentity：
```text
DUPLICATE_TARGET
```
相关项目同时标记 Error。

## 28.2 目标当前已被占用
定义：
```text
VacatingSourceSet
= 本次计划中确实会进入 Phase 1、因此会让出原路径的 Source NamespaceIdentity 集合
```

目标已存在时分三类：

### A. 目标就是当前项目自身的源目录项
例如仅大小写变化：
```text
photo.jpg → Photo.jpg
```
在大小写不敏感目录中属于同一 Namespace Entry，不视为外部冲突；仍通过两阶段 Rename 安全完成。

### B. 目标被另一个“本次会让出”的 Source 占用
例如：
```text
A.txt → B.txt
B.txt → A.txt
```
或者三向循环。

只要目标占用者属于 `VacatingSourceSet`，且该目标不存在批次内部重复目标，则**允许**进入事务，因为 Phase 1 会先统一腾空全部 Source。

### C. 目标被不会让出的对象占用
包括：
- 未勾选项目；
- 无变化、不会进入 Phase 1 的项目；
- 列表之外的外部文件 / 文件夹；
- 任何不能安全确认会被本事务腾空的对象。

统一：
```text
TARGET_EXISTS
```
阻止执行。

## 28.3 跨目录
冲突判断键不是纯文本文件名，而是：
```text
目标 ParentDirectory 的 PathSemantics
+
目标名称
```

不同目录下同名通常不冲突；每个目录按其自身大小写语义判断。

---

# 29. V1 冲突策略

统一：
> 冲突即阻止执行。

V1 不提供：
- 自动追加 `(1)`
- 自动跳过
- 自动覆盖

---

# 30. 父子路径限制

如果某个待改名文件夹内部还存在任何其他当前列表对象：
> V1 禁止执行该父文件夹改名。

无论内部对象是否勾选、是否准备改名，都阻止。

未来通过 Path Dependency Graph 扩展真正的父子事务能力。

---

# 31. 执行前最终校验

用户点击执行后，必须重新读取文件系统并验证：
- Source NamespaceIdentity 是否仍存在；
- 能获取 FileIdentity 时，是否仍与预览/Plan 记录一致；
- 当前目录 PathSemantics 是否发生影响判断的变化；
- 当前目标占用关系是否仍满足第 28 节；
- 是否新增外部冲突；
- 当前目录是否可访问；
- 是否存在父子路径问题；
- 文件扩展名是否仍与 V1.0 锁定合同一致。

任何状态变化都取消本次执行、废弃尚未开始的 Plan，并刷新 Preview / Error。

**FileIdentity 只用于确认“对象有没有被替换”，不得替代 NamespaceIdentity 做导入去重或目标路径判等。**

---

# 32. RenamePlan

通过全部验证后生成不可变的：
```text
RenamePlan
```

执行线程不得继续读取 UI 输入框或实时规则状态。

---

# 33. 安全执行模型

V1 所有实际 Rename 默认采用两阶段重命名。

## 33.1 Phase 1
```text
原始名称
↓
唯一临时名称
```

## 33.2 Phase 2
```text
临时名称
↓
最终名称
```

## 33.3 解决的问题
统一支持：
- A ↔ B
- A → B → C → A
- 仅大小写变化 Rename

无变化项目不进入事务。

---

# 34. 临时名称

临时名称必须：
- 使用安全字符
- 长度合理
- 在当前目录唯一
- 使用高随机性 / GUID 类标识
- 执行前确认不存在

普通 UI 不显示。

---

# 35. Rename Journal

V1.0 Journal 采用“**不可变事务计划 + 追加式进度事件 + 文件系统事实重建**”模型，避免每个文件都重写整个巨大 JSON。

每个事务使用独立目录，例如：
```text
transactions/{transactionId}/
    plan.json
    events.jsonl
    state.json
```

## 35.1 `plan.json`：唯一完整意图记录
在任何真实 Rename 发生前，一次性写入不可变 RenamePlan，并执行：
```text
写临时文件
↓
Flush
↓
原子/安全替换为 plan.json
↓
再次确认可读取
↓
才允许开始 Phase 1
```

至少包含：
```text
schemaVersion
transactionId
创建时间
程序版本
文件系统能力摘要

每个对象：
Source NamespaceIdentity / Path
Temp Path
Target NamespaceIdentity / Path
预览时 FileIdentity（可为 Unknown）
PathSemantics 摘要
```

因为所有 Source→Temp→Target 意图已经在 `plan.json` 中持久化，所以 V1.0 **不要求每个单项 Rename 前都重写并 Flush 整份 Journal**。

## 35.2 `events.jsonl`：追加式进度
记录例如：
```text
PHASE1_APPLIED itemId
PHASE2_APPLIED itemId
ROLLBACK_APPLIED itemId
ERROR ...
```

允许小批量缓冲以控制 20,000～50,000 项性能，但：
- Phase 1 完成边界必须 Flush；
- Phase 2 完成边界必须 Flush；
- Transaction Completed / RolledBack 状态必须安全持久化；
- Crash Recovery 不得只相信 event log，必须与真实文件系统联合判断。

## 35.3 `state.json`：粗粒度事务状态
采用安全替换写入，记录：
```text
Prepared
Phase1InProgress
Phase1Complete
Phase2InProgress
Completed
RollbackInProgress
RolledBack
RecoveryRequired
```

`state.json` 是快速入口，不是恢复时的唯一事实来源。

---

# 36. 自动回滚

执行中出现失败：
```text
停止继续执行
↓
对已经发生的修改进行最大努力回滚
```

---

# 37. 回滚安全原则

回滚绝不：
- 覆盖外部新文件
- 删除未知对象
- 强行恢复到被其他程序占用的名称

如果回滚遇到冲突：
```text
停止
保留 Journal
报告未解决状态
```

---

# 38. 用户停止任务

长任务中的“停止”语义实际为：
> 停止并回滚。

---

# 39. Crash Recovery

程序启动时发现任何非终态 Transaction：
```text
Prepared / Phase1InProgress / Phase1Complete /
Phase2InProgress / RollbackInProgress / RecoveryRequired
```
必须先进入 Recovery Gate，不允许静默忽略。

UI 提示：
```text
检测到上次未完成的重命名操作。
```
提供：
```text
恢复到安全状态（推荐）
查看详情
```

V1.0 不提供“盲目从日志位置继续执行”。

## 39.1 恢复事实来源
Recovery 同时使用：
```text
plan.json
+
events.jsonl（辅助）
+
当前文件系统真实状态（权威事实）
```

对每个计划项检查：
- Source Path 是否存在；
- Temp Path 是否存在；
- Target Path 是否存在；
- 能获取时，各位置 FileIdentity 是否与计划预期相容；
- 是否出现事务外对象占据 Source / Temp / Target。

Namespace Path 占用是第一判断维度；FileIdentity 只是防替换 Guard。Hard Link 共享同一 FileIdentity 时不得因此把多个目录项合并。

## 39.2 恢复状态分类
每个计划项至少分类为：
```text
AT_SOURCE
AT_TEMP
AT_TARGET
MISSING
EXTERNALLY_MODIFIED
AMBIGUOUS
```

若所有项安全位于 Target，可将事务认定为“实际已完成但终态未落盘”，补写 Completed。

若事务只完成部分步骤，则 V1.0 默认目标是**恢复到执行前 Source 状态**，而不是猜测用户是否希望继续完成剩余 Rename。

## 39.3 幂等与安全原则
Recovery 必须幂等：
> 同一恢复过程被再次启动，只继续完成尚未安全完成的恢复步骤，不重复破坏已经恢复的对象。

恢复操作仍通过受控临时名 / 两阶段方式处理循环，不允许：
- 覆盖未知对象；
- 删除未知对象；
- 根据一条可能丢失的 Event 就认定磁盘状态；
- 在 `MISSING / EXTERNALLY_MODIFIED / AMBIGUOUS` 时自动强行修复。

无法安全恢复时：
```text
停止自动操作
↓
保留完整 Transaction 目录
↓
标记 RecoveryRequired
↓
向用户报告具体未解决项目
```

因此，即使程序恰好在“文件系统 Rename 已成功、进度事件尚未 Flush”的窗口崩溃，也能通过计划 + 实际路径重新判定，而不是机械重放同一步。

---

# 40. 撤销

V1 UI 支持：
> 撤销最近一次尚未撤销的成功 Rename 事务。

撤销：
- 先完整验证
- 仍使用两阶段 Rename
- 不覆盖任何对象
- 冲突则禁止撤销

V1 不做 Redo，也不允许跨过最近事务直接撤销更早历史。

---

# 41. 历史

底层可保留最近约 20 次事务记录，为未来历史管理器预留数据。

V1 不制作复杂操作历史页面。

---

# 42. 执行完成后的列表

执行成功后不立即清空列表。

显示完成结果并提供撤销。

---

# 43. 连续二次修改

执行成功后，新磁盘名称成为下一轮真实源名称。

历史中仍保留执行前后映射用于撤销。

---

# 44. 执行后排序

执行完成后不自动重新排序。

当前顺序保持不变，并进入：
```text
排序：自定义
```

---

# 45. 清空与重置

## 清空列表
只移除当前任务项目，保留当前规则，便于连续处理多批文件。

## 重置规则
恢复安全默认规则，不一定清空当前列表。

---

# 46. 程序重启行为

恢复：
- 窗口尺寸
- 面板宽度
- UI 偏好
- 默认排序偏好

不恢复：
- 上一次临时文件列表
- 上一次临时规则
- 上一次高级规则

程序每次启动默认进入安全空任务。

---

# 47. 预设系统

V1.0 支持轻量改名方案：
- 保存；
- 应用；
- 另存为；
- 重命名；
- 删除。

Preset 保存：
- V1.0 `RenameRuleSet` 中的常规名称参数；
- 单一 `SequenceConfig` 的启用状态、位置、补零、分隔符、起始值、步长。

Preset 不保存：
- 实际文件路径；
- 当前文件列表；
- 勾选状态；
- 手动排序结果；
- 当前预览；
- 任何 V1.1 尚未开放的 Rule Chain / Regex / Template / 扩展名修改配置。

排序不属于 V1.0 Preset。

内置 Preset 控制在少量：
- 连续编号；
- 名称 + 编号；
- 原名 + 编号；
- 查找替换。

---

# 48. 主界面结构

推荐布局：
```text
┌───────────────────────────────────────────────┐
│ 顶部工具栏                                    │
├──────────────┬────────────────────────────────┤
│              │                                │
│ 重命名设置   │       文件 / 文件夹列表        │
│              │                                │
│ 常规规则     │  原名称 → 新名称               │
│              │                                │
│ 高级设置     │                                │
│              │                                │
├──────────────┴────────────────────────────────┤
│ 状态信息                         执行改名      │
└───────────────────────────────────────────────┘
```

列表必须是视觉主体。

---

# 49. 顶部工具栏

常规入口：
```text
添加文件
添加文件夹
清空
排序
刷新
撤销
高级设置
```

---

# 50. 空列表状态

中央显示：
```text
将文件或文件夹拖到这里
```

并提供“添加文件”“添加文件夹”。

---

# 51. 排序入口 UI

顶部类似：
```text
排序：名称 ↑
```

菜单中选择：
```text
原始顺序
名称
扩展名
创建时间
修改时间
大小

升序
降序

混合
文件夹优先
文件优先
```

人工拖动后显示：
```text
排序：自定义
```

---

# 52. 左侧设置

常规主要呈现：
- 基础名称；
- 原名称组合方式；
- 连续编号；
- 常用文字操作；
- 高级设置入口。

V1.0 高级设置只需要轻量折叠：
```text
▸ 高级编号
   - 起始值
   - 步长

▸ 预设
```

V1.1 才增加：
```text
高级规则链
Regex / Template
复杂 Scope
扩展名风险操作
```

---

# 53. 底部状态

例如：
```text
83 项 · 80 项将改名 · 3 项无变化 · 0 错误
```

存在错误：
```text
3 个错误需要处理
```

该状态文字必须可点击或旁边提供：
```text
只看问题
```

问题视图至少过滤显示 Error；可同时包含 Warning。

“只看问题”只改变显示，不改变：
- 勾选状态；
- CurrentOrder；
- Sequence 编号；
- RenamePlan 参与集合。

存在任意 Error 时执行按钮禁用。

---

# 54. 执行过程 UI

执行时冻结：
- 文件列表
- 勾选
- 排序
- 规则编辑
- 预设应用
- 新文件拖入

允许：
- 查看进度
- 请求停止并回滚

---

# 55. 执行完成 UI

不弹无意义的强制成功确认框。

界面内显示：
```text
✓ 已成功重命名 83 项
```

并提供撤销。

---

# 56. 右键菜单

V1 建议支持：
```text
勾选
取消勾选

移到顶部
上移
下移
移到底部

从列表移除

在资源管理器中显示
复制完整路径
```

---

# 57. 删除行为

V1 不提供删除磁盘文件。

列表中的“移除”永远表示从当前任务中移除。

---

# 58. 键盘行为

`Ctrl + O`：添加文件。

`Delete`：仅当列表拥有焦点时从列表移除；在文本输入框中保持正常 Delete。

全局 `Ctrl + Z` 不绑定文件系统撤销。

---

# 59. 文件系统支持

V1.0 目标覆盖：
- NTFS；
- exFAT；
- FAT32；
- 移动硬盘；
- U盘；
- 网络盘；
- NAS；
- UNC 路径。

但安全承诺分级：

```text
Local / directly attached file system
→ Strong Recovery Protocol
```

```text
Remote / NAS / UNC
→ Best-effort Recovery
```

“Strong”表示程序严格执行持久计划、两阶段 Rename、身份复核和幂等恢复协议，不代表能对抗磁盘硬件故障、管理员强制修改或文件系统本身损坏。

网络环境可能出现：
- 连接中断；
- 服务端大小写语义差异；
- File ID 不可用或不稳定；
- Rename 原子性/共享锁行为与本地 NTFS 不同。

因此网络路径无法确认能力时必须保守失败，不得伪装成本地 NTFS 等价保证。

---

# 60. Unicode

完整支持：
- 中文
- 日文
- 韩文
- Emoji
- 其他 Unicode 文件名

程序内部统一使用 Unicode。

---

# 61. 长路径

尽可能使用现代 Windows 长路径兼容方式，不在业务逻辑中大量写死传统 MAX_PATH=260。

---

# 62. `.lnk`

Windows 快捷方式视为普通文件，只修改快捷方式文件名称，不修改其目标。

---

# 63. Symbolic Link / Junction

原则：
> 修改链接本身，不跟随链接修改目标。

未来目录遍历默认也不跟随 Reparse Point。

---

# 64. 文件占用

“文件被打开”不等于一定不能 Rename。

最终以实际文件系统操作结果为准。

---

# 65. 权限

程序默认：
```text
asInvoker
```

不要求管理员权限，不自动提权。

---

# 66. 跨目录批次

允许不同目录、不同盘符项目同时加入一个列表。

仍按最终列表顺序统一编号，文件只在各自原目录内 Rename。

V1 不移动文件、不跨卷复制。

---

# 67. 性能目标

性能目标以“UI 响应性 + 可完成性”为主，不把“正常可操作”解释为所有操作瞬间完成。

建议验收档位：

| 项目数 | V1.0 目标 |
|---:|---|
| 1～1,000 | 常用排序 / 预览体感基本即时 |
| 1,000～10,000 | UI 持续流畅，后台任务可取消 |
| 10,000～50,000 | UI 不长期假死；滚动正常；排序/预览可在后台完成 |
| 50,000+ | 允许导入，提示大型任务并保留取消能力 |

至少把约 20,000 项作为正式性能验收档：
- 主列表虚拟化有效；
- 导入期间 UI 可交互；
- 排序、Preview 可取消；
- 规则输入经过防抖，不阻塞 UI；
- “只看问题”能够快速定位少量 Error；
- 内存随数据量近似线性增长，不因创建等量 WPF 行控件爆炸；
- Transaction Journal 不对每一项重写整份大 JSON。

不设置人为的低数量硬限制。具体毫秒级 SLA 在实现基准测试后另行记录，不在冻结需求中拍脑袋写死。

---

# 68. UI 虚拟化

主列表必须使用虚拟化。

数据规模与控件数量解耦。

---

# 69. 导入性能

导入时只读取必要元数据：
- 路径
- 名称
- 扩展名
- 文件 / 文件夹
- 大小
- 创建时间
- 修改时间
- 必要的文件身份信息

不读取：
- 文件内容
- Hash
- EXIF
- 视频内容
- PDF 内容

---

# 70. 后台任务

以下任务不得阻塞 UI：
- 大规模导入
- 大规模文件属性读取
- 大规模排序
- 大规模预览
- Rename 执行
- 刷新

采用异步任务和取消机制。

---

# 71. 实时预览性能

输入变化采用短时间防抖，避免大量无意义重复计算。

---

# 72. Preview Generation

每次异步预览拥有 GenerationId。

只有最新 Generation 允许提交结果，旧计算不得覆盖新预览。

---

# 73. 技术选型

正式推荐：
```text
Language: C#
Framework: .NET 10 LTS
GUI: WPF
Architecture: MVVM
Target: Windows x64
```

---

# 74. 发布方式

目标：
```text
BatchRenamer.exe
```

采用 Self-contained Single-file Publish。

用户无需提前安装：
- .NET Runtime
- Python
- Java
- Qt

---

# 75. 发布原则

稳定优先。

V1 不采用激进 Assembly Trimming。

---

# 76. 软件架构

逻辑分层：
```text
UI / MVVM
        ↓
Application
        ↓
Domain
        ↓
Sorting Engine
Sequence Engine
Rule Engine
Validation Engine
        ↓
Rename Planner
Transaction Engine / Recovery Engine
        ↓
IPathSemanticsProvider
IFileSystemIdentityProvider
Windows File System Adapter
        ↓
Persistence
```

关键边界：
- UI 不直接判断文件名冲突；
- Validation 不自己猜目录是否大小写敏感；
- FileIdentity 与 NamespaceIdentity 分开；
- Transaction / Recovery 不重新解释 UI 规则。

---

# 77. 核心模型

主要对象：
```text
RenameItem
NamespaceIdentity
FileIdentity
RenameRuleSet
SequenceConfig
RenameContext
RenamePreview
ValidationResult
RenamePlan
RenameTransaction
RecoveryState
Preset
```

V1.0 不需要把任意高级 Rule Chain 暴露为用户可编辑模型，但 Domain 层可以保留可扩展规则接口。

---

# 78. RenameItem

至少包含：
```text
Id

CurrentPath
ParentDirectory
CurrentName
Stem
Extension

ItemType
TypeGroup（预留）

OriginalImportIndex
CurrentOrder

IsChecked

FileSize
CreationTime
ModificationTime

NamespaceIdentity
FileIdentity   // 可 Unknown

PreviewName
ValidationState
```

其中：
- `NamespaceIdentity` 用于导入去重和路径判等；
- `FileIdentity` 用于执行前 / Recovery 的对象一致性 Guard；
- 两者禁止互相替代。

为未来目录遍历预留：
```text
ParentItemId
RelativePath
Depth
```

---

# 79. SortingEngine

负责：
- Natural Sort
- 扩展名
- 时间
- 大小
- 对象优先级
- 升降序

输出新的 CurrentOrder，不生成名称。

---

# 80. SequenceEngine

V1.0 只维护一套：
```text
SequenceConfig
```

包含：
```text
Enabled
Start
Step
Padding
Position
Separator
```

作用域固定：
```text
ALL_CHECKED_ITEMS
```

编号输入顺序只来自 `CurrentOrder`。

V1.1 才扩展：
```text
SequenceDefinitionId
scope_key(item)
FILE / FOLDER / EXT:* / CUSTOM:*
```

若未来存在多套序列，模板引用必须显式绑定定义，禁止多个计数器共享含糊的默认 `{序号}`。

---

# 81. RuleEngine

统一规则执行接口，但 V1.0 用户侧采用固定顺序。

V1.0 实际规则能力包括：
```text
ReplaceLiteralRule
DeleteTextRule
CharacterRule
CaseRule
NameCompositionRule
AddTextRule
SequenceRule   // 单一 SequenceConfig
```

所有规则只处理主文件名 `WorkingName`，扩展名只读保护。

V1.1 再增加：
```text
TemplateRule
RegexRule
ExtensionRule
Custom Rule Chain
Scoped SequenceRule
```

---

# 82. RenameContext

V1.0 规则可访问：
```text
当前项目
源主文件名
当前 WorkingName
受保护扩展名
当前顺序
单一序号（若启用）
对象类型
```

Validation / Sorting 可访问创建时间、修改时间、大小等元数据，但 V1.0 不把它们全部暴露为命名模板变量。

未来增加：
```text
父目录
相对路径
EXIF
媒体信息
多 Sequence Definition
```

---

# 83. ValidationEngine

负责结构化验证，例如：
```text
EMPTY_NAME
INVALID_CHARACTER
RESERVED_NAME
TARGET_EXISTS
DUPLICATE_TARGET
SOURCE_MISSING
SOURCE_IDENTITY_CHANGED
PARENT_CHILD_CONFLICT
PERMISSION_ERROR
PATH_ERROR
FILESYSTEM_SEMANTICS_UNKNOWN
```

V1.1 才启用与对应功能有关的：
```text
REGEX_ERROR
TEMPLATE_ERROR
EXTENSION_ERROR
```

内部使用结构化 Error Code，UI 再映射为中文提示。

Validation 必须依赖 `IPathSemanticsProvider` 与文件系统 Adapter，不得用字符串 `ToLower()` 代替真实路径语义。

---

# 84. RenamePlanner

负责将全部 Preview + 验证结果转换为冻结的 RenamePlan。

执行层不重新解释规则。

---

# 85. TransactionEngine

只关心冻结后的：
```text
Source Namespace Path
Temp Namespace Path
Target Namespace Path
Expected FileIdentity（可 Unknown）
```

负责：
- 持久化 `plan.json`；
- 两阶段执行；
- 追加式 `events.jsonl`；
- 粗粒度 `state.json`；
- 回滚；
- Crash Recovery；
- Undo。

不得重新运行 RuleEngine，也不得读取 UI 当前控件。

---

# 86. Windows API 使用原则

优先使用现代 .NET API。

仅在必要能力使用 Win32 / Windows 文件系统 API：
- File ID / Volume ID；
- 查询目录大小写敏感语义；
- Reparse Point；
- 文件系统能力探测；
- 标准 .NET API 不足的 Rename 边缘情况。

必须提供：
```text
IPathSemanticsProvider
```
至少回答目标目录的：
- 名称比较是否大小写敏感；
- 可确认的路径 / 组件限制；
- 能否可靠判断相关 Rename 语义。

普通 Windows 目录通常按大小写不敏感处理，但**不得全局假设所有目录都 `OrdinalIgnoreCase`**；NTFS 可存在目录级 Case Sensitivity，网络服务端也可能不同。

无法可靠确认时，Validation 应采取保守策略并给出结构化错误/警告。

---

# 87. 不使用命令行 Rename

核心文件操作禁止通过：
```text
cmd.exe
PowerShell
shell command
```

执行。

只使用 .NET 文件系统 API 和必要的 Win32 API。

---

# 88. 持久化目录

默认：
```text
%LocalAppData%\BatchRenamer\
```

概念结构：
```text
settings.json

presets\
history\
transactions\
logs\
```

其中 `transactions\{transactionId}\` 保存该事务的 `plan.json / events.jsonl / state.json`。

---

# 89. JSON 数据

V1.0 不引入 SQLite。

使用版本化持久格式保存：
- Settings：JSON；
- Presets：JSON；
- History：JSON；
- Transaction Plan / State：JSON；
- Transaction Progress：JSONL append-only。

所有持久格式包含 `schemaVersion`。

禁止把 20,000～50,000 项事务的每一步进度都通过“重写整份巨大 Journal JSON”实现。

---

# 90. 安全写入

JSON 更新采用：
```text
写临时文件
↓
Flush
↓
安全替换
```

---

# 91. Settings 损坏

设置文件损坏时：
```text
备份损坏文件
↓
加载安全默认设置
↓
继续启动
```

---

# 92. Journal 损坏

事务文件损坏按层级处理：

### `plan.json` 无法解析 / 校验失败
- 不自动碰任何相关磁盘对象；
- 隔离事务目录；
- 标记需要人工处理；
- 保留诊断信息。

### `events.jsonl` 尾部损坏但 `plan.json` 完整
- 允许忽略最后一个不完整事件；
- 不以事件日志单独推断最终状态；
- 通过 `plan.json + 当前文件系统` 重建。

### `state.json` 损坏
- 将其视为提示信息丢失；
- 仍可通过计划、事件和真实文件系统进入 Recovery；
- 不因此直接放弃一个本可安全恢复的事务。

---

# 93. Preset 文件

实际磁盘文件使用 GUID.json，用户显示名称存储在 JSON 内部。

---

# 94. 日志

仅记录必要：
- 程序异常
- 文件系统错误
- Transaction 错误
- Recovery 错误
- 程序版本

不记录大量无意义 UI 操作。

---

# 95. 网络

V1 完全本地运行。

不需要：
- 登录
- 云端
- 上传文件名
- 账号系统
- 自动更新
- 网络规则库

---

# 96. V1 不实现功能

## V1.1 下放的高级重命名能力
- 任意 Rule Chain / 规则重排；
- Regex Replace；
- 复杂 Template；
- 日期命名变量；
- 修改扩展名；
- 文件 / 文件夹分别编号；
- 按扩展名分别编号；
- 多条编号规则；
- 复杂规则作用范围。

## 文件夹高级处理
- 展开一级；
- 递归遍历；
- 父目录变量；
- 父子 Rename 依赖图；
- 按目录重新编号。

## 文件内容
- EXIF；
- 视频 Metadata；
- ID3；
- PDF Metadata。

## 重复内容
- Hash 扫描；
- 重复文件检测。

## 类型系统
- 图片 / 视频 / 音频等复杂类型组；
- 自定义类型组。

## 编号
- 字母编号；
- 罗马数字；
- 自定义编号组；
- 锁定单个文件序号。

## 排序
- 中文拼音排序。

## 文件管理
- 删除文件；
- 移动文件；
- 复制文件；
- 覆盖文件。

## 系统
- 实时 FileSystemWatcher；
- 自动更新；
- 云同步；
- 插件系统；
- Portable 配置模式。

---

# 97. 后续迭代优先方向

V1.1 优先考虑：
1. 自定义高级 Rule Chain；
2. Regex Replace；
3. Template；
4. 文件 / 文件夹 / 扩展名分组编号；
5. 显式多 Sequence Definition；
6. 修改扩展名风险操作；
7. 更细规则 Scope；
8. 日期命名变量。

V2 再优先考虑：
1. 文件夹展开与递归遍历；
2. `{父文件夹}` 模板变量；
3. 按所在目录重新编号；
4. 文件类型组；
5. EXIF 拍摄时间；
6. 完整操作历史；
7. Hash 重复文件检测；
8. 自动冲突解决策略；
9. 父子路径依赖事务。

---

# 98. 开发顺序

```text
1. Domain 数据模型 + NamespaceIdentity / FileIdentity
        ↓
2. IPathSemanticsProvider + 文件系统 Adapter
        ↓
3. Natural Sorting
        ↓
4. 单一 SequenceEngine
        ↓
5. V1 固定 RuleEngine
        ↓
6. ValidationEngine
        ↓
7. RenamePlanner
        ↓
8. Transaction Plan / 两阶段 Rename
        ↓
9. Rollback + Crash Recovery
        ↓
10. 核心 GUI + 虚拟化
        ↓
11. Drag & Drop / Manual Sort / 批量勾选
        ↓
12. Preview / 问题筛选 / Preset
        ↓
13. 性能与真实文件系统验收
        ↓
14. EXE 发布
```

原则：
> 先证明“路径判断正确、交换循环能改、崩溃后能恢复”，再做 UI 打磨；V1.1 高级规则不得反向拖累 V1.0 首发。

---

# 99. 测试策略

核心逻辑必须脱离 GUI 测试。

## 99.1 Natural Sort
验证：
```text
1
2
10
```

以及：
```text
S1E2
S1E10
S2E1
S10E1
```

## 99.2 SequenceEngine

覆盖：
- 最终 CurrentOrder 改变后序号同步变化；
- 未勾选对象不占号；
- 起始值 0 / 1 / 任意整数；
- 步长；
- 补零；
- 混合文件 / 文件夹仍只有一个统一序列；
- V1.0 不出现第二套活动 SequenceConfig。

---

## 99.3 RuleEngine

覆盖 V1.0 固定管线：
- 字面量查找替换；
- 删除文本；
- Unicode Grapheme 字符裁剪；
- 大小写；
- 名称组合；
- 前后缀；
- 单一连续编号；
- 扩展名始终保持不变；
- 规则执行顺序不可由 UI 重排。

---

## 99.4 Validation

覆盖：
- 空名称；
- 非法字符；
- 保留名；
- 批次重复目标；
- 外部目标已存在；
- `A↔B` / 三向循环中的目标虽存在但属于 VacatingSourceSet，应允许；
- 未勾选 / 无变化项目占据目标，应阻止；
- 仅大小写 Rename；
- 父子冲突；
- 网络路径异常；
- 源文件消失；
- FileIdentity 被外部替换；
- 大小写敏感目录与普通目录使用不同 PathSemantics。

---

## 99.5 Transaction

重点测试：
```text
A ↔ B
```

```text
A → B
B → C
C → A
```

以及：
```text
photo.jpg → Photo.jpg
```

并测试：
- 两个 Hard Link 作为两个 Namespace Entry 分别改名；
- 20,000 项事务计划只在开始时完整持久化一次，不为每个进度重写整个计划；
- Phase 边界状态安全落盘；
- 中途失败进入受控 Rollback。

---

## 99.6 Recovery

至少模拟崩溃点：
```text
plan.json 已 Flush，Phase 1 尚未开始
Phase 1 执行一半
Phase 1 完成、状态未落盘
Phase 2 执行一半
最后一个 Rename 成功、Completed 尚未落盘
Rollback 执行一半
```

每次重新启动都必须：
```text
读取 Plan
↓
重扫真实 Source / Temp / Target
↓
分类 RecoveryState
↓
安全补写 Completed 或幂等恢复到 Source
```

额外覆盖：
- `events.jsonl` 最后一行截断；
- `state.json` 损坏；
- 外部对象占据待恢复 Source；
- FileIdentity 变化；
- 重复运行 Recovery 不产生二次破坏。

---

## 99.7 真文件系统集成测试
自动创建临时目录和真实文件，实际运行 Rename 后验证最终文件系统状态。

---

# 100. V1 验收案例

## Case 1：自然排序
输入：
```text
1.jpg
10.jpg
2.jpg
```

结果：
```text
1.jpg
2.jpg
10.jpg
```

编号：
```text
Photo_001.jpg
Photo_002.jpg
Photo_003.jpg
```

## Case 2：人工调整
把第三项拖到第一位，第三项必须立即变 001。

## Case 3：取消勾选
```text
☑ A
☐ B
☑ C
```

编号：
```text
A → 001
B → —
C → 002
```

## Case 4：名称交换
```text
A.txt → B.txt
B.txt → A.txt
```

安全完成。

## Case 5：三向循环
```text
A → B
B → C
C → A
```

安全完成。

## Case 6：中途失败
100 项执行过程中制造异常，程序必须停止、最大努力回滚并报告结果。

## Case 7：异常退出
Phase 1 中强制结束程序，重新启动后自动识别未完成 Journal 并允许恢复。

## Case 8：已有目标冲突
目录中存在一个**不属于本次 VacatingSourceSet** 的 `Photo_002.jpg` 时，预览阶段立即发现并禁止执行；但 `A↔B` 交换不得被该规则误杀。

## Case 9：Unicode
例如：
```text
照片🌄1.jpg
照片🌄2.jpg
```

排序、字符处理、重命名正常。

## Case 10：跨目录统一编号
允许不同目录文件统一编号但各自在原目录改名。

## Case 11：父子对象
父目录和内部对象同时出现在列表且父目录准备改名时，明确阻止执行。

## Case 12：大型列表
至少进行约 20,000 项规模测试，要求列表可滚动、排序和预览可完成、UI 不长期无响应、内存合理；事务进度不得通过每项重写整个巨大 Journal。

## Case 13：Hard Link
同一 NTFS 文件对象的两个 Hard Link 以不同路径导入时，必须保留为两个独立 RenameItem，可分别改名。

## Case 14：大小写语义
普通大小写不敏感目录与显式 Case-sensitive 目录分别验证目标冲突和 case-only rename 行为，不得全局写死一种字符串比较器。

## Case 15：Crash 窗口
模拟“真实 Rename 已成功但对应 progress event 尚未 Flush”后强制退出，Recovery 必须通过 Plan + 文件系统事实正确识别，而不是机械重复执行。

---

# 101. 代码质量原则

- 核心业务逻辑不依赖 WPF 控件
- Rename 安全逻辑不写在 ViewModel 中
- 文件系统实现通过明确接口隔离
- 所有危险文件操作必须可测试
- 错误使用结构化 Error Code
- 不吞异常
- 不把“大 catch”当恢复机制
- 所有异步任务支持生命周期控制
- 所有 Transaction 使用唯一 ID
- 所有持久格式使用 Schema Version

---

# 102. 产品最终一句话定义

> 一个以“排序 + 人工调整顺序”为核心，并通过实时预览、安全事务和可撤销机制完成 Windows 文件/文件夹批量自定义重命名的本地工具。

---

# 103. V1 最终核心链路

```text
文件 / 文件夹拖入
        ↓
IPathSemanticsProvider 获取目录语义
        ↓
构建 NamespaceIdentity + RenameItem
        ↓
记录 FileIdentity（可获取时）
        ↓
自动排序
        ↓
人工拖动微调
        ↓
形成最终列表顺序
        ↓
配置 V1.0 RenameRuleSet
        ↓
单一 SequenceEngine 根据最终顺序计算序号
        ↓
固定 RuleEngine 生成候选主文件名
        ↓
拼回受保护扩展名
        ↓
ValidationEngine 全量校验
   - DuplicateTarget
   - VacatingSourceSet
   - TargetExists
   - PathSemantics
        ↓
实时预览 / 只看问题
        ↓
用户点击执行
        ↓
再次读取文件系统状态 + Identity
        ↓
生成不可变 RenamePlan
        ↓
安全持久化 transaction plan.json
        ↓
Phase 1：全部 Source → 唯一 Temp
        ↓
Flush Phase1 边界状态
        ↓
Phase 2：全部 Temp → 最终 Target
        ↓
验证结果 / Flush Completed
        ↓
完成 Transaction
        ↓
提供最近一次撤销

若任意阶段异常退出：
        ↓
下次启动读取 Plan + Events + 真实文件系统
        ↓
幂等 Recovery / 安全回滚
```

---

# 104. 冻结声明

本文件作为 **V1.0 Rev.A 开发唯一需求与设计基准**。

Rev.A 已替代最初冻结版中与以下事项相关的旧定义：
- `TARGET_EXISTS` 与交换 / 循环 Rename 的冲突逻辑；
- File ID 作为导入去重主依据；
- 单文件大 Journal 每步重写 / Flush 的隐含实现；
- Crash Recovery 仅凭 Journal 阶段机械恢复；
- V1.0 多 Sequence / Regex / Template / 修改扩展名等过重范围。

进入编码后：
- 不根据开发过程临时扩大 V1.0 范围；
- 新想法优先记录为 V1.1 / V2；
- 涉及文件安全的行为不得绕过本方案；
- 若必须修改冻结需求，应先修改本设计文档，再修改代码。

**冻结状态：Rev.A 已冻结，可进入编码阶段。**

---

