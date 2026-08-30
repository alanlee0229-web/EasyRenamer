# PS-00 双构建 Flavor

同一源码和同一 commit 支持两个 Release 优化构建：

```powershell
dotnet build BatchRenamer.UIPrototype.sln -c Release-Internal
dotnet build BatchRenamer.UIPrototype.sln -c Release-Public
```

## Release-Internal

- 产品身份：`easy重命名 — INTERNAL TEST`
- 保留 `Shift+Ctrl+P` 事务准备诊断。
- 保留 `Shift+Ctrl+D` 少量演示数据和 `Shift+Ctrl+T` 20,000 项预览压力数据。
- 2,000 个真实文件日常压力测试：

```powershell
python tools\run_release_stress.py --quick
```

发布：

```powershell
python tools\publish_portable.py --flavor internal
```

## Release-Public

- 产品身份：`easy重命名`
- `src/BatchRenamer.App/InternalTools` 在 MSBuild 编译项层面排除。
- 不注册内部快捷键，不生成测试数据，不打包内部资源。

发布及纯净度检查：

```powershell
python tools\publish_portable.py --flavor public
powershell -ExecutionPolicy Bypass -File tools\verify_public_build.ps1
```

`BatchRenamerBuildFlavor=Internal|Public` 仍可作为显式 MSBuild 属性使用；默认值为 `Public`。
