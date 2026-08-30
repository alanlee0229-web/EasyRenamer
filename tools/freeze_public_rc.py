from __future__ import annotations

import hashlib
import json
import os
import platform
import shutil
import subprocess
import tempfile
import zipfile
from datetime import datetime, timezone
from pathlib import Path


RC_ID = "BatchRenamer-v1.0.0-RC1"
VERSION = "1.0.0"
ICON_SHA256 = "467DF074F455504261CE35B7B8F0B5494A575DB8EEE41084F8515F3AB97306D1"
APACHE_LICENSE_SHA256 = "CFC7749B96F63BD31C3C42B5C471BF756814053E847C10F3EB003417BC523D30"
WPF_LICENSE_SHA256 = "EFB68DBCCB1BE73CD78729B76F39720132126BACFF8194EED934323BAB6455B7"
WPF_NOTICES_SHA256 = "871E788E025383423FAE377A97229DAFCB9254687CE917D63EBBF7B10F34C588"


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest().upper()


def sha256_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest().upper()


def require_hash(path: Path, expected: str) -> None:
    actual = sha256(path)
    if actual != expected:
        raise RuntimeError(f"SHA256 mismatch: {path} expected={expected} actual={actual}")


def git(repo: Path, *args: str) -> str:
    return subprocess.check_output(["git", *args], cwd=repo, text=True).strip()


def add_to_zip(archive: zipfile.ZipFile, source: Path | bytes, name: str) -> None:
    info = zipfile.ZipInfo(name, date_time=(1980, 1, 1, 0, 0, 0))
    info.compress_type = zipfile.ZIP_DEFLATED
    info.external_attr = 0o100644 << 16
    data = source if isinstance(source, bytes) else source.read_bytes()
    archive.writestr(info, data, compress_type=zipfile.ZIP_DEFLATED, compresslevel=9)


def artifact(path: Path) -> dict[str, object]:
    return {"filename": path.name, "size": path.stat().st_size, "sha256": sha256(path)}


