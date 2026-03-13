from __future__ import annotations

import argparse
import os
import shutil
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PACK_SCRIPT = ROOT / "tools" / "godot_pack_mod.gd"


def discover_godot_exe(explicit: str | None = None) -> Path:
    candidates: list[Path] = []
    if explicit:
        candidates.append(Path(explicit))

    for env_name in ("GODOT_CONSOLE_EXE", "GODOT_EXE"):
        value = os.environ.get(env_name)
        if value:
            candidates.append(Path(value))

    for command_name in ("godot_console", "godot"):
        resolved = shutil.which(command_name)
        if resolved:
            candidates.append(Path(resolved))

    local_app_data = os.environ.get("LOCALAPPDATA")
    if local_app_data:
        winget_packages = Path(local_app_data) / "Microsoft" / "WinGet" / "Packages"
        if winget_packages.exists():
            for package_dir in sorted(winget_packages.glob("GodotEngine.GodotEngine_*"), reverse=True):
                candidates.extend(sorted(package_dir.glob("Godot_v*_console.exe"), reverse=True))
                candidates.extend(sorted(package_dir.glob("Godot_v*.exe"), reverse=True))

    seen: set[Path] = set()
    for candidate in candidates:
        resolved = candidate.expanduser()
        if resolved in seen:
            continue
        seen.add(resolved)
        if resolved.exists():
            return resolved.resolve()

    raise FileNotFoundError(
        "Could not locate a Godot editor executable. Install it with "
        "`winget install --id GodotEngine.GodotEngine` or pass --godot-exe / set GODOT_EXE."
    )


def run_godot_script(godot_exe: Path, script_path: Path, script_args: list[str]) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        [str(godot_exe), "--headless", "--script", str(script_path), "--", *script_args],
        cwd=ROOT,
        capture_output=True,
        text=True,
        check=False,
    )


def build_mod_pck(manifest_path: Path, output_path: Path, godot_exe: Path) -> Path:
    manifest_path = manifest_path.resolve()
    output_path = output_path.resolve()

    if not manifest_path.exists():
        raise FileNotFoundError(f"Manifest file does not exist: {manifest_path}")
    if not PACK_SCRIPT.exists():
        raise FileNotFoundError(f"Godot pack script does not exist: {PACK_SCRIPT}")

    output_path.parent.mkdir(parents=True, exist_ok=True)
    if output_path.exists():
        output_path.unlink()

    result = run_godot_script(
        godot_exe,
        PACK_SCRIPT,
        [str(output_path), "res://mod_manifest.json", str(manifest_path)],
    )
    if result.returncode != 0:
        raise RuntimeError(
            "Godot pack command failed.\n"
            f"Godot: {godot_exe}\n"
            f"Manifest: {manifest_path}\n"
            f"Output: {output_path}\n"
            f"STDOUT:\n{result.stdout}\n"
            f"STDERR:\n{result.stderr}"
        )

    if not output_path.exists() or output_path.stat().st_size == 0:
        raise RuntimeError(f"Godot reported success but no .pck was created at {output_path}")

    return output_path


def main() -> int:
    parser = argparse.ArgumentParser(description="Build the minimal STS2 mod .pck package.")
    parser.add_argument("--manifest", required=True, type=Path, help="Path to the generated mod_manifest.json")
    parser.add_argument("--output", required=True, type=Path, help="Path to the output .pck file")
    parser.add_argument("--godot-exe", help="Optional path to the Godot editor executable")
    args = parser.parse_args()

    try:
        godot_exe = discover_godot_exe(args.godot_exe)
        output_path = build_mod_pck(args.manifest, args.output, godot_exe)
    except Exception as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 1

    print(f"Godot executable: {godot_exe}")
    print(f"Packed mod: {output_path}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
