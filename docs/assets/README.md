# Product Asset Status

本目录只接收真实、已批准的产品资产。不使用空白图片、HTML mockup、AI 假截图或临时图标占位。

| Reserved path | Status | Acceptance boundary |
| --- | --- | --- |
| `main-window.png` | `SCREENSHOT_STATUS = PENDING` | 真实 Release-Public UI；8–15 个中性示例文件；清晰 before → after；无私人路径；主表格和规则区可见 |
| `demo.gif` | `DEMO_GIF_STATUS = PENDING` | 真实应用录制；8–15 秒；Import → Rule → Preview → Execute → Success |
| `icon-512.png` | `ICON_ASSET_STATUS = PENDING` | 仅由用户批准的正式品牌资产导出 |

Windows 应用图标的最终 `.ico` 接入边界见 [PS-03 Release Identity](../PS03_RELEASE_IDENTITY.md)。
