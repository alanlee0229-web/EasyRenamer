from __future__ import annotations

import argparse
import subprocess
import sys
from pathlib import Path


def main() -> int:
    parser = argparse.ArgumentParser(description="Run the Windows V1 release-scale real rename/undo stress audit.")
    parser.add_argument("--count", type=int, default=20000, help="Number of real sandbox files (default: 20000).")
    parser.add_argument("--quick", action="store_true", help="Use 2000 files for a short diagnostic run.")
    parser.add_argument("--keep", action="store_true", help="Keep the TEMP sandbox after a successful run.")
    parser.add_argument("--configuration", default="Release-Internal", help="Build configuration (default: Release-Internal).")
    args = parser.parse_args()

    repo = Path(__file__).resolve().parents[1]
    project = repo / "tools" / "BatchRenamer.ReleaseStressTests" / "BatchRenamer.ReleaseStressTests.csproj"
    report_dir = repo / "artifacts" / "stress"
    report_dir.mkdir(parents=True, exist_ok=True)

    build = [
        "dotnet", "build", str(project), "-c", args.configuration,
        "-p:TreatWarningsAsErrors=true",
    ]
    print("[build]", " ".join(build))
    try:
        subprocess.run(build, cwd=repo, check=True)
    except FileNotFoundError:
        print("ERROR: dotnet was not found. Run on the Windows/.NET 10 development machine.", file=sys.stderr)
        return 2
    except subprocess.CalledProcessError as exc:
        print(f"ERROR: strict Release build failed with exit code {exc.returncode}.", file=sys.stderr)
        return exc.returncode or 1

    count = 2000 if args.quick else args.count
    run = [
        "dotnet", "run", "--no-build", "-c", args.configuration,
        "--project", str(project), "--",
        "--count", str(count),
    ]
    if args.keep:
        run.append("--keep")
    print("[stress]", " ".join(run))
    try:
        completed = subprocess.run(run, cwd=repo)
        return completed.returncode
    except KeyboardInterrupt:
        print("Interrupted. The stress executable preserves its sandbox on failure/interruption when it can finalize normally.", file=sys.stderr)
        return 130


if __name__ == "__main__":
    raise SystemExit(main())
