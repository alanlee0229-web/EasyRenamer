# BatchRenamer UX Audit — V0.3

## 审查前提

V0.2 已在 Windows 实机完成 20,000 条数据测试，Preview 约 76 ms。V0.3 的目标因此不是继续“优化一个数字”，而是处理高频桌面软件的视觉状态与交互语义。

## 已处理

### A. Rules panel

问题：V0.2 仍带明显属性面板感，右侧原生深色滚动条破坏浅色视觉。

处理：

- 固定 Header + 可滚动内容；
- 3 个轻量规则 Section；
- 统一 TextBox / ComboBox / CheckBox；
- ScrollBar 改为 4 px 可视 Grip，Hover / Drag 7 px；
- Thumb 最小 36 px。

### B. Included / Excluded semantics

问题：未参与行与参与行使用相同蓝色新名称，容易让用户误认为仍会执行。

处理：

- Excluded `DisplayNewName = —`；
- 原名保持正常可读；
- 状态灰色“未参与”；
- 不参与编号计数。

### C. Case conversion

问题：ALL-CAPS 输入不能得到用户预期的 Title Case。

处理：

- UI 名称改为“单词首字母大写”；
- lower normalization -> boundary capitalization；
- `_`、`-`、空格及其他非字母数字字符均开启新词；
- 扩展名不进入大小写处理。

### D. Large list navigation

问题：2,000+ 项时比例式 ScrollBar Thumb 过小，鼠标难以抓取。

处理：

- `Thumb.MinHeight = 36`；
- Hover / Drag 增强；
- 保留 Track PageUp / PageDown；
- 保留 DataGrid Recycling virtualization；
- 不引入分页器。

### E. Checkbox / ComboBox visual consistency

问题：V0.2 Checkbox 受系统 Accent 影响，实机出现棕色，与蓝色品牌主色不一致；ComboBox 偏原生灰色。

处理：自定义蓝色 Checkbox 和白底现代 ComboBox。

### F. Header select semantics

问题：旧 header toggle 使用本地 bool，单行取消后可能与真实选择状态不同步。

处理：ViewModel 提供 `AllIncludedState`：

- `true`：全部参与；
- `false`：全部不参与；
- `null`：部分参与。

### G. Drag ordering

审查发现：旧版多选项向后拖动时，目标索引基于原列表计算，而源项移除后索引发生偏移；且没有明确 before/after 视觉反馈。

处理：

- `removedBeforeTarget` 校正目标 index；
- 指针位于目标行上半区 -> 前插；下半区 -> 后插；
- 使用 2 px 蓝色插入线反馈。

## 暂不进入 V0.3 的事项

- 高级规则折叠/可编辑 Rule Chain：V1.1 范围；
- 分页：不适合本地文件工具主流程；
- 右侧面板宽度自由拖拽：可在后续真实使用反馈中决定；
- 历史 / 设置页面：仍为占位；
- 真实执行按钮：必须等 Validation / Transaction。

## V0.3 Gate

只有以下均通过，才进入 V0.4 ValidationEngine：

1. 7 / 2,000 / 20,000 项均能正常滚动；
2. 右侧滚动条不再以黑色常驻形式出现；
3. 20,000 项 Thumb 肉眼和鼠标均可抓取；
4. 未参与项显示 `— / 未参与`；
5. ALL-CAPS Title Case smoke cases 全过；
6. 多选拖动前插/后插无错位；
7. Checkbox 主色与应用蓝色一致；
8. 不出现新的 Binding Exception / UI stall。
