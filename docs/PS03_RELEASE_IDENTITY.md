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

## 正式图标接入边界

当前仓库没有用户批准的正式 `.ico`，因此：

```text
ICON_ASSET_STATUS = PENDING
```

不得用历史预览 PNG 或临时生成图标冒充正式资产。最终资产批准后固定放置于 `src/BatchRenamer.App/Assets/BatchRenamer.ico`，至少包含 16、24、32、48、64、128、256 像素图层；随后在项目文件设置 `ApplicationIcon`，并将同一资源用于 WPF 主窗口 `Icon`，统一 EXE、Explorer、Taskbar、Alt+Tab 与窗口图标。

## Hash 规则

PS-03 产生的 EXE/ZIP SHA256 仅为 qualification artifact，不是最终 v1.0.0 Release Hash。最终 Hash 只能由 PS-07 冻结。