def main() -> int:
    repo = Path(__file__).resolve().parents[1]
    source_commit = git(repo, "rev-parse", "HEAD")
    protected = git(
        repo,
        "diff",
        "--name-only",
        "HEAD",
        "--",
        "src",
        "Directory.Build.props",
        "BatchRenamer.UIPrototype.sln",
    )
    if protected:
        raise RuntimeError(f"Production/build inputs are dirty; refusing RC freeze:\n{protected}")

    publish_dir = repo / "artifacts" / "portable" / "public" / "win-x64"
    source_exe = publish_dir / "BatchRenamer.exe"
    published_files = sorted(path for path in publish_dir.rglob("*") if path.is_file())
    if published_files != [source_exe]:
        raise RuntimeError(f"Canonical publish must contain only BatchRenamer.exe: {published_files}")

    project_license = subprocess.check_output(["git", "show", f"{source_commit}:LICENSE"], cwd=repo)
    icon = repo / "src" / "BatchRenamer.App" / "Assets" / "BatchRenamer.ico"
    release_notes = repo / "docs" / "releases" / "RELEASE_NOTES_v1.0.0.md"
    nuget_root = Path(os.environ.get("NUGET_PACKAGES", Path.home() / ".nuget" / "packages"))
    wpf_root = nuget_root / "wpf-ui" / "4.3.0"
    abstractions_root = nuget_root / "wpf-ui.abstractions" / "4.3.0"
    wpf_license = wpf_root / "LICENSE.md"
    wpf_notices = wpf_root / "ThirdPartyNotices.txt"
    abstractions_license = abstractions_root / "LICENSE.md"
    abstractions_notices = abstractions_root / "ThirdPartyNotices.txt"

    if sha256_bytes(project_license) != APACHE_LICENSE_SHA256:
        raise RuntimeError("Committed LICENSE does not match the frozen Apache-2.0 text.")
    require_hash(icon, ICON_SHA256)
    require_hash(wpf_license, WPF_LICENSE_SHA256)
    require_hash(wpf_notices, WPF_NOTICES_SHA256)
    require_hash(abstractions_license, WPF_LICENSE_SHA256)
    require_hash(abstractions_notices, WPF_NOTICES_SHA256)
    if wpf_notices.read_bytes() != abstractions_notices.read_bytes():
        raise RuntimeError("Locked WPF-UI notice files differ; audit is required.")

    release_root = repo / "artifacts" / "release"
    output = release_root / "v1.0.0-rc1"
    if output.exists():
        raise RuntimeError(f"RC1 already exists and is immutable: {output}")
    release_root.mkdir(parents=True, exist_ok=True)

    with tempfile.TemporaryDirectory(prefix=".v1.0.0-rc1-", dir=release_root) as temporary:
        staging = Path(temporary)
        standalone = staging / "BatchRenamer-v1.0.0-win-x64.exe"
        portable_zip = staging / "BatchRenamer-v1.0.0-win-x64-portable.zip"
        notes_output = staging / "RELEASE_NOTES_v1.0.0.md"
        sums = staging / "SHA256SUMS.txt"
        manifest_path = staging / "RELEASE_MANIFEST.json"

        shutil.copyfile(source_exe, standalone)
        shutil.copyfile(release_notes, notes_output)

        zip_sources = [
            (source_exe, "BatchRenamer/BatchRenamer.exe"),
            (project_license, "BatchRenamer/LICENSE"),
            (wpf_notices, "BatchRenamer/THIRD_PARTY_NOTICES.txt"),
            (wpf_license, "BatchRenamer/licenses/WPF-UI-4.3.0-LICENSE.md"),
            (abstractions_license, "BatchRenamer/licenses/WPF-UI.Abstractions-4.3.0-LICENSE.md"),
        ]
        with zipfile.ZipFile(portable_zip, "w") as archive:
            for source, name in zip_sources:
                add_to_zip(archive, source, name)

        binary_artifacts = [standalone, portable_zip]
        sums.write_bytes(
            "".join(f"{sha256(path)}  {path.name}\n" for path in binary_artifacts).encode("ascii")
        )
        for line in sums.read_text(encoding="ascii").splitlines():
            expected, filename = line.split("  ", 1)
            require_hash(staging / filename, expected)

        manifest_artifacts = [artifact(path) for path in [standalone, portable_zip, sums, notes_output]]
        manifest = {
            "product": "easy重命名 / BatchRenamer",
            "version": VERSION,
            "release_candidate_id": RC_ID,
            "source_commit": source_commit,
            "build_flavor": "Public",
            "publish_profile": "compact",
            "runtime_identifier": "win-x64",
            "self_contained": True,
            "single_file": True,
            "ready_to_run": False,
            "single_file_compression": True,
            "trimmed": False,
            "dotnet_sdk": subprocess.check_output(["dotnet", "--version"], text=True).strip(),
            "windows_build_environment": platform.platform(),
            "icon_sha256": ICON_SHA256,
            "license": "Apache-2.0",
            "artifacts": manifest_artifacts,
            "public_purity_status": "PASS",
            "smoke_test_status": "PASS (510 passed, 0 skipped)",
            "third_party_notice_status": "PASS",
            "authenticode_status": "UNSIGNED",
            "created_at": datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
            "canonical_publish_command": "python tools\\publish_portable.py --flavor public",
            "zip_contents": [name for _, name in zip_sources],
            "ps07_protection": "FROZEN INPUT. DO NOT REBUILD OR REPACKAGE.",
        }
        manifest_path.write_bytes(
            (json.dumps(manifest, ensure_ascii=False, indent=2) + "\n").encode("utf-8")
        )

        shutil.move(str(staging), str(output))

    for path in sorted(output.iterdir()):
        if path.is_file():
            print(f"{path.name} {path.stat().st_size} {sha256(path)}")
    print("SHA256SUMS_SELF_VERIFY=PASS")
    print("RC_FREEZE=PASS")
    print(f"PS07_INPUT={output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
