# PS-05 Fast vs Compact Portable Benchmark

## 结论

```text
RECOMMENDED_PUBLIC_PROFILE = COMPACT
```

Compact 是 v1.0.0 的 Canonical Public Publish Profile。默认命令不需要额外参数：

```powershell
python tools\publish_portable.py --flavor public
```

Fast 的 Warm 启动改善稳定且明显，但未压缩 ReadyToRun EXE 达到 156.565 MiB，超过本轮强调的 100 MB 可接受区间，并比 Compact 大 143.31%。ReadyToRun + 压缩的补充探索虽然降至 69.394 MiB，却没有保留启动收益。因此选择 Compact，不把显著体积增长作为默认发布代价。

## 测试环境

- 时间：2026-08-30 16:31 +08:00 起。
- Source commit：`6cc766f9917a276967f45ccd06c7786e49fca8c2`。
- Windows：Windows 11 家庭版中文版，10.0.26200，Build 26200。
- CPU：Intel Core i9-14900HX，24 cores / 32 logical processors。
- RAM：31.8 GiB。
- Storage：WD_BLACK SN7100 2TB，NVMe SSD，Healthy；工作区位于该磁盘。
- .NET SDK：10.0.400；Runtime target：`net10.0-windows / win-x64`。
- Power mode：高性能。
- Security software：Microsoft Defender Antivirus、实时保护、行为监控、NIS 均开启。

## 候选定义

两个候选使用同一 Public 源码、版本、资源、架构和功能。仅以下 Publish properties 不同：

| Property | Compact | Fast |
| --- | ---: | ---: |
| PublishReadyToRun | false | true |
| EnableCompressionInSingleFile | true | false |

共同属性：`Release-Public`、`win-x64`、SelfContained、PublishSingleFile、PublishTrimmed=false、IncludeNativeLibrariesForSelfExtract=true、DebugType=None、DebugSymbols=false、TreatWarningsAsErrors=true。

未引入 trimming，也未修改任何产品 C# 或 Transaction Core。

## Artifact Size

| Profile | EXE | ZIP | 相对 Compact |
| --- | ---: | ---: | ---: |
| Compact | 67,473,196 bytes / 64.347 MiB | 61,739,961 bytes / 58.880 MiB | baseline |
| Fast | 164,170,163 bytes / 156.565 MiB | 66,803,104 bytes / 63.708 MiB | EXE +143.31%；ZIP +8.20% |

## Startup / First-window

采用固定种子 `20260830`，每个 profile 15 次，成对随机交错。边界为 Process launch → 可见主窗口；随后发送无侵入 `WM_NULL`，确认窗口 Dispatcher 可响应。无法可靠清空 Windows 文件缓存，因此第一轮只标记为 first launch / cold-ish，不宣称 true cold start。

### 可见窗口，15 次全部样本（ms）

| Profile | Mean | Median | Std | P90 | Min | Max |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Compact | 1073.718 | 1033.842 | 164.504 | 1083.978 | 984.015 | 1659.329 |
| Fast | 973.815 | 840.644 | 520.597 | 871.568 | 806.362 | 2854.565 |

- Median：Fast 改善 18.69%。
- P90：Fast 改善 19.60%。
- 首轮 cold-ish：Compact 1659.329 ms；Fast 2854.565 ms。单次首轮不用于宣称胜负。

### Warm 14 次（ms）

| Profile | Mean | Median | Std | P90 |
| --- | ---: | ---: | ---: | ---: |
| Compact | 1031.888 | 1032.002 | 29.646 | 1074.079 |
| Fast | 839.475 | 837.797 | 18.441 | 856.177 |

Warm median / p90 分别改善 18.82% / 20.29%。可见后响应 median / p90 分别改善 17.86% / 19.51%。

## Preview 20K

Benchmark Harness 位于 `tools/`，直接调用正式 UI 路径使用的 `PreviewEngine.Build`。每个 profile 15 次、固定种子交错，每次相同 20,000 项和规则，先 warmup 一次。

| Profile | Mean ms | Median ms | Std ms | P90 ms |
| --- | ---: | ---: | ---: | ---: |
| Compact | 17.296 | 17.070 | 1.860 | 19.509 |
| Fast | 15.070 | 15.041 | 1.803 | 17.405 |

Fast 没有 Preview regression；median / p90 分别低 11.89% / 10.78%。该纯计算差异不是最终选择 Fast 的充分条件。

## Memory

每次启动在同一空闲主界面条件下等待稳定，并连续采样 5 次后取当次中位数；下表再取 15 次启动的中位数。

| Profile | Working Set | Private Memory | 相对 Compact |
| --- | ---: | ---: | ---: |
| Compact | 245.91 MiB | 161.80 MiB | baseline |
| Fast | 165.18 MiB | 116.96 MiB | -32.83% / -27.71% |

## 2,000 Real-file Transaction

两个已发布 Harness 各运行一次相同正式链：Planner → Execute → Journal → Startup Gate → Undo → rolled-back Startup Gate → idempotent second Undo。

| Profile | Planner | Execute | Undo | Total | Result | Temp residual |
| --- | ---: | ---: | ---: | ---: | --- | ---: |
| Compact | 30.418 s | 487.317 s | 440.558 s | 1119.620 s | PASS | 0 |
| Fast | 31.606 s | 483.602 s | 406.214 s | 1084.223 s | PASS | 0 |

两次均验证 2,000 个文件、4,000 Execute moves、4,000 Undo moves、8,000 Execute Journal events、精确命名空间恢复、第二次 Undo 零 mutation，并成功删除 TEMP 沙箱。单次 I/O 耗时只记录，不作为性能胜负依据。

## Gates

- Compact strict publish：PASS，0 warnings / 0 errors。
- Fast strict publish：PASS，0 warnings / 0 errors。
- Compact Public Purity：PASS。
- Fast Public Purity：PASS。
- 默认 Compact 最终 strict build：PASS，0 warnings / 0 errors。
- Full SmokeTests：510 PASS / 0 SKIP。
- 默认 Public Publish：PASS；单一 EXE。
- Canonical Public Purity：PASS。
- Internal Negative Control：PASS；Internal artifact 被正确拒绝，exit code 1。

## 决策依据

Fully-qualified Fast 的 Warm startup median / p90 改善约 18.8% / 20.3%，但 EXE 为 156.565 MiB，超过 100 MB 区间且增长 143.31%。补充探索的 ReadyToRun + compression 候选为 69.394 MiB；6 次/组交错试测的 Warm median 为 1195.629 ms，Compact 为 1166.475 ms，约慢 2.50%，没有达到 10% 改善门槛，因此不升级为正式候选。

基于冻结规则，最终选择 Compact。Fast 保留为显式实验参数 `--profile fast`，不作为正式默认。

## Qualification Hashes

- Winning EXE SHA256：`64A6F01B2476327562FBB7AB1BFABAC343957CB97CBBC95AAF0CDF8DBCC4FB38`
- Winning ZIP SHA256：`3EABDD9D958CB870C4D07269440408B846F7C0673F846D880B490DCE378CAE42`
- Status：PS-05 qualification only / NOT FINAL RELEASE HASH。

## 安全与偏差

- Transaction Core files changed：0。
- Semantic change：NONE。
- Second mutation path：NONE。
- 未关闭 Defender；未伪造 true cold start。
- Benchmark EXE、ZIP、CSV 和 JSON 位于 `artifacts/benchmarks/ps05/`，受 `.gitignore` 排除，不提交 Git，不上传 GitHub Release。
