from __future__ import annotations

import json
import subprocess
import sys
import time
from pathlib import Path
from urllib.request import urlopen

ROOT = Path(__file__).resolve().parents[1]
PORT = 17654
BASE_URL = f"http://127.0.0.1:{PORT}"


def fetch(path: str) -> dict | list:
    with urlopen(BASE_URL + path, timeout=5) as response:
        return json.loads(response.read().decode("utf-8"))


def wait_for_server() -> None:
    for _ in range(40):
        try:
            payload = fetch("/health")
            if payload.get("healthy") is True:
                return
        except Exception:
            time.sleep(0.25)
    raise RuntimeError("bridge server did not become healthy in time")


def main() -> int:
    command = [
        "dotnet",
        "run",
        "--project",
        str(ROOT / "mod" / "Sts2Mod.StateBridge.Host"),
        "--",
        "--port",
        str(PORT),
        "--game-version",
        "prototype",
    ]
    process = subprocess.Popen(command, cwd=ROOT)
    try:
        wait_for_server()
        health = fetch("/health")
        assert health["protocol_version"] == "0.1.0"
        for phase in ("combat", "reward", "map", "terminal"):
            snapshot = fetch(f"/snapshot?phase={phase}")
            actions = fetch(f"/actions?phase={phase}")
            assert snapshot["phase"] == phase
            assert snapshot["compatibility"]["provider_mode"] == "fixture"
            if phase == "terminal":
                assert snapshot["terminal"] is True
                assert actions == []
            else:
                assert snapshot["terminal"] is False
                assert len(actions) >= 1
        print("mod bridge validation passed")
        return 0
    finally:
        process.terminate()
        try:
            process.wait(timeout=5)
        except subprocess.TimeoutExpired:
            process.kill()
            process.wait(timeout=5)


if __name__ == "__main__":
    sys.exit(main())
