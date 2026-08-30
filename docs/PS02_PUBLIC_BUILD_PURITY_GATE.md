# PS-02 Public Build Purity Gate

正式 Gate 入口只有一个：

```powershell
powershell -ExecutionPolicy Bypass -File tools\verify_public_build.ps1
```

运行前须已生成 Public 与 Internal 单文件便携构建。默认 Gate 对 Public 执行完整纯净度检查，并将同一 Gate 应用于 Internal 反向样本；只有 Internal 因构建口味被明确拒绝，Negative Control 才为 PASS。

Public 发布命令会自动运行正向纯净度检查：

```powershell
python tools\publish_portable.py --flavor public
```

机器可读报告写入 `artifacts/gates/public_build_purity.json`，该目录不进入 Git。任何未知口味、缺失证据、检查异常或元数据歧义均 fail-closed。
