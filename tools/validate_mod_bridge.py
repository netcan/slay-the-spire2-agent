from __future__ import annotations

import json
import subprocess
import sys
import time
from pathlib import Path
import socket
from urllib.error import HTTPError
from urllib.request import urlopen

ROOT = Path(__file__).resolve().parents[1]
DEFAULT_PORT = 17654


def pick_free_port(preferred: int = DEFAULT_PORT) -> int:
    """Pick a free localhost TCP port.

    Prefer the well-known bridge port when available, but fall back to an ephemeral port
    to avoid collisions with a running in-game bridge.
    """
    for candidate in (preferred, 0):
        with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as sock:
            sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
            try:
                sock.bind(("127.0.0.1", candidate))
            except OSError:
                continue
            return int(sock.getsockname()[1])
    raise RuntimeError("could not allocate a free TCP port")


def fetch(base_url: str, path: str) -> dict | list:
    with urlopen(base_url + path, timeout=5) as response:
        return json.loads(response.read().decode("utf-8"))


def wait_for_server(base_url: str) -> None:
    for _ in range(40):
        try:
            payload = fetch(base_url, "/health")
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


def post_json(base_url: str, path: str, payload: dict) -> tuple[int, dict]:
    import urllib.request

    request = urllib.request.Request(
        base_url + path,
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
    port = pick_free_port()
    base_url = f"http://127.0.0.1:{port}"
    command = [
        "dotnet",
        str(resolve_host_dll()),
        "--port",
        str(port),
        "--game-version",
        "prototype",
        "--read-only",
        "false",
    ]
    process = subprocess.Popen(command, cwd=ROOT)
    try:
        wait_for_server(base_url)
        health = fetch(base_url, "/health")
        assert health["protocol_version"] == "0.1.0"
        for phase in ("combat", "reward", "map", "terminal"):
            snapshot = fetch(base_url, f"/snapshot?phase={phase}")
            actions = fetch(base_url, f"/actions?phase={phase}")
            assert snapshot["phase"] == phase
            assert snapshot["compatibility"]["provider_mode"] == "fixture"
            if phase == "terminal":
                assert snapshot["terminal"] is True
                assert actions == []
            else:
                assert snapshot["terminal"] is False
                assert len(actions) >= 1
        combat_snapshot = fetch(base_url, "/snapshot?phase=combat")
        assert combat_snapshot["player"]["hand"][0]["description"]
        assert combat_snapshot["player"]["hand"][0]["description_rendered"]
        assert combat_snapshot["player"]["hand"][0]["description_vars"]
        assert combat_snapshot["player"]["hand"][0]["glossary"]
        assert combat_snapshot["player"]["powers"][0]["name"]
        assert combat_snapshot["player"]["powers"][0]["description_vars"]
        assert combat_snapshot["enemies"][0]["intent_type"]
        assert combat_snapshot["enemies"][0]["powers"][0]["name"]
        assert combat_snapshot["enemies"][0]["powers"][0]["glossary"]
        assert combat_snapshot["run_state"]["act"] == 1
        assert combat_snapshot["run_state"]["map"]["reachable_nodes"]
        combat_actions = fetch(base_url, "/actions?phase=combat")
        status_code, apply_response = post_json(
            base_url,
            "/apply",
            {
                "decision_id": combat_snapshot["decision_id"],
                "action_id": combat_actions[0]["action_id"],
                "params": {},
            },
        )
        assert status_code == 200
        assert apply_response["status"] == "accepted"
        reward_snapshot = fetch(base_url, "/snapshot")
        assert reward_snapshot["phase"] == "reward"

        # Simulate reward chain: choose a reward, then select a concrete card reward.
        reward_actions = fetch(base_url, "/actions")
        choose_reward = next(action for action in reward_actions if action["type"] == "choose_reward")
        status_code, apply_response = post_json(
            base_url,
            "/apply",
            {
                "decision_id": reward_snapshot["decision_id"],
                "action_id": choose_reward["action_id"],
                "params": {},
            },
        )
        assert status_code == 200
        assert apply_response["status"] == "accepted"
        card_snapshot = fetch(base_url, "/snapshot")
        assert card_snapshot["phase"] == "reward"
        assert card_snapshot["metadata"]["window_kind"] == "reward_card_selection"
        assert card_snapshot["metadata"]["reward_subphase"] == "card_reward_selection"
        card_actions = fetch(base_url, "/actions")
        assert any(action["type"] == "choose_reward" for action in card_actions)
        assert any(action["type"] == "skip_reward" for action in card_actions)

        choose_card = next(action for action in card_actions if action["type"] == "choose_reward")
        status_code, apply_response = post_json(
            base_url,
            "/apply",
            {
                "decision_id": card_snapshot["decision_id"],
                "action_id": choose_card["action_id"],
                "params": {},
            },
        )
        assert status_code == 200
        assert apply_response["status"] == "accepted"
        map_snapshot = fetch(base_url, "/snapshot")
        assert map_snapshot["phase"] == "map"

        status_code, stale_response = post_json(
            base_url,
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
