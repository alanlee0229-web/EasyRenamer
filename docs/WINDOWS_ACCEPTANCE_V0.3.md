# Windows / VS2026 验收清单 — V0.3

## A. Build / Launch

1. VS2026 打开 `BatchRenamer.UIPrototype.sln`。
2. `生成 -> 重新生成解决方案`。
3. 确认 3 个项目均成功：App / Core / Core.SmokeTests。
4. 以 `BatchRenamer.App` 为启动项目 F5。

必须：无 XAML Parse Exception、Binding Exception、未处理异常。

## B. 视觉快速检查

观察右侧规则栏：

- Header 固定；
- 基础命名 / 连续编号 / 文字处理为 3 个轻量 Section；
- ComboBox 为白底浅边框，不是旧式灰块；
- Checkbox 为蓝色，不跟随系统棕色 Accent；
- 右侧纵向 ScrollBar 浅灰、细，不再有黑色常驻视觉。

## C. 未参与语义

1. 取消第 2 行勾选。
2. 检查该行。

必须：

- 原名称仍清楚；
- 新名称为 `—`；
- 新名称不是蓝色强调；
- 状态为“未参与”；
- 后续参与项连续编号自动前移，不给未参与项占号。

表头 Checkbox 此时应显示“部分选择”的中间态。

## D. 大小写修复

将连续编号暂时关闭，基础名称依次输入并选择“单词首字母大写”：

- `HELLO WORLD` -> `Hello World`
- `HELLO_WORLD-test` -> `Hello_World-Test`
- `my PHOTO collection` -> `My Photo Collection`
- `2026 SUMMER-trip` -> `2026 Summer-Trip`

扩展名应保持原样。

也可运行 `BatchRenamer.Core.SmokeTests` 进行同组检查。

## E. 2,000 / 20,000 ScrollBar

20,000 测试：`Ctrl + Shift + T`。

必须：

- DataGrid 仍可流畅滚动；
- 纵向 Thumb 不能缩成一个点，最低约 36 px；
- 鼠标 Hover Thumb 时明显加宽；
- 拖动 Thumb 可快速跳转；
- Track 上下区域点击仍能分页式跳转；
- Preview 性能不应因 ScrollBar 样式出现数量级退化。

`Ctrl + Shift + D` 恢复 7 项。

## F. 多选拖动

1. 选中连续 2–3 行；
2. 从前部拖到后部；
3. 分别在目标行上半区、下半区 Drop；
4. 再从后部拖回前部。

必须：

- 出现蓝色插入线；
- 上半区为前插、下半区为后插；
- 多选相对顺序保持；
- 不多偏一行；
- 编号与最终视觉顺序一致。

## G. Header 三态

- 全选 -> 表头勾选；
- 取消任意一行 -> 表头中间态；
- 全不选 -> 表头未勾；
- 点击表头可重新统一全部状态。

## H. 回归性能

20,000 项：

- 修改基础名称；
- 查找/替换；
- 大小写转换；
- 排序；
- 搜索；
- 只看问题。

记录 Preview latency。V0.2 实机参考为约 76 ms；V0.3 不要求逐毫秒相同，但不应出现明显数量级退化或持续 UI 卡死。
