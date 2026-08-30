# P0-UI-001 — 7 项数据仍卡顿 / DataGrid 长时间空白

## 结论

V0.1.x 的主要卡死原因已经从代码层面定位，不再只是“WPF 性能可能较差”的泛化判断。

### 根因：DataGrid 行生成期间发生 CollectionView 重入刷新循环

V0.1.x 的 DataGrid 单元格包含：

```xml
<CheckBox IsChecked="{Binding IsIncluded, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
          Checked="ItemCheck_Changed"
          Unchecked="ItemCheck_Changed" />
```

当 DataGrid 首次创建一行时，`IsIncluded=true` 会通过 Binding 写入 CheckBox，CheckBox 随即触发 `Checked`。

`Checked` 调用：

```csharp
_vm.RebuildPreview();
```

而旧 `RebuildPreview()` 结尾无条件调用：

```csharp
ItemsView.Refresh();
```

这会使 CollectionView / DataGrid 重新构造可视行。新行的 CheckBox 又因为绑定到 `true` 再次触发 `Checked`，再次 Refresh。

逻辑链为：

```text
DataGrid 生成行
→ CheckBox Binding 设置 IsChecked=true
→ Checked
→ RebuildPreview()
→ ItemsView.Refresh()
→ DataGrid 重新生成行
→ Checked
→ ...
```

这解释了实机现象：

- 底部状态已经显示 7 项，说明 ViewModel 数据存在；
- 表格长时间不稳定/空白；
- UI 主线程持续忙；
- 文件数量极少仍可卡数分钟。

## V0.2 修复合同

1. DataGrid CheckBox **禁止**挂 `Checked/Unchecked -> Refresh` 事件。
2. `IsIncluded` 变化由 ViewModel 订阅 Item 的 `InclusionChanged`，只调度一次 Preview。
3. Preview 规则输入采用 120ms debounce。
4. Preview 纯计算移动到 `BatchRenamer.Core.PreviewEngine`，通过 `Task.Run` 在工作线程执行。
5. 每行 Preview 由 4 个独立属性合并为一个不可变 `PreviewRowState`，一行一次 PropertyChanged。
6. Preview 完成后不再无条件 `ItemsView.Refresh()`；只有“只看问题”或搜索过滤确实依赖 Preview 时才刷新一次。
7. 排序/大批增删使用 `BulkObservableCollection.ReplaceAll/AddRange`，一次 Reset，不逐项广播成千上万 CollectionChanged。
8. V0.2 仍不连接真实 RenameTransaction，先验证 UI 稳定性与响应性。

## 仍需 Windows 真机验收

本执行环境不能运行 WPF，因此以下性能数值必须由 VS2026 / Windows 真机确认：

- 7 项启动后 DataGrid 是否立即出现；
- 规则输入是否顺滑；
- Ctrl+Shift+T 加载 20,000 条合成数据后的滚动与预览响应；
- Preview latency 文本；
- CPU 是否在静置后恢复低占用。

如果 7 项在 V0.2 仍明显卡顿，则转入 WPF ETW / Visual Studio UI Responsiveness / Binding Diagnostics，不能继续用猜测方式优化。
