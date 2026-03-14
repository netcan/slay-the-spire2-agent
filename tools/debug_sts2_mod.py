from __future__ import annotations

import argparse
import csv
import io
import json
import os
import re
import shutil
import subprocess
import sys
import time
from pathlib import Path
from urllib.error import URLError
from urllib.request import urlopen


ROOT = Path(__file__).resolve().parents[1]
DEFAULT_GAME_DIRS = [
    Path(r"F:\SteamLibrary\steamapps\common\Slay the Spire 2"),
    Path(r"C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2"),
    Path(r"C:\Program Files\Steam\steamapps\common\Slay the Spire 2"),
]
GAME_PROCESS_NAME = "SlayTheSpire2.exe"


def discover_game_dir(explicit: str | None = None) -> Path:
    candidates: list[Path] = []
    if explicit:
        candidates.append(Path(explicit))

    env_value = os.environ.get("STS2_GAME_DIR")
    if env_value:
        candidates.append(Path(env_value))

    candidates.extend(DEFAULT_GAME_DIRS)

    for candidate in candidates:
        exe_path = candidate / "SlayTheSpire2.exe"
        if exe_path.exists():
            return candidate.resolve()

    raise FileNotFoundError(
        "Could not locate the Slay the Spire 2 install directory. "
        "Pass --game-dir or set STS2_GAME_DIR."
    )


def resolve_managed_dir(game_dir: Path) -> Path:
    managed_dir = game_dir / "data_sts2_windows_x86_64"
    if not managed_dir.exists():
        raise FileNotFoundError(f"Managed directory does not exist: {managed_dir}")
    return managed_dir


def discover_steam_appid(game_dir: Path) -> str | None:
    steamapps_dir = game_dir.parent.parent
    install_dir_name = game_dir.name
    for manifest_path in steamapps_dir.glob("appmanifest_*.acf"):
        try:
            text = manifest_path.read_text(encoding="utf-8")
        except OSError:
            continue

        install_dir_match = re.search(r'"installdir"\s+"([^"]+)"', text)
        if install_dir_match is None or install_dir_match.group(1) != install_dir_name:
            continue

        match = re.search(r'"appid"\s+"(\d+)"', text)
        if match:
            return match.group(1)

    return None


def ensure_steam_appid_file(game_dir: Path) -> Path | None:
    appid = discover_steam_appid(game_dir)
    if appid is None:
        return None

    appid_path = game_dir / "steam_appid.txt"
    current_value = appid_path.read_text(encoding="utf-8").strip() if appid_path.exists() else ""
    if current_value != appid:
        appid_path.write_text(appid + "\n", encoding="ascii")
    return appid_path


def resolve_mod_output_dir() -> Path:
    candidates = sorted((ROOT / "mod" / "Sts2Mod.StateBridge" / "bin" / "Debug").glob("net*/mod"), reverse=True)
    if not candidates:
        raise FileNotFoundError("Could not find built mod output. Build the solution first.")
    return candidates[0]


def list_running_game_processes() -> list[dict[str, str]]:
    if os.name != "nt":
        return []

    result = subprocess.run(
        ["tasklist", "/FI", f"IMAGENAME eq {GAME_PROCESS_NAME}", "/FO", "CSV", "/NH"],
        capture_output=True,
        text=True,
        check=False,
    )
    if result.returncode != 0 or not result.stdout.strip():
        return []

    reader = csv.reader(io.StringIO(result.stdout))
    processes: list[dict[str, str]] = []
    for row in reader:
        if len(row) < 2:
            continue
        image_name = row[0].strip().strip('"')
        if not image_name or image_name.upper() == "INFO":
            continue
        if image_name.lower() != GAME_PROCESS_NAME.lower():
            continue
        processes.append({"image_name": image_name, "pid": row[1].strip().strip('"')})
    return processes


