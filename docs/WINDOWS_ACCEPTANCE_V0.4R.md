# V0.4R1 Windows 实机验收（简化版）

> 本版不会修改文件。以下测试只检查 UI、预览和只读校验。

## 1. 先看基础 UI

1. VS2026 打开 `BatchRenamer.UIPrototype.sln`。
2. `生成 → 重新生成解决方案`。
3. F5。
4. 7 条演示数据应立即正常显示。

## 2. 检查三个下拉框

依次点击：

- `原名称`
- `位置`
- `大小写转换`

要求：

- 每次都能正常展开；
- 能点击选项；
- 再次点击/点击外部可以正常关闭；
- 键盘上下键也能选择。

## 3. 搜索框

点击“搜索文件…”输入任意文字。

要求：

- Placeholder 消失；
- 光标和输入文字位于同一基线；
- 不应再出现提示文字与 caret 横向/纵向错位。

## 4. 查找替换

先设置：

- `基础名称`：清空
- `原名称`：选择“放在基础名称后”
- `连续编号`：关闭
- `查找`：`IMG_`
- `替换为`：`Photo_`

例如原文件 `IMG_000001.JPG`，预览应得到：

```text
Photo_000001.JPG
```

然后把 `原名称`改回“不保留”。

要求：

- 查找/替换输入框自动变灰不可编辑；
- 下方明确提示为什么当前功能不生效。

## 5. 大小写转换

设置：

- `原名称`：不保留
- `基础名称`：`HELLO_WORLD`
- `连续编号`：关闭
- `大小写转换`：单词首字母大写

预览应为：

```text
Hello_World.JPG
```

扩展名 `.JPG` 不应被大小写规则修改。

## 6. Windows 非法字符

为了避免编号把 `CON` 变成合法的 `CON_001`，先统一设置：

- 原名称：不保留
- 连续编号：关闭
- 前缀/后缀：清空

然后分别输入基础名称：

### `CON`

应变红，状态：`保留名称`。

### `A?B`

应变红，状态：`含非法字符`。

### `A*B`

应变红，状态：`含非法字符`。

### `A&B`

**不应**因为 `&` 报非法字符。`&` 在 Windows 文件名中是合法字符。

## 7. 重复目标

1. 保持连续编号关闭。
2. 至少让两个同扩展名文件得到相同基础名称。

两个文件都应显示：`目标重名`。

## 8. 大数据回归

按：

```text
Ctrl + Shift + T
```

加载 20,000 项。

要求：

- UI 仍可滚动和输入；
- 滚动条 Thumb 仍能抓取；
- 搜索、大小写、编号变化不会出现 V0.1 那种持续卡死。

## 9. 不要求你人工测试的项目

以下由 Core SmokeTests/后续工程测试负责，不需要普通体验测试时手工理解：

- A↔B VacatingSourceSet；
- FileIdentity Changed；
- case-sensitive directory；
- Parent/Child transaction restriction；
- hard-link namespace identity。

如需运行自动测试：

```powershell
dotnet run --project .\tools\BatchRenamer.Core.SmokeTests\BatchRenamer.Core.SmokeTests.csproj
```
