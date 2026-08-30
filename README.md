# easy重命名 / BatchRenamer

> A modern, safe and extensible<br>
> batch renaming toolkit for Windows.

[**Download for Windows**](https://github.com/alanlee0229-web/EasyRenamer/releases)

> v1.0.0 Release is coming. The Releases page is the future official download location; no production binary is published yet.

`SCREENSHOT_STATUS = PENDING` — the reserved path is `docs/assets/main-window.png`. It will contain a real Release-Public screenshot, never a mockup or AI-generated substitute. See the [asset policy](docs/assets/README.md).

- **Fast Preview** — inspect clear before → after names before any file changes.
- **Safe Transactions** — execute through a frozen plan and two-phase transaction.
- **Undo** — restore a completed rename through the durable transaction history.
- **Crash / Startup Recovery** — fail closed when an interrupted transaction needs attention.
- **Portable Windows App** — self-contained, single-file `win-x64` packaging.

**20,000 real-file transaction/undo stress tested.**

## Why BatchRenamer

Batch renaming is easy until collisions, occupied targets, case-only changes, crashes, or interrupted undo operations appear. BatchRenamer keeps preview generation separate from mutation and requires every real rename to pass the same validation, planning, and transaction boundary.

The current v1.0 product focuses on predictable Windows desktop use: import files, configure supported naming rules, review the live preview, execute safely, and undo when the verified transaction state allows it.

## Features

- Live before → after preview.
- Prefix, suffix, retained original name, continuous numbering, literal find/replace, and case conversion.
- Natural sorting and manual ordering before sequence generation.
- Validation for invalid names, reserved Windows names, duplicate targets, occupied targets, and source identity changes.
- Durable two-phase rename with journaled intent/completion records.
- Startup recovery gate and safe automatic rollback only when filesystem evidence is unambiguous.
- Durable undo for eligible completed transactions.
- Public and Internal build flavors with compile-time isolation.
- Portable self-contained Windows x64 publishing.

Future capabilities are listed only in the [Roadmap](docs/ROADMAP.md); they are not presented as current features.

## Safety-first by design

Every mutation must cross this permanent boundary:

```text
Validation
    ↓
RenamePlanner
    ↓
Frozen RenamePlan
    ↓
Transaction
```

The transaction layer journals each move, uses a temporary namespace, re-checks source identity and target occupancy, and refuses unsafe recovery or undo. Future automation or extension work must not bypass this chain.

Read the [Safety Architecture](docs/SAFETY_ARCHITECTURE.md) and [Security Policy](SECURITY.md) before changing planner, transaction, recovery, or undo behavior.

## Portable usage

After the official v1.0.0 Release is published:

1. Open the [Releases page](https://github.com/alanlee0229-web/EasyRenamer/releases).
2. Download the Windows portable ZIP.
3. Extract it to a normal writable folder.
4. Run `BatchRenamer.exe`.

Until PS-07 completes, do not treat local qualification artifacts or hashes as a final Release.

## Performance and qualification evidence

- Release-Public and Release-Internal strict builds complete with warnings promoted to errors.
- The accumulated Windows smoke suite currently contains 510 passing checks with zero skipped checks.
- The release stress gate has completed a 20,000 real-file Planner → Execute → Startup Scan → Undo → idempotence cycle in an isolated temporary workspace.
- The Public Build Purity Gate verifies that Internal QA code, commands, resources, dependencies, identity, and extra files do not enter the Public artifact.

Qualification evidence describes tested engineering state; it is not a claim that v1.0.0 has already been publicly released.

## Roadmap

v1.0 is the current Core Product. Power-user rules, personalization, automation, an extension platform, and intelligent renaming remain planned work. See the versioned [Roadmap](docs/ROADMAP.md).

## Feedback and support

- Bugs: use the **Bug Report** Issue Form.
- Rename, recovery, or undo safety concerns: use **File Safety / Recovery Problem**.
- Windows compatibility: use **Compatibility Problem**.
- Questions and ideas: use [GitHub Discussions](https://github.com/alanlee0229-web/EasyRenamer/discussions) after the repository owner completes the [Discussions setup](docs/DISCUSSIONS_SETUP.md).

Never publish private files, sensitive filenames, or personal directory paths. See [Support](SUPPORT.md) for safe reporting guidance.

## Contributing

Contributions are welcome, but mutation-related changes require proportionate evidence. Start with [Contributing](CONTRIBUTING.md), which defines four change-risk levels and the required PR report.

Developer verification entry points:

```powershell
dotnet build BatchRenamer.UIPrototype.sln -c Release-Public -p:TreatWarningsAsErrors=true
dotnet run --no-build -c Release-Public --project tools\BatchRenamer.Core.SmokeTests\BatchRenamer.Core.SmokeTests.csproj
powershell -ExecutionPolicy Bypass -File tools\verify_public_build.ps1
D:\DATA\tpredict\python.exe tools\validate_repository_docs.py
```

`DEMO_GIF_STATUS = PENDING` — the reserved path is `docs/assets/demo.gif`, sourced only from a real application recording.

`APP_ICON_STATUS = APPROVED_AND_INTEGRATED` — Release-Public and Release-Internal use the same approved Windows icon. See [Release Identity / Branding](docs/PS03_RELEASE_IDENTITY.md). `ICON_ASSET_STATUS = PENDING` applies only to the separate website-facing `icon-512.png`, which is not substituted with an unapproved derivative.

## License

**License: Apache-2.0**

The original work in this repository is licensed under the [Apache License 2.0](LICENSE). Third-party dependencies remain under their respective licenses and are not relicensed by this project.
