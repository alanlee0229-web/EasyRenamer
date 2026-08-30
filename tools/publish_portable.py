from __future__ import annotations

import argparse
import hashlib
import shutil
import subprocess
import sys
import zipfile
from pathlib import Path


def sha256(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest().upper()


def main() -> int:
    parser = argparse.ArgumentParser(description="Build BatchRenamer self-contained win-x64 portable release.")
    parser.add_argument("--flavor", choices=("public", "internal"), default="public", help="Build flavor (default: public).")
    parser.add_argument("--no-zip", action="store_true", help="Do not create the portable ZIP.")
    parser.add_argument("--allow-warnings", action="store_true", help="Do not promote compiler warnings to errors (diagnostic only; not for release).")
    args = parser.parse_args()

    repo = Path(__file__).resolve().parents[1]
    project = repo / "src" / "BatchRenamer.App" / "BatchRenamer.App.csproj"
    flavor = args.flavor
    configuration = "Release-Public" if flavor == "public" else "Release-Internal"
    output = repo / "artifacts" / "portable" / flavor / "win-x64"
    package_dir = repo / "artifacts" / "packages"
    zip_name = "BatchRenamer-v1.0.0-win-x64.zip" if flavor == "public" else "BatchRenamer-v1.0.0-internal-win-x64.zip"
    zip_path = package_dir / zip_name

    if output.exists():
        shutil.rmtree(output)
    output.mkdir(parents=True, exist_ok=True)
    package_dir.mkdir(parents=True, exist_ok=True)

    cmd = [
        "dotnet", "publish", str(project),
        "-c", configuration,
        "-r", "win-x64",
        "--self-contained", "true",
        "-p:PublishSingleFile=true",
        "-p:PublishTrimmed=false",
        "-p:PublishReadyToRun=false",
        "-p:IncludeNativeLibrariesForSelfExtract=true",
        "-p:EnableCompressionInSingleFile=true",
        "-p:DebugType=None",
        "-p:DebugSymbols=false",
        "-o", str(output),
    ]
    if not args.allow_warnings:
        cmd.insert(-2, "-p:TreatWarningsAsErrors=true")

    print("[publish]", " ".join(cmd))
    try:
        subprocess.run(cmd, check=True, cwd=repo)
    except FileNotFoundError:
        print("ERROR: dotnet was not found. Run this on the Windows/.NET 10 development machine.", file=sys.stderr)
        return 2
    except subprocess.CalledProcessError as exc:
        print(f"ERROR: dotnet publish failed with exit code {exc.returncode}.", file=sys.stderr)
        return exc.returncode or 1

    exe = output / "BatchRenamer.exe"
    if not exe.is_file():
        print(f"ERROR: expected portable executable was not produced: {exe}", file=sys.stderr)
        return 3

    files = sorted(p for p in output.rglob("*") if p.is_file())
    print(f"[ok] BatchRenamer.exe: {exe.stat().st_size / (1024 * 1024):.1f} MiB")
    print(f"[ok] SHA256: {sha256(exe)}")
    if len(files) == 1:
        print("[ok] publish output is a single executable.")
    else:
        print("[info] publish output contains additional files; they will be included in the ZIP:")
        for f in files:
            if f != exe:
                print("  -", f.relative_to(output))

    if not args.no_zip:
        if zip_path.exists():
            zip_path.unlink()
        with zipfile.ZipFile(zip_path, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as zf:
            for f in files:
                zf.write(f, f.relative_to(output))
        print(f"[ok] portable package: {zip_path}")
        print(f"[ok] package SHA256: {sha256(zip_path)}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