def kill_running_game(timeout_seconds: float = 15.0) -> list[str]:
    processes = list_running_game_processes()
    if not processes:
        return []

    pids = [process["pid"] for process in processes if process.get("pid")]
    subprocess.run(
        ["taskkill", "/IM", GAME_PROCESS_NAME, "/T", "/F"],
        capture_output=True,
        text=True,
        check=False,
    )

    deadline = time.time() + timeout_seconds
    while time.time() < deadline:
        if not list_running_game_processes():
            return pids
        time.sleep(0.5)

    remaining = ", ".join(process["pid"] for process in list_running_game_processes())
    raise TimeoutError(f"Timed out waiting for {GAME_PROCESS_NAME} to exit. Remaining pids: {remaining}")


def build_mod(game_dir: Path) -> Path:
    managed_dir = resolve_managed_dir(game_dir)
    command = [
        "dotnet",
        "build",
        str(ROOT / "mod" / "Sts2Mod.StateBridge.sln"),
        f"-p:Sts2ManagedDir={managed_dir}",
        f"-p:Sts2ModLoaderDir={managed_dir}",
    ]
    subprocess.run(command, cwd=ROOT, check=True)
    return resolve_mod_output_dir()


def install_mod(game_dir: Path, source_dir: Path | None = None, kill_game: bool = False) -> Path:
    source_dir = source_dir or resolve_mod_output_dir()
    target_dir = game_dir / "mods" / "Sts2Mod.StateBridge"
    target_dir.mkdir(parents=True, exist_ok=True)

    required_files = [
        source_dir / "Sts2Mod.StateBridge.pck",
        source_dir / "Sts2Mod.StateBridge.dll",
        source_dir / "mod_manifest.json",
    ]
    for required_file in required_files:
        if not required_file.exists():
            raise FileNotFoundError(f"Required mod artifact is missing: {required_file}")

    if kill_game:
        killed_pids = kill_running_game()
        if killed_pids:
            print(f"Stopped running Slay the Spire 2 processes: {', '.join(killed_pids)}")

    try:
        for required_file in required_files:
            shutil.copy2(required_file, target_dir / required_file.name)
    except PermissionError as exc:
        if list_running_game_processes():
            raise PermissionError(
                f"Could not overwrite installed mod files because {GAME_PROCESS_NAME} is still running. "
                f"Retry with --kill-game or close the game first. Target: {target_dir}"
            ) from exc
        raise

    return target_dir


def fetch_json(url: str) -> dict:
    with urlopen(url, timeout=3) as response:
        return json.loads(response.read().decode("utf-8"))


def wait_for_bridge(port: int, timeout_seconds: float) -> dict | None:
    deadline = time.time() + timeout_seconds
    url = f"http://127.0.0.1:{port}/health"
    while time.time() < deadline:
        try:
            return fetch_json(url)
        except (URLError, TimeoutError, ValueError, OSError):
            time.sleep(1.0)
    return None


def summarize_runtime_log(log_file: Path) -> list[str]:
    if not log_file.exists():
        return []

    try:
        content = log_file.read_text(encoding="utf-8", errors="ignore")
    except OSError:
        return []

    hints: list[str] = []
    if "No appID found" in content:
        hints.append("Direct launch is missing Steam app id; create steam_appid.txt or launch through Steam.")
    if "Skipping loading mod" in content and "mods warning" in content:
        hints.append("STS2 found the mod pack but skipped loading it because the in-game mods warning has not been acknowledged yet.")
    if "Found mod pck file" not in content:
        hints.append("Game log did not report discovering the installed .pck file; verify the mod directory contents.")
    return hints


