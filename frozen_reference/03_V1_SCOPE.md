# V1.0 Rev.A 范围矩阵

## V1.0 核心必须有
- 文件 / 文件夹拖入
- 添加文件 / 添加文件夹
- 基于 NamespaceIdentity 的重复目录项去重
- FileIdentity 执行前一致性 Guard
- 列表勾选、多选、表头全选/全不选、移除
- 原始顺序 / 名称 / 扩展名 / 创建时间 / 修改时间 / 大小排序
- Natural Sort
- 单项 / 多项拖动
- 上移 / 下移 / 顶部 / 底部
- 常规基础名称
- 原名组合
- 前缀 / 后缀
- 字面量查找替换
- 删除指定文字
- 字符删减 / 保留
- 大小写
- 单一连续编号
- 高级编号参数：起始值、步长、从 0 开始
- 扩展名全程保护
- 实时预览
- 跨目录来源辨识
- “只看问题”过滤
- 全量合法性与冲突校验
- VacatingSourceSet 冲突模型，支持 A↔B / 多向循环
- IPathSemanticsProvider（含目录级大小写语义）
- 两阶段 Rename
- 不可变 transaction plan.json
- 追加式 events.jsonl
- state.json
- 自动回滚
- 幂等 Crash Recovery
- 最近一次撤销
- 轻量 Preset
- 虚拟化列表
- 大规模异步处理
- 约 20,000 项正式性能验收

## V1.1 下放
- 任意高级 Rule Chain / 规则重排
- Regex Replace
- 复杂 Template
- 日期命名变量
- 文件 / 文件夹分别编号
- 按扩展名分别编号
- 多 Sequence Definition
- 复杂规则 Scope
- 修改扩展名

## 架构预留但 V1.0 不开放
- TypeGroup
- ParentItemId
- RelativePath
- Depth
- 按父目录编号
- 自定义编号组
- 复合扩展名识别
- 完整历史浏览器
- SimpleCompatible / 高级规则投影机制

## V2+
- 文件夹递归遍历
- 父目录变量
- 父子路径依赖事务
- EXIF
- 音视频元数据
- Hash 重复检测
- 中文拼音排序
- 字母 / 罗马数字编号
- 文件系统实时监控
- 自动冲突解决
- 自动更新
- 云同步
- 插件系统
