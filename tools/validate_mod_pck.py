from __future__ import annotations

import subprocess
import sys
from pathlib import Path

from build_mod_pck import ROOT, discover_godot_exe, run_godot_script

CHECK_SCRIPT = ROOT / "tools" / "godot_check_pck.gd"


def resolve_mod_output_dir() -> Path:
    candidates = sorted((ROOT / "mod" / "Sts2Mod.StateBridge" / "bin" / "Debug").glob("net*/mod"), reverse=True)
    if not candidates:
        raise FileNotFoundError("could not find a built mod output directory; build the solution first")
    return candidates[0]


def main() -> int:
    output_dir = resolve_mod_output_dir()
    pck_path = output_dir / "Sts2Mod.StateBridge.pck"
    dll_path = output_dir / "Sts2Mod.StateBridge.dll"
    manifest_path = output_dir / "mod_manifest.json"

    for required_path in (pck_path, dll_path, manifest_path):
        if not required_path.exists():
            raise FileNotFoundError(f"required mod artifact is missing: {required_path}")

    godot_exe = discover_godot_exe()
    result: subprocess.CompletedProcess[str] = run_godot_script(
        godot_exe,
        CHECK_SCRIPT,
        [str(pck_path), "res://mod_manifest.json"],
    )
    if result.returncode != 0:
        raise RuntimeError(
            "Failed to validate .pck contents.\n"
            f"STDOUT:\n{result.stdout}\n"
            f"STDERR:\n{result.stderr}"
        )

    print(f"mod pck validation passed: {pck_path}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
