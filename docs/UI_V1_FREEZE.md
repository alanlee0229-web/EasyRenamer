# BatchRenamer V1 UI Freeze

## Freeze Point

V0.46 用户实机确认后，V1.0 主 UI / IA 正式冻结。

冻结内容：

- Desktop SaaS 浅色极简视觉语言；
- 白 / 浅灰 Surface + 蓝色主强调；
- 左侧窄导航；
- Header；
- 文件列表为主工作区；
- 右侧重命名规则栏；
- 搜索 / 只看问题；
- 拖动 / 上移 / 下移 / 排序；
- 左下角设置入口；
- 设置勾选可选能力、主面板动态显示对应模块的机制；
- 基础命名 / 连续编号的布局；
- 状态、滚动条、未参与项目等已验收视觉语义。

## 后续允许修改 UI 的条件

仅在以下情况修改：

1. 明确功能 Bug；
2. 新的 Transaction / Recovery 状态必须呈现；
3. 严重阻碍实际使用的 UX 问题；
4. 后续正式版本新增已冻结 Roadmap 功能。

不再因为“还能更漂亮一点”反复改动 V1 基础 UI。

## V0.5 Freeze Verification

V0.5 未修改视觉 XAML：

```text
MainWindow.xaml SHA256
5949e602670203379cb1731ce8aebb4fb22ed54455c689a04e738c3040287610

App.xaml SHA256
a72f312548c185dc6a7841bf6d8c859f66efe273f8c007a35cc1f6aeaa81a277
```

以上与 V0.46 完全一致。
