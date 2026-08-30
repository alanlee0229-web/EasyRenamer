# PS-05.5 Open Source License Freeze

## 冻结结果

```text
PROJECT_LICENSE = Apache License 2.0
SPDX = Apache-2.0
```

仓库根目录 `LICENSE` 与 Apache Software Foundation 官方 `LICENSE-2.0.txt` 完全同字节：11,358 bytes，SHA256 为 `CFC7749B96F63BD31C3C42B5C471BF756814053E847C10F3EB003417BC523D30`。没有删改条款，也没有增加项目自定义条款。

官方来源：

- [Apache License 2.0 text](https://www.apache.org/licenses/LICENSE-2.0.txt)
- [Apache License 2.0 overview and SPDX identifier](https://www.apache.org/licenses/LICENSE-2.0)

本项目没有现存根级 NOTICE，因此本轮不编造 NOTICE。Apache 官方文本的 NOTICE 条件只在上游 Work 实际包含 NOTICE 时触发；第三方依赖义务独立审查如下。

## 第三方依赖审查

`dotnet list src\BatchRenamer.App\BatchRenamer.App.csproj package --include-transitive` 的实际 `net10.0-windows` 结果：

| 组件 | 关系 | 声明许可证 | 证据与处理 |
| --- | --- | --- | --- |
| WPF-UI 4.3.0 | 直接 NuGet 依赖 | MIT | 项目文件、NuGet metadata 与包内 `LICENSE.md` 一致；不得被本项目 Apache-2.0 重新授权。 |
| WPF-UI.Abstractions 4.3.0 | 传递依赖 | MIT | NuGet metadata 与包内 `LICENSE.md` 一致；不得被本项目 Apache-2.0 重新授权。 |

[NuGet 的 WPF-UI 4.3.0 页面](https://www.nuget.org/packages/WPF-UI/4.3.0)确认项目引用版本及 `net10.0-windows` 的 WPF-UI.Abstractions 依赖。

WPF-UI 4.3.0 NuGet 包随附 `ThirdPartyNotices.txt`，明显列出：

- VirtualizingWrapPanel 2.0.6 — MIT notice。
- Fluent UI System Icons 1.1.242 — MIT notice。
- dotnet/wpf 8.0 — MIT notice。
- Microsoft UI XAML 3.0 — MIT notice。
- Segoe Fluent Icons Font 3.0 — Microsoft Platform 使用条款，并明确不授予向第三方分发或再许可字体的权利。

本轮不复制、改写或拼接这些上游文本，也不新增猜测性的根级 NOTICE。PS-06 / PS-07 Release Packaging 必须从实际锁定的 WPF-UI 4.3.0 包原样保留其 `LICENSE.md` 与 `ThirdPartyNotices.txt`，并确认没有将 Segoe 字体文件作为独立内容再分发。该事项是发布包装义务，不改变本项目 Apache-2.0 冻结结论。

`Microsoft.NET.ILLink.Tasks` 可出现在还原资产中，但 `dotnet list package --include-transitive` 不把它列为应用依赖；它是 .NET SDK publish/build tooling，不在本轮应用运行时第三方清单中。

## 变更边界

- Production code changed：NONE。
- Transaction Core changed：NONE。
- UI / build / publish configuration changed：NONE。
- Semantic behavior change：NONE。
