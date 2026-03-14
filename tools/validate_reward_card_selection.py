from __future__ import annotations

import argparse
import json
import os
import time
from datetime import datetime
from pathlib import Path
from typing import Any
from urllib.error import HTTPError, URLError
from urllib.request import Request, urlopen

ROOT = Path(__file__).resolve().parents[1]


def fetch_json(base_url: str, path: str) -> dict[str, Any] | list[Any]:
    with urlopen(base_url + path, timeout=5) as response:
        return json.loads(response.read().decode("utf-8"))


def post_json(base_url: str, path: str, payload: dict[str, Any]) -> tuple[int, dict[str, Any]]:
    request = Request(
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


def write_json(path: Path, payload: Any) -> None:
    path.write_text(json.dumps(payload, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")


def timestamp_slug() -> str:
    return datetime.now().strftime("%Y%m%d-%H%M%S")


def create_artifact_dir(root: Path | None = None) -> Path:
    base = root or (ROOT / "tmp" / "reward-card-selection-validation")
    artifact_dir = base / timestamp_slug()
    artifact_dir.mkdir(parents=True, exist_ok=False)
    return artifact_dir


def parse_bool_env(name: str) -> bool:
    value = os.environ.get(name, "")
    return value.strip().lower() in {"1", "true", "yes", "on"}


def main() -> int:
    parser = argparse.ArgumentParser(description="Validate that reward card selection is exported as phase=reward.")
    parser.add_argument("--bridge-base-url", default="http://127.0.0.1:17654", help="Bridge base URL.")
    parser.add_argument("--artifact-root", help="Override output root for validation artifacts.")
    parser.add_argument("--apply", action="store_true", help="Submit a real POST /apply choose_reward after capture.")
    parser.add_argument(
        "--allow-write",
        action="store_true",
        help="Explicitly acknowledge that a real in-game write may be sent when --apply is used.",
    )
    parser.add_argument(
        "--apply-timeout-seconds",
        type=float,
        default=8.0,
        help="How long to wait for a post-apply window change.",
    )
    args = parser.parse_args()

    artifact_dir = create_artifact_dir(Path(args.artifact_root) if args.artifact_root else None)
    base_url = args.bridge_base_url.rstrip("/")

    try:
        health = fetch_json(base_url, "/health")
        snapshot = fetch_json(base_url, "/snapshot")
        actions = fetch_json(base_url, "/actions")
    except (HTTPError, URLError, OSError, ValueError, TypeError) as exc:
        result = {
            "timestamp": datetime.now().isoformat(),
            "artifact_dir": str(artifact_dir),
            "verdict": "failed",
            "summary": "bridge 当前无法读取 /health 或 /snapshot 或 /actions。",
            "error": repr(exc),
        }
        write_json(artifact_dir / "result.json", result)
        print(json.dumps(result, ensure_ascii=False, indent=2))
        return 1

    write_json(artifact_dir / "health.json", health)
    write_json(artifact_dir / "snapshot.json", snapshot)
    write_json(artifact_dir / "actions.json", actions)

    phase = str(snapshot.get("phase") or "unknown") if isinstance(snapshot, dict) else "unknown"
    metadata = snapshot.get("metadata") if isinstance(snapshot, dict) else None
    metadata = metadata if isinstance(metadata, dict) else {}
    window_kind = str(metadata.get("window_kind") or "")
    reward_subphase = str(metadata.get("reward_subphase") or "")

    if phase != "reward" or reward_subphase != "card_reward_selection":
        result = {
            "timestamp": datetime.now().isoformat(),
            "artifact_dir": str(artifact_dir),
            "verdict": "not_in_reward_card_selection",
            "summary": "当前不在卡牌奖励选择界面，请将游戏停在选牌二级界面后再运行。",
            "phase": phase,
            "window_kind": window_kind,
            "reward_subphase": reward_subphase,
        }
        write_json(artifact_dir / "result.json", result)
        print(json.dumps(result, ensure_ascii=False, indent=2))
        return 2

    if not isinstance(actions, list):
        actions = []

    choose_actions = [action for action in actions if isinstance(action, dict) and action.get("type") == "choose_reward"]
    skip_actions = [action for action in actions if isinstance(action, dict) and action.get("type") == "skip_reward"]
    if not choose_actions:
        result = {
            "timestamp": datetime.now().isoformat(),
            "artifact_dir": str(artifact_dir),
            "verdict": "missing_actions",
            "summary": "已识别 reward_card_selection，但未找到 choose_reward legal actions。",
            "phase": phase,
            "window_kind": window_kind,
            "reward_subphase": reward_subphase,
        }
        write_json(artifact_dir / "result.json", result)
        print(json.dumps(result, ensure_ascii=False, indent=2))
        return 1

    if not args.apply:
        result = {
            "timestamp": datetime.now().isoformat(),
            "artifact_dir": str(artifact_dir),
            "verdict": "captured",
            "summary": "已捕获 reward_card_selection 快照与动作列表（未执行 POST /apply）。",
            "phase": phase,
            "window_kind": window_kind,
            "reward_subphase": reward_subphase,
            "choose_reward_count": len(choose_actions),
            "skip_reward_count": len(skip_actions),
        }
        write_json(artifact_dir / "result.json", result)
        print(json.dumps(result, ensure_ascii=False, indent=2))
        return 0

    if not args.allow_write:
        result = {
            "timestamp": datetime.now().isoformat(),
            "artifact_dir": str(artifact_dir),
            "verdict": "rejected",
            "summary": "缺少 --allow-write 显式确认，拒绝发送真实 POST /apply。",
            "phase": phase,
            "window_kind": window_kind,
            "reward_subphase": reward_subphase,
        }
        write_json(artifact_dir / "result.json", result)
        print(json.dumps(result, ensure_ascii=False, indent=2))
        return 1

    if bool(health.get("read_only", True)):
        result = {
            "timestamp": datetime.now().isoformat(),
            "artifact_dir": str(artifact_dir),
            "verdict": "rejected",
            "summary": "bridge 当前 read_only=true，拒绝发送真实 POST /apply。",
        }
        write_json(artifact_dir / "result.json", result)
        print(json.dumps(result, ensure_ascii=False, indent=2))
        return 1

    if not parse_bool_env("STS2_BRIDGE_ENABLE_WRITES"):
        result = {
            "timestamp": datetime.now().isoformat(),
            "artifact_dir": str(artifact_dir),
            "verdict": "rejected",
            "summary": "当前进程未显式开启 STS2_BRIDGE_ENABLE_WRITES=true，拒绝发送真实写入。",
        }
        write_json(artifact_dir / "result.json", result)
        print(json.dumps(result, ensure_ascii=False, indent=2))
        return 1

    chosen = choose_actions[0]
    apply_payload = {
        "decision_id": snapshot.get("decision_id"),
        "action_id": chosen.get("action_id"),
        "params": chosen.get("params") if isinstance(chosen.get("params"), dict) else {},
    }
    write_json(artifact_dir / "apply_request.json", apply_payload)
    http_status, apply_response = post_json(base_url, "/apply", apply_payload)
    write_json(artifact_dir / "apply_response.json", {"http_status": http_status, "body": apply_response})

    deadline = time.time() + args.apply_timeout_seconds
    after_snapshot: dict[str, Any] | None = None
    while time.time() < deadline:
        time.sleep(0.5)
        latest = fetch_json(base_url, "/snapshot")
        if isinstance(latest, dict) and latest.get("decision_id") != snapshot.get("decision_id"):
            after_snapshot = latest
            break

    if after_snapshot is not None:
        write_json(artifact_dir / "after_snapshot.json", after_snapshot)

    verdict = "success" if apply_response.get("status") == "accepted" and http_status < 400 else "rejected"
    result = {
        "timestamp": datetime.now().isoformat(),
        "artifact_dir": str(artifact_dir),
        "verdict": verdict,
        "summary": "已提交 choose_reward 并完成结果记录。",
        "http_status": http_status,
        "apply_status": apply_response.get("status"),
    }
    write_json(artifact_dir / "result.json", result)
    print(json.dumps(result, ensure_ascii=False, indent=2))
    return 0 if verdict == "success" else 1


if __name__ == "__main__":
    raise SystemExit(main())

