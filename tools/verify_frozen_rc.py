from __future__ import annotations

import hashlib
import json
import sys
import zipfile
from pathlib import Path


EXPECTED_FILES = {
    "BatchRenamer-v1.0.0-win-x64.exe",
    "BatchRenamer-v1.0.0-win-x64-portable.zip",
    "SHA256SUMS.txt",
    "RELEASE_NOTES_v1.0.0.md",
    "RELEASE_MANIFEST.json",
}
EXPECTED_ZIP_FILES = {
    "BatchRenamer/BatchRenamer.exe",
    "BatchRenamer/LICENSE",
    "BatchRenamer/THIRD_PARTY_NOTICES.txt",
    "BatchRenamer/licenses/WPF-UI-4.3.0-LICENSE.md",
    "BatchRenamer/licenses/WPF-UI.Abstractions-4.3.0-LICENSE.md",
}
ICON_SHA256 = "467DF074F455504261CE35B7B8F0B5494A575DB8EEE41084F8515F3AB97306D1"
APACHE_LICENSE_SHA256 = "CFC7749B96F63BD31C3C42B5C471BF756814053E847C10F3EB003417BC523D30"
WPF_LICENSE_SHA256 = "EFB68DBCCB1BE73CD78729B76F39720132126BACFF8194EED934323BAB6455B7"
WPF_NOTICES_SHA256 = "871E788E025383423FAE377A97229DAFCB9254687CE917D63EBBF7B10F34C588"


def sha256_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest().upper()


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest().upper()


def require(condition: bool, message: str) -> None:
    if not condition:
        raise RuntimeError(message)


def main() -> int:
    repo = Path(__file__).resolve().parents[1]
    rc = Path(sys.argv[1]).resolve() if len(sys.argv) > 1 else repo / "artifacts" / "release" / "v1.0.0-rc1"
    files = {path.name for path in rc.iterdir() if path.is_file()}
    require(files == EXPECTED_FILES, f"RC file set mismatch: {sorted(files)}")

    first = {name: sha256(rc / name) for name in sorted(EXPECTED_FILES)}
    second = {name: sha256(rc / name) for name in sorted(EXPECTED_FILES)}
    require(first == second, "Artifact drift detected during re-read verification.")

    manifest = json.loads((rc / "RELEASE_MANIFEST.json").read_text(encoding="utf-8"))
    expected_manifest = {
        "product": "easy重命名 / BatchRenamer",
        "version": "1.0.0",
        "release_candidate_id": "BatchRenamer-v1.0.0-RC1",
        "build_flavor": "Public",
        "publish_profile": "compact",
        "runtime_identifier": "win-x64",
        "self_contained": True,
        "single_file": True,
        "ready_to_run": False,
        "single_file_compression": True,
        "trimmed": False,
        "icon_sha256": ICON_SHA256,
        "license": "Apache-2.0",
        "public_purity_status": "PASS",
        "smoke_test_status": "PASS (510 passed, 0 skipped)",
        "third_party_notice_status": "PASS",
        "authenticode_status": "UNSIGNED",
    }
    for key, expected in expected_manifest.items():
        require(manifest.get(key) == expected, f"Manifest {key} mismatch: {manifest.get(key)!r}")
    for key in ["source_commit", "dotnet_sdk", "windows_build_environment", "created_at"]:
        require(bool(manifest.get(key)), f"Manifest field is missing: {key}")

    manifest_artifacts = {item["filename"]: item for item in manifest["artifacts"]}
    for name in EXPECTED_FILES - {"RELEASE_MANIFEST.json"}:
        item = manifest_artifacts.get(name)
        require(item is not None, f"Manifest artifact is missing: {name}")
        require(item["size"] == (rc / name).stat().st_size, f"Manifest size mismatch: {name}")
        require(item["sha256"] == first[name], f"Manifest SHA256 mismatch: {name}")

    sums = {}
    for line in (rc / "SHA256SUMS.txt").read_text(encoding="ascii").splitlines():
        digest, name = line.split("  ", 1)
        sums[name] = digest
    for name in ["BatchRenamer-v1.0.0-win-x64.exe", "BatchRenamer-v1.0.0-win-x64-portable.zip"]:
        require(sums.get(name) == first[name], f"SHA256SUMS mismatch: {name}")

    zip_path = rc / "BatchRenamer-v1.0.0-win-x64-portable.zip"
    with zipfile.ZipFile(zip_path) as archive:
        zip_files = {item.filename for item in archive.infolist() if not item.is_dir()}
        require(zip_files == EXPECTED_ZIP_FILES, f"ZIP contents mismatch: {sorted(zip_files)}")
        require(
            sha256_bytes(archive.read("BatchRenamer/BatchRenamer.exe"))
            == first["BatchRenamer-v1.0.0-win-x64.exe"],
            "Standalone and portable EXE bytes differ.",
        )
        require(sha256_bytes(archive.read("BatchRenamer/LICENSE")) == APACHE_LICENSE_SHA256, "LICENSE mismatch")
        require(
            sha256_bytes(archive.read("BatchRenamer/THIRD_PARTY_NOTICES.txt")) == WPF_NOTICES_SHA256,
            "Third-party notices mismatch",
        )
        for name in [
            "BatchRenamer/licenses/WPF-UI-4.3.0-LICENSE.md",
            "BatchRenamer/licenses/WPF-UI.Abstractions-4.3.0-LICENSE.md",
        ]:
            require(sha256_bytes(archive.read(name)) == WPF_LICENSE_SHA256, f"Upstream license mismatch: {name}")
        require(not any(name.lower().endswith((".ttf", ".otf", ".ttc")) for name in zip_files), "Font file found")

    for name in sorted(EXPECTED_FILES):
        print(f"{name} {(rc / name).stat().st_size} {first[name]}")
    print("ZIP_STRUCTURE=PASS")
    print("SHA256SUMS_SELF_VERIFY=PASS")
    print("RE_READ_VERIFICATION=PASS")
    print("RC_FREEZE=PASS")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
