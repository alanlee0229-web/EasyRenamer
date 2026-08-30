# PS-06 Public RC / Release Packaging

## 状态

```text
PS-06 = PASS
RC ID = BatchRenamer-v1.0.0-RC1
SOURCE COMMIT = a42b5a887a7e79580473291614fac3a64825d3ab
PUBLIC PROFILE = COMPACT
PS-07 INPUT = artifacts/release/v1.0.0-rc1
```

## 发布门禁

- Release-Public strict build：PASS，0 warnings / 0 errors。
- Full SmokeTests：510 PASS / 0 Skip。
- Public Build Purity：PASS。
- Negative Control：PASS；Internal artifact 被 BUILD_FLAVOR 正确拒绝，exit code 1。
- Canonical publish：`python tools\publish_portable.py --flavor public`；COMPACT、win-x64、self-contained、single-file、compression enabled、ReadyToRun false、trimming false。
- Public identity：`easy重命名 / BatchRenamer`；EXE `BatchRenamer.exe`；FileVersion `1.0.0.0`；ProductVersion `1.0.0`；BuildFlavor `Public`。
- Approved ICO：SHA256 `467DF074F455504261CE35B7B8F0B5494A575DB8EEE41084F8515F3AB97306D1`；PE 内 16、24、32、48、64、128、256 像素 frame 与权威 ICO 逐字节一致。
- Authenticode：UNSIGNED；PS-06 未生成自签名证书，也未修改 EXE。

## 包装

Portable ZIP 精确包含：

```text
BatchRenamer/BatchRenamer.exe
BatchRenamer/LICENSE
BatchRenamer/THIRD_PARTY_NOTICES.txt
BatchRenamer/licenses/WPF-UI-4.3.0-LICENSE.md
BatchRenamer/licenses/WPF-UI.Abstractions-4.3.0-LICENSE.md
```

- `LICENSE` 来自 source commit 中已冻结的 Apache-2.0 官方文本，SHA256 `CFC7749B96F63BD31C3C42B5C471BF756814053E847C10F3EB003417BC523D30`。
- `THIRD_PARTY_NOTICES.txt` 原样来自锁定的 WPF-UI 4.3.0 NuGet package，SHA256 `871E788E025383423FAE377A97229DAFCB9254687CE917D63EBBF7B10F34C588`。
- 两个 WPF-UI license 文件原样保留；它们内容相同，SHA256 `EFB68DBCCB1BE73CD78729B76F39720132126BACFF8194EED934323BAB6455B7`。
- ZIP 没有独立 `.ttf`、`.otf` 或 `.ttc` 文件，没有 source、PDB、InternalTools、测试/benchmark/transaction data 或私有路径。
- Standalone versioned EXE 与 canonical `BatchRenamer.exe` 逐字节一致。

## RC Freeze Snapshot

| 文件 | Bytes | SHA256 |
| --- | ---: | --- |
| `BatchRenamer-v1.0.0-win-x64.exe` | 67,855,504 | `7745D6FAFA48ABBE8D2789EE1E2E071D7FA3183F6FECBD5A5B552E4D21690702` |
| `BatchRenamer-v1.0.0-win-x64-portable.zip` | 62,060,279 | `9FA2C33A6D4B3339FD763FB4077F1FD9374DF3DB4846BFFB276808C847D0DECB` |
| `SHA256SUMS.txt` | 205 | `F350CE76D4F1FD861E60E4F27F1879B1ADC295302FE7B310C3BBEB1AC8BADA68` |
| `RELEASE_MANIFEST.json` | 1,912 | `ED6BF4AAED912694A02654F046664D9C34A8E1A3F4D4AEDCDC8869EA1DD8C7D5` |
| `RELEASE_NOTES_v1.0.0.md` | 801 | `FB598ED1791E6EDB61A013E2374B646895F8E57E2A4B1DC8EEB5CC4AD33C65CA` |

`SHA256SUMS_SELF_VERIFY = PASS`；两轮磁盘重读结果一致；`RC_FREEZE = PASS`。

## PS-07 接管保护

> THE FOLLOWING FILES ARE FROZEN INPUTS FOR PS-07. DO NOT REBUILD OR REPACKAGE.

PS-07 必须先运行 `python tools\verify_frozen_rc.py` 重新计算 RC1；任一 byte 或 Manifest 不一致时必须报告 `BLOCKED_ARTIFACT_DRIFT`。不得重新 publish、压 ZIP、改名、签名或修改 metadata / icon / license / notices。

正式 GitHub Release 尚未发布；Draft Release 延后至 PS-07，以避免提前产生 tag side effect。Screenshot 与 Demo GIF 仍为 PENDING；Discussions 仍需用户在 GitHub Settings 中启用。

Production code diff、Transaction Core diff、semantic behavior change、second mutation path 均为 NONE。正式 EXE/ZIP 保持在被 `.gitignore` 排除的本机 artifact 目录，不提交 Git 历史。