def launch_game(
    game_dir: Path,
    port: int,
    enable_writes: bool,
    wait_seconds: float,
    show_game_log: bool = False,
) -> int:
    exe_path = game_dir / "SlayTheSpire2.exe"
    if not exe_path.exists():
        raise FileNotFoundError(f"Game executable does not exist: {exe_path}")

    log_dir = ROOT / "tmp" / "sts2-debug"
    log_dir.mkdir(parents=True, exist_ok=True)
    log_file = log_dir / f"sts2-runtime-{int(time.time())}.log"
    appid_path = ensure_steam_appid_file(game_dir)

    env = os.environ.copy()
    env["STS2_BRIDGE_HOST"] = "127.0.0.1"
    env["STS2_BRIDGE_PORT"] = str(port)
    if enable_writes:
        env["STS2_BRIDGE_ENABLE_WRITES"] = "true"

    popen_kwargs: dict[str, object] = {
        "cwd": game_dir,
        "env": env,
    }
    if not show_game_log:
        popen_kwargs["stdin"] = subprocess.DEVNULL
        popen_kwargs["stdout"] = subprocess.DEVNULL
        popen_kwargs["stderr"] = subprocess.DEVNULL
        if os.name == "nt":
            creationflags = getattr(subprocess, "DETACHED_PROCESS", 0) | getattr(subprocess, "CREATE_NEW_PROCESS_GROUP", 0)
            if creationflags:
                popen_kwargs["creationflags"] = creationflags

    process = subprocess.Popen(
        [str(exe_path), "--log-file", str(log_file), "--verbose"],
        **popen_kwargs,
    )

    print(f"Launched Slay the Spire 2 (pid={process.pid})")
    print(f"Runtime log: {log_file}")
    if appid_path is not None:
        print(f"steam_appid.txt: {appid_path}")
    health = wait_for_bridge(port, wait_seconds)
    if health is None:
        print("Bridge did not respond before timeout. The game may still be starting.")
        for hint in summarize_runtime_log(log_file):
            print(f"Hint: {hint}")
    else:
        print("Bridge health:")
        print(json.dumps(health, ensure_ascii=False, indent=2))

    return process.pid


def command_build(args: argparse.Namespace) -> int:
    game_dir = discover_game_dir(args.game_dir)
    output_dir = build_mod(game_dir)
    print(f"Built mod artifacts: {output_dir}")
    return 0


def command_install(args: argparse.Namespace) -> int:
    game_dir = discover_game_dir(args.game_dir)
    target_dir = install_mod(game_dir, kill_game=args.kill_game)
    print(f"Installed mod to: {target_dir}")
    return 0


def command_debug(args: argparse.Namespace) -> int:
    game_dir = discover_game_dir(args.game_dir)
    output_dir = build_mod(game_dir)
    target_dir = install_mod(game_dir, output_dir, kill_game=args.kill_game)
    print(f"Installed mod to: {target_dir}")
    launch_game(game_dir, args.port, args.enable_writes, args.wait_seconds, show_game_log=args.show_game_log)
    return 0


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Build, install, and debug the STS2 in-game bridge mod.")
    subparsers = parser.add_subparsers(dest="command", required=True)

    for name, handler in (("build", command_build), ("install", command_install)):
        subparser = subparsers.add_parser(name)
        subparser.add_argument("--game-dir", help="Path to the Slay the Spire 2 install directory")
        if name == "install":
            subparser.add_argument("--kill-game", action="store_true", help="Kill running Slay the Spire 2 before copying mod files")
        subparser.set_defaults(handler=handler)

    debug_parser = subparsers.add_parser("debug")
    debug_parser.add_argument("--game-dir", help="Path to the Slay the Spire 2 install directory")
    debug_parser.add_argument("--port", type=int, default=17654, help="Bridge port to expose from the in-game mod")
    debug_parser.add_argument("--enable-writes", action="store_true", help="Enable in-game write actions")
    debug_parser.add_argument("--kill-game", action="store_true", help="Kill running Slay the Spire 2 before reinstalling and relaunching")
    debug_parser.add_argument("--wait-seconds", type=float, default=30.0, help="How long to wait for /health")
    debug_parser.add_argument("--show-game-log", action="store_true", help="Also mirror the game process stdout/stderr into the current terminal")
    debug_parser.set_defaults(handler=command_debug)

    return parser


def main() -> int:
    parser = build_parser()
    args = parser.parse_args()
    return args.handler(args)


if __name__ == "__main__":
    sys.exit(main())
