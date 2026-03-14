from __future__ import annotations

import json
import subprocess
import sys
import time
from pathlib import Path
from urllib.error import HTTPError
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


def resolve_host_dll() -> Path:
    candidates = sorted(
        (ROOT / "mod" / "Sts2Mod.StateBridge.Host" / "bin" / "Debug").glob("net*/Sts2Mod.StateBridge.Host.dll"),
        reverse=True,
    )
    if not candidates:
        raise FileNotFoundError("could not find Sts2Mod.StateBridge.Host.dll; build the solution first")
    return candidates[0]


def post_json(path: str, payload: dict) -> tuple[int, dict]:
    import urllib.request

    request = urllib.request.Request(
        BASE_URL + path,
        data=json.dumps(payload).encode("utf-8"),
        headers={"Content-Type": "application/json"},
        method="POST",
    )
    try:
        with urlopen(request, timeout=5) as response:
            return response.status, json.loads(response.read().decode("utf-8"))
    except HTTPError as ex:
        return ex.code, json.loads(ex.read().decode("utf-8"))


def main() -> int:
    command = [
        "dotnet",
        str(resolve_host_dll()),
        "--port",
        str(PORT),
        "--game-version",
        "prototype",
        "--read-only",
        "false",
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
        combat_snapshot = fetch("/snapshot?phase=combat")
        combat_actions = fetch("/actions?phase=combat")
        status_code, apply_response = post_json(
            "/apply",
            {
                "decision_id": combat_snapshot["decision_id"],
                "action_id": combat_actions[0]["action_id"],
                "params": {},
            },
        )
        assert status_code == 200
        assert apply_response["status"] == "accepted"
        reward_snapshot = fetch("/snapshot")
        assert reward_snapshot["phase"] == "reward"

        # Simulate reward chain: choose a reward, then select a concrete card reward.
        reward_actions = fetch("/actions")
        choose_reward = next(action for action in reward_actions if action["type"] == "choose_reward")
        status_code, apply_response = post_json(
            "/apply",
            {
                "decision_id": reward_snapshot["decision_id"],
                "action_id": choose_reward["action_id"],
                "params": {},
            },
        )
        assert status_code == 200
        assert apply_response["status"] == "accepted"
        card_snapshot = fetch("/snapshot")
        assert card_snapshot["phase"] == "reward"
        assert card_snapshot["metadata"]["window_kind"] == "reward_card_selection"
        assert card_snapshot["metadata"]["reward_subphase"] == "card_reward_selection"
        card_actions = fetch("/actions")
        assert any(action["type"] == "choose_reward" for action in card_actions)
        assert any(action["type"] == "skip_reward" for action in card_actions)

        choose_card = next(action for action in card_actions if action["type"] == "choose_reward")
        status_code, apply_response = post_json(
            "/apply",
            {
                "decision_id": card_snapshot["decision_id"],
                "action_id": choose_card["action_id"],
                "params": {},
            },
        )
        assert status_code == 200
        assert apply_response["status"] == "accepted"
        map_snapshot = fetch("/snapshot")
        assert map_snapshot["phase"] == "map"

        status_code, stale_response = post_json(
            "/apply",
            {
                "decision_id": combat_snapshot["decision_id"],
                "action_id": combat_actions[0]["action_id"],
                "params": {},
            },
        )
        assert status_code == 409
        assert stale_response["status"] == "rejected"
        assert stale_response["error_code"] == "stale_decision"
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
