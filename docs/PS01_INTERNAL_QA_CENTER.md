# PS-01 Internal QA Center

仅 `Release-Internal` 编译 Internal QA Center。按 `Shift+Ctrl+P` 打开。

## 功能

- Quick Smoke：在 `%TEMP%\BatchRenamer\InternalQA` 创建 8 个真实文件并通过正式导入 API 加入主列表。
- Demo Data：复用原 `Shift+Ctrl+D` 实现。
- 20k Preview：复用原 `Shift+Ctrl+T` 实现。
- 事务准备检查：复用正式 Validation、RenamePlanner、Plan Persistence 和 Preflight。
- 2k Real File Stress：复制现有 `python tools\run_release_stress.py --quick` 命令，并读取其最新结构化 JSON 报告。
- Workspace：支持 Open、Reset、Cleanup。

## Workspace 安全合同

- 根目录固定为 `%TEMP%\BatchRenamer\InternalQA`，不接受用户输入路径。
- 创建时写入 `.easyrenamer-internal-qa-owned` ownership marker。
- Cleanup 同时校验固定路径、marker 和 reparse point；任何一项异常都拒绝删除。
- Cleanup 只删除 QA 自有沙箱，不操作用户选择目录。

## 验证命令

```powershell
dotnet build BatchRenamer.UIPrototype.sln -c Release-Internal -p:TreatWarningsAsErrors=true
dotnet build BatchRenamer.UIPrototype.sln -c Release-Public -p:TreatWarningsAsErrors=true
dotnet run --no-build -c Release-Public --project tools\BatchRenamer.Core.SmokeTests\BatchRenamer.Core.SmokeTests.csproj
python tools\run_release_stress.py --quick
python tools\publish_portable.py --flavor public
powershell -ExecutionPolicy Bypass -File tools\verify_public_build.ps1
```
