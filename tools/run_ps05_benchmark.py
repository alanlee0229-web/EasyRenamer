from __future__ import annotations

import csv
import ctypes
import hashlib
import json
import math
import random
import shutil
import statistics
import subprocess
import sys
import time
import zipfile
from ctypes import wintypes
from pathlib import Path

from portable_profiles import PORTABLE_PROFILES, msbuild_property_args


STARTUP_RUNS = 15
PREVIEW_RUNS = 15
SEED = 20260830
PROFILES = ("compact", "fast")


def run(command: list[str], cwd: Path, *, capture: bool = False) -> subprocess.CompletedProcess[str]:
    print("[run]", subprocess.list2cmdline(command), flush=True)
    return subprocess.run(
        command,
        cwd=cwd,
        check=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        capture_output=capture,
    )


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest().upper()


def percentile(values: list[float], fraction: float) -> float:
    ordered = sorted(values)
    position = (len(ordered) - 1) * fraction
    lower = math.floor(position)
    upper = math.ceil(position)
    if lower == upper:
        return ordered[lower]
    return ordered[lower] + (ordered[upper] - ordered[lower]) * (position - lower)


def stats(values: list[float]) -> dict[str, float]:
    return {
        "mean": statistics.fmean(values),
        "median": statistics.median(values),
        "std": statistics.stdev(values) if len(values) > 1 else 0.0,
        "p90": percentile(values, 0.9),
        "min": min(values),
        "max": max(values),
    }


def balanced_order(repetitions: int, seed_offset: int) -> list[str]:
    rng = random.Random(SEED + seed_offset)
    order: list[str] = []
    for _ in range(repetitions):
        pair = list(PROFILES)
        rng.shuffle(pair)
        order.extend(pair)
    return order


def publish(project: Path, output: Path, profile: str, repo: Path) -> Path:
    if output.exists():
        shutil.rmtree(output)
    output.mkdir(parents=True)
    command = [
        "dotnet", "publish", str(project),
        "-c", "Release-Public",
        "-r", "win-x64",
        "--self-contained", "true",
        *msbuild_property_args(profile),
        "-p:TreatWarningsAsErrors=true",
        "-o", str(output),
    ]
    run(command, repo)
    executables = sorted(output.glob("*.exe"))
    if len(executables) != 1:
        raise RuntimeError(f"Expected one executable in {output}; found {executables}")
    return executables[0]


def make_zip(source: Path, destination: Path) -> None:
    destination.parent.mkdir(parents=True, exist_ok=True)
    if destination.exists():
        destination.unlink()
    with zipfile.ZipFile(destination, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for path in sorted(p for p in source.rglob("*") if p.is_file()):
            archive.write(path, path.relative_to(source))


if sys.platform == "win32":
    user32 = ctypes.WinDLL("user32", use_last_error=True)
    psapi = ctypes.WinDLL("psapi", use_last_error=True)

    class ProcessMemoryCountersEx(ctypes.Structure):
        _fields_ = [
            ("cb", wintypes.DWORD),
            ("PageFaultCount", wintypes.DWORD),
            ("PeakWorkingSetSize", ctypes.c_size_t),
            ("WorkingSetSize", ctypes.c_size_t),
            ("QuotaPeakPagedPoolUsage", ctypes.c_size_t),
            ("QuotaPagedPoolUsage", ctypes.c_size_t),
            ("QuotaPeakNonPagedPoolUsage", ctypes.c_size_t),
            ("QuotaNonPagedPoolUsage", ctypes.c_size_t),
            ("PagefileUsage", ctypes.c_size_t),
            ("PeakPagefileUsage", ctypes.c_size_t),
            ("PrivateUsage", ctypes.c_size_t),
        ]

    EnumWindowsProc = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)
    user32.EnumWindows.argtypes = (EnumWindowsProc, wintypes.LPARAM)
    user32.EnumWindows.restype = wintypes.BOOL
    user32.GetWindowThreadProcessId.argtypes = (wintypes.HWND, ctypes.POINTER(wintypes.DWORD))
    user32.IsWindowVisible.argtypes = (wintypes.HWND,)
    user32.IsWindowVisible.restype = wintypes.BOOL
    user32.SendMessageTimeoutW.argtypes = (
        wintypes.HWND, wintypes.UINT, wintypes.WPARAM, wintypes.LPARAM,
        wintypes.UINT, wintypes.UINT, ctypes.POINTER(ctypes.c_size_t),
    )
    user32.PostMessageW.argtypes = (wintypes.HWND, wintypes.UINT, wintypes.WPARAM, wintypes.LPARAM)
    user32.PostMessageW.restype = wintypes.BOOL
    psapi.GetProcessMemoryInfo.argtypes = (wintypes.HANDLE, ctypes.c_void_p, wintypes.DWORD)
    psapi.GetProcessMemoryInfo.restype = wintypes.BOOL


