# PS-03B Final App Icon Integration

## 状态

```text
PS-03B = PASS
ICON_ASSET_STATUS = APPROVED_AND_INTEGRATED
```

## 权威资源

- 路径：`src/BatchRenamer.App/Assets/BatchRenamer.ico`
- SHA256：`467DF074F455504261CE35B7B8F0B5494A575DB8EEE41084F8515F3AB97306D1`
- ICONDIR：有效 ICO，7 个 32-bit PNG frame。
- 尺寸：16×16、24×24、32×32、48×48、64×64、128×128、256×256。

批准资产未经修改、重绘、转换或补帧。Git 仓库只保留上述单一权威 ICO，批准输入包不提交。

## Wiring

- `ApplicationIcon` 指向权威 ICO，为 Release-Public 与 Release-Internal 的 EXE / Explorer executable resource 提供同一图标。
- WPF `MainWindow.Icon` 指向同一 Resource，为 Window、Taskbar 与 Alt+Tab 提供同一图标。
- PE 资源验证从 Public/Internal 普通构建 EXE 和 Portable EXE 中读取 `RT_GROUP_ICON/RT_ICON`；四个 EXE 的 7 个 frame 均与批准 ICO 逐字节一致。
- 运行时验证确认 Public/Internal 顶层窗口可见、无 owner、`WM_GETICON` 大/小图标句柄均非零。

## Identity

- Public window：`easy重命名`。
- Public Product / FileDescription：`easy重命名 / BatchRenamer`。
- Public ProductVersion：`1.0.0`；FileVersion：`1.0.0.0`。
- Internal window：`easy重命名 — INTERNAL TEST`。
- Internal Product / FileDescription：`BatchRenamer Internal Test`。
- Internal ProductVersion：`1.0.0-internal`；FileVersion：`1.0.0.0`。
- Internal QA：`InternalQaCenterWindow` 与 `InternalTools_PreviewKeyDown` 保持存在。

## Regression

- Release-Internal strict build：PASS，0 warnings / 0 errors。
- Release-Public strict build：PASS，0 warnings / 0 errors。
- Full SmokeTests：510 PASS / 0 SKIP。
- Public publish：PASS，单一 EXE。
- Public Build Purity：PASS。
- Negative Control：PASS，Internal artifact 被 BUILD_FLAVOR 正确拒绝，exit code 1。
- Transaction Core diff：NONE。
- Semantic behavior change：NONE。
- Second mutation path：NONE。

## Qualification Hashes

- Public EXE SHA256：`43A0BF190D6032CB3552F545CCC0CE31117F06DB1EE06EB98C31F18EBBEC63D1`
- Public ZIP SHA256：`ED5CE8E3072F152F1167046CC4DAA45BF0CDA6C06A044204C9894A545BE2F788`
- Status：PS-03B qualification only / NOT FINAL RELEASE HASH。
