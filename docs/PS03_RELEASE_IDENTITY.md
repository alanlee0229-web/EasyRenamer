# PS-03 Release Identity / Branding

## 冻结身份

- Public 窗口：`easy重命名`
- Public 产品与文件描述：`easy重命名 / BatchRenamer`
- Public 版本：Product `1.0.0`，File `1.0.0.0`
- Internal 窗口：`easy重命名 — INTERNAL TEST`
- Internal 产品：`BatchRenamer Internal Test`
- Internal 版本：Product `1.0.0-internal`，File `1.0.0.0`
- 可执行文件：`BatchRenamer.exe`

`src/BatchRenamer.App/BatchRenamer.App.csproj` 是版本与 Windows 文件元数据的权威来源。Gate 与 inspector 中的固定值属于发布合同断言，不是第二套版本来源。

## 自动验证

```powershell
powershell -ExecutionPolicy Bypass -File tools\verify_public_build.ps1
```

默认 Gate 自动检查 Public 与 Internal 的 ProductName、FileDescription、FileVersion、ProductVersion、BuildFlavor、程序集身份、Internal QA 存在性以及 Public 隔离，并保留 PS-02 Negative Control。

## 正式图标

```text
ICON_ASSET_STATUS = APPROVED_AND_INTEGRATED
```

单一权威资源为 `src/BatchRenamer.App/Assets/BatchRenamer.ico`，SHA256 为 `467DF074F455504261CE35B7B8F0B5494A575DB8EEE41084F8515F3AB97306D1`。ICO 包含 16、24、32、48、64、128、256 像素的 32-bit PNG 图层。项目通过 `ApplicationIcon` 接入 EXE / Explorer executable resource，并将同一 WPF Resource 用作主窗口 `Icon`，统一 Window、Taskbar 与 Alt+Tab 图标。Release-Public 与 Release-Internal 共用该资源，身份差异继续由冻结的标题和 metadata 表达。

## Hash 规则

PS-03 产生的 EXE/ZIP SHA256 仅为 qualification artifact，不是最终 v1.0.0 Release Hash。最终 Hash 只能由 PS-07 冻结。