def visible_window_for_pid(pid: int) -> int | None:
    found: list[int] = []

    @EnumWindowsProc
    def callback(hwnd: int, _lparam: int) -> bool:
        process_id = wintypes.DWORD()
        user32.GetWindowThreadProcessId(hwnd, ctypes.byref(process_id))
        if process_id.value == pid and user32.IsWindowVisible(hwnd):
            found.append(hwnd)
            return False
        return True

    user32.EnumWindows(callback, 0)
    return found[0] if found else None


def is_responsive(hwnd: int) -> bool:
    result = ctypes.c_size_t()
    return bool(user32.SendMessageTimeoutW(hwnd, 0, 0, 0, 0x0002, 250, ctypes.byref(result)))


def memory_bytes(process: subprocess.Popen[bytes]) -> tuple[int, int]:
    counters = ProcessMemoryCountersEx()
    counters.cb = ctypes.sizeof(counters)
    handle = wintypes.HANDLE(int(process._handle))  # type: ignore[attr-defined]
    ok = psapi.GetProcessMemoryInfo(handle, ctypes.byref(counters), counters.cb)
    if not ok:
        raise ctypes.WinError(ctypes.get_last_error())
    return int(counters.WorkingSetSize), int(counters.PrivateUsage)


def startup_sample(executable: Path) -> dict[str, float]:
    started = time.perf_counter()
    process = subprocess.Popen([str(executable)], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    hwnd: int | None = None
    visible_at: float | None = None
    responsive_at: float | None = None
    try:
        deadline = started + 30.0
        while time.perf_counter() < deadline:
            if process.poll() is not None:
                raise RuntimeError(f"Candidate exited during startup with code {process.returncode}")
            hwnd = visible_window_for_pid(process.pid)
            if hwnd is not None:
                visible_at = time.perf_counter()
                break
            time.sleep(0.01)
        if hwnd is None or visible_at is None:
            raise TimeoutError(f"No visible main window within 30 seconds: {executable}")

        while time.perf_counter() < deadline:
            if is_responsive(hwnd):
                responsive_at = time.perf_counter()
                break
            time.sleep(0.01)
        if responsive_at is None:
            raise TimeoutError(f"Main window did not become responsive: {executable}")

        time.sleep(0.75)
        memory_samples = [memory_bytes(process)]
        for _ in range(4):
            time.sleep(0.15)
            memory_samples.append(memory_bytes(process))
        return {
            "visibleMs": (visible_at - started) * 1000.0,
            "responsiveMs": (responsive_at - started) * 1000.0,
            "workingSetBytes": float(statistics.median(x[0] for x in memory_samples)),
            "privateBytes": float(statistics.median(x[1] for x in memory_samples)),
        }
    finally:
        if process.poll() is None and hwnd is not None:
            user32.PostMessageW(hwnd, 0x0010, 0, 0)
        try:
            process.wait(timeout=5)
        except subprocess.TimeoutExpired:
            process.kill()
            process.wait(timeout=5)


def parse_last_json(text: str) -> dict[str, object]:
    for line in reversed(text.splitlines()):
        line = line.strip()
        if line.startswith("{"):
            return json.loads(line)
    raise RuntimeError(f"No JSON object in output: {text[-1000:]}")


def main() -> int:
    if sys.platform != "win32":
        print("ERROR: PS-05 benchmark requires Windows.", file=sys.stderr)
        return 2

    repo = Path(__file__).resolve().parents[1]
    root = repo / "artifacts" / "benchmarks" / "ps05"
    root.mkdir(parents=True, exist_ok=True)
    app_project = repo / "src" / "BatchRenamer.App" / "BatchRenamer.App.csproj"
    preview_project = repo / "tools" / "BatchRenamer.PreviewBenchmark" / "BatchRenamer.PreviewBenchmark.csproj"
    stress_project = repo / "tools" / "BatchRenamer.ReleaseStressTests" / "BatchRenamer.ReleaseStressTests.csproj"
    candidates: dict[str, dict[str, object]] = {}

    for profile in PROFILES:
        profile_root = root / profile
        app = publish(app_project, profile_root / "app", profile, repo)
        preview = publish(preview_project, profile_root / "preview", profile, repo)
        stress = publish(stress_project, profile_root / "stress", profile, repo)
        package = profile_root / f"BatchRenamer-v1.0.0-{profile}-win-x64.zip"
        make_zip(app.parent, package)
        candidates[profile] = {
            "properties": PORTABLE_PROFILES[profile],
            "app": str(app),
            "preview": str(preview),
            "stress": str(stress),
            "exeBytes": app.stat().st_size,
            "zipBytes": package.stat().st_size,
            "exeSha256": sha256(app),
            "zipSha256": sha256(package),
        }
        purity = run([
            "powershell", "-NoProfile", "-ExecutionPolicy", "Bypass",
            "-File", str(repo / "tools" / "verify_public_build.ps1"),
            "-PublishDirectory", str(app.parent),
            "-PositiveOnly",
        ], repo, capture=True)
        candidates[profile]["publicPurity"] = "PUBLIC_BUILD_PURITY = PASS" in purity.stdout

    raw_rows: list[dict[str, object]] = []
    startup: dict[str, list[dict[str, float]]] = {profile: [] for profile in PROFILES}
    startup_order = balanced_order(STARTUP_RUNS, 0)
    for order_index, profile in enumerate(startup_order, 1):
        sample = startup_sample(Path(str(candidates[profile]["app"])))
        sample["run"] = float(len(startup[profile]) + 1)
        startup[profile].append(sample)
        print(f"[startup] {order_index:02d}/{len(startup_order)} {profile}: {sample}", flush=True)
        for metric, unit in (("visibleMs", "ms"), ("responsiveMs", "ms"),
                             ("workingSetBytes", "bytes"), ("privateBytes", "bytes")):
            raw_rows.append({
                "metric": metric,
                "profile": profile,
                "run": len(startup[profile]),
                "order": order_index,
                "value": sample[metric],
                "unit": unit,
                "classification": "first/cold-ish" if len(startup[profile]) == 1 else "warm",
            })
        time.sleep(0.35)

    preview_samples: dict[str, list[float]] = {profile: [] for profile in PROFILES}
    preview_order = balanced_order(PREVIEW_RUNS, 100)
    for order_index, profile in enumerate(preview_order, 1):
        completed = run([
            str(candidates[profile]["preview"]),
            "--items", "20000",
            "--warmup", "1",
        ], repo, capture=True)
        result = parse_last_json(completed.stdout)
        value = float(result["computeMs"])
        preview_samples[profile].append(value)
        print(f"[preview] {order_index:02d}/{len(preview_order)} {profile}: {value:.3f} ms", flush=True)
        raw_rows.append({
            "metric": "preview20kComputeMs",
            "profile": profile,
            "run": len(preview_samples[profile]),
            "order": order_index,
            "value": value,
            "unit": "ms",
            "classification": "warm-core",
        })

    stress_results: dict[str, dict[str, object]] = {}
    for profile in PROFILES:
        report_path = root / profile / "stress-2000.json"
        run([
            str(candidates[profile]["stress"]),
            "--count", "2000",
            "--report", str(report_path),
        ], repo)
        stress_results[profile] = json.loads(report_path.read_text(encoding="utf-8-sig"))
        if not stress_results[profile].get("Success"):
            raise RuntimeError(f"2K stress failed for {profile}: {stress_results[profile]}")

    startup_summary: dict[str, dict[str, object]] = {}
    for profile in PROFILES:
        samples = startup[profile]
        startup_summary[profile] = {
            "firstLaunchColdish": samples[0],
            "visibleAll15": stats([x["visibleMs"] for x in samples]),
            "responsiveAll15": stats([x["responsiveMs"] for x in samples]),
            "visibleWarm14": stats([x["visibleMs"] for x in samples[1:]]),
            "responsiveWarm14": stats([x["responsiveMs"] for x in samples[1:]]),
            "workingSetBytes": stats([x["workingSetBytes"] for x in samples]),
            "privateBytes": stats([x["privateBytes"] for x in samples]),
        }

    summary = {
        "schema": 1,
        "seed": SEED,
        "startupRunsPerProfile": STARTUP_RUNS,
        "previewRunsPerProfile": PREVIEW_RUNS,
        "startupOrder": startup_order,
        "previewOrder": preview_order,
        "candidates": candidates,
        "startup": startup_summary,
        "preview20k": {profile: stats(preview_samples[profile]) for profile in PROFILES},
        "stress2k": stress_results,
    }
    (root / "ps05_summary.json").write_text(json.dumps(summary, ensure_ascii=False, indent=2), encoding="utf-8")
    with (root / "ps05_raw.csv").open("w", newline="", encoding="utf-8-sig") as stream:
        writer = csv.DictWriter(stream, fieldnames=("metric", "profile", "run", "order", "value", "unit", "classification"))
        writer.writeheader()
        writer.writerows(raw_rows)
    print(f"[ok] {root / 'ps05_summary.json'}")
    print(f"[ok] {root / 'ps05_raw.csv'}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
