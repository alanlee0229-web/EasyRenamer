from __future__ import annotations

import argparse
import hashlib
import json
import subprocess
import sys
from datetime import datetime, timezone
from pathlib import Path


def sha256(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest().upper()


def run(label: str, cmd: list[str], cwd: Path) -> None:
    print()
    print(f"===== {label} =====")
    print(" ".join(cmd))
    try:
        subprocess.run(cmd, cwd=cwd, check=True)
    except FileNotFoundError:
        print("ERROR: required executable was not found. Run this gate on the Windows/.NET 10 development machine.", file=sys.stderr)
        raise SystemExit(2)
    except subprocess.CalledProcessError as exc:
        print(f"ERROR: {label} failed with exit code {exc.returncode}.", file=sys.stderr)
        raise SystemExit(exc.returncode or 1)


def main() -> int:
    parser = argparse.ArgumentParser(description="BatchRenamer V1 final Windows release gate.")
    parser.add_argument("--count", type=int, default=20000, help="Real sandbox files for the transaction stress audit (default: 20000).")
    parser.add_argument("--quick", action="store_true", help="Diagnostic-only 2000-file run. Does NOT qualify as the final V1 release gate.")
    parser.add_argument("--keep-stress-sandbox", action="store_true", help="Keep the successful stress sandbox for inspection.")
    args = parser.parse_args()

    repo = Path(__file__).resolve().parents[1]
    solution = repo / "BatchRenamer.UIPrototype.sln"
    smoke_project = repo / "tools" / "BatchRenamer.Core.SmokeTests" / "BatchRenamer.Core.SmokeTests.csproj"
    stress_project = repo / "tools" / "BatchRenamer.ReleaseStressTests" / "BatchRenamer.ReleaseStressTests.csproj"
    publish_script = repo / "tools" / "publish_portable.py"
    count = 2000 if args.quick else args.count

    run("1/4 Strict Release solution build (warnings are errors)", [
        "dotnet", "build", str(solution), "-c", "Release-Public", "-p:TreatWarningsAsErrors=true"
    ], repo)

    run("2/4 Full Release smoke tests", [
        "dotnet", "run", "--no-build", "-c", "Release-Public", "--project", str(smoke_project)
    ], repo)

    stress_cmd = [
        "dotnet", "run", "--no-build", "-c", "Release-Public", "--project", str(stress_project), "--",
        "--count", str(count),
    ]
    if args.keep_stress_sandbox:
        stress_cmd.append("--keep")
    run(f"3/4 Real filesystem transaction stress ({count:,} files)", stress_cmd, repo)

    run("4/4 Strict self-contained portable publish", [
        sys.executable, str(publish_script)
    ], repo)

    exe = repo / "artifacts" / "portable" / "public" / "win-x64" / "BatchRenamer.exe"
    package = repo / "artifacts" / "packages" / "BatchRenamer-v1.0.0-win-x64.zip"
    if not exe.is_file() or not package.is_file():
        print("ERROR: release artifacts are missing after successful publish.", file=sys.stderr)
        return 3

    stress_reports = sorted((repo / "artifacts" / "stress").glob("release-stress-*.json"), key=lambda p: p.stat().st_mtime)
    latest_stress = stress_reports[-1] if stress_reports else None
    if latest_stress is None:
        print("ERROR: stress report was not produced.", file=sys.stderr)
        return 4

    try:
        stress = json.loads(latest_stress.read_text(encoding="utf-8"))
    except Exception as exc:
        print(f"ERROR: stress report is unreadable: {exc}", file=sys.stderr)
        return 4
    if not stress.get("Success") and not stress.get("success"):
        print("ERROR: latest stress report does not record success.", file=sys.stderr)
        return 4

    final_dir = repo / "artifacts" / "release-gate"
    final_dir.mkdir(parents=True, exist_ok=True)
    manifest = {
        "schemaVersion": 1,
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "releaseGateQualified": not args.quick and count >= 20000,
        "stressCount": count,
        "stressReport": str(latest_stress.relative_to(repo)),
        "portableExe": str(exe.relative_to(repo)),
        "portableExeBytes": exe.stat().st_size,
        "portableExeSha256": sha256(exe),
        "portableZip": str(package.relative_to(repo)),
        "portableZipBytes": package.stat().st_size,
        "portableZipSha256": sha256(package),
    }
    manifest_path = final_dir / "V1_RELEASE_GATE_MANIFEST.json"
    manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")

    print()
    print("===== RELEASE GATE PASS =====")
    if args.quick or count < 20000:
        print("PASS (diagnostic only): quick/count<20000 run does not qualify for V1 final freeze.")
    else:
        print("PASS: strict build + full smoke + 20k real transaction/undo stress + strict portable publish.")
    print(f"Manifest: {manifest_path}")
    print(f"EXE SHA256: {manifest['portableExeSha256']}")
    print(f"ZIP SHA256: {manifest['portableZipSha256']}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
