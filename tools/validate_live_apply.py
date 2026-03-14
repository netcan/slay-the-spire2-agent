from __future__ import annotations

import argparse
import json
import os
import sys
import time
from dataclasses import dataclass
from datetime import datetime
from pathlib import Path
from typing import Any
from urllib.error import HTTPError, URLError
from urllib.request import Request, urlopen

try:
    from .debug_sts2_mod import build_mod, discover_game_dir, install_mod, launch_game, wait_for_bridge
except ImportError:
    from debug_sts2_mod import build_mod, discover_game_dir, install_mod, launch_game, wait_for_bridge


ROOT = Path(__file__).resolve().parents[1]
DEFAULT_PORT = 17654


@dataclass(frozen=True)
class CandidateSelection:
    action: dict[str, Any] | None
    reason: str
    phase: str
    notes: list[str]


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
    base = root or (ROOT / "tmp" / "live-apply-validation")
    artifact_dir = base / timestamp_slug()
    artifact_dir.mkdir(parents=True, exist_ok=False)
    return artifact_dir


def parse_bool_env(name: str) -> bool:
    value = os.environ.get(name, "")
    return value.strip().lower() in {"1", "true", "yes", "on"}


def read_health(base_url: str) -> dict[str, Any]:
    payload = fetch_json(base_url, "/health")
    if not isinstance(payload, dict):
        raise TypeError("/health returned a non-object payload")
    return payload


def read_snapshot(base_url: str) -> dict[str, Any]:
    payload = fetch_json(base_url, "/snapshot")
    if not isinstance(payload, dict):
        raise TypeError("/snapshot returned a non-object payload")
    return payload


def read_actions(base_url: str) -> list[dict[str, Any]]:
    payload = fetch_json(base_url, "/actions")
    if not isinstance(payload, list):
        raise TypeError("/actions returned a non-list payload")
    return [action for action in payload if isinstance(action, dict)]


def action_priority(action: dict[str, Any], phase: str, action_counts: dict[str, int]) -> tuple[int, str]:
    action_type = str(action.get("type") or "")
    params = action.get("params")
    params = params if isinstance(params, dict) else {}
    targets = action.get("target_constraints")
    target_constraints = targets if isinstance(targets, list) else []

    if phase == "combat":
        if action_type == "play_card" and params.get("card_id") and not target_constraints:
            return 100, "优先选择无需额外目标的 play_card。"
        if action_type == "end_turn":
            return 60, "没有安全出牌动作时，end_turn 是最明确的低风险回退。"
        if action_type == "play_card" and params.get("card_id"):
            return 20, "该 play_card 需要目标，默认不作为首选。"
        return 0, f"当前不自动选择战斗动作类型 {action_type}。"

    if phase == "reward":
        if action_type == "skip_reward":
            return 100, "奖励窗口优先选择 skip_reward，避免误拿奖励。"
        if action_type == "choose_reward":
            return 50, "choose_reward 可执行，但默认优先级低于 skip_reward。"
        return 0, f"当前不自动选择奖励动作类型 {action_type}。"

    if phase == "map":
        if action_type == "choose_map_node" and action_counts.get("choose_map_node", 0) == 1:
            return 80, "地图窗口仅在唯一可选节点时自动执行。"
        if action_type == "choose_map_node":
            return 0, "地图存在多个可选节点，默认不替用户做路线决策。"
        return 0, f"当前不自动选择地图动作类型 {action_type}。"

    return 0, f"当前 phase={phase} 不在自动写入验证范围内。"


def select_candidate(
    snapshot: dict[str, Any],
    actions: list[dict[str, Any]],
    requested_action_id: str | None = None,
) -> CandidateSelection:
    phase = str(snapshot.get("phase") or "unknown")

    if requested_action_id:
        for action in actions:
            if action.get("action_id") == requested_action_id:
                return CandidateSelection(
                    action=action,
                    reason="使用调用者显式指定的 action_id。",
                    phase=phase,
                    notes=["已跳过自动筛选，直接使用显式 action_id。"],
                )
        return CandidateSelection(
            action=None,
            reason=f"未找到 action_id={requested_action_id} 的合法动作。",
            phase=phase,
            notes=["请重新拉取 /actions 后再指定 action_id。"],
        )

    action_counts: dict[str, int] = {}
    for action in actions:
        action_type = str(action.get("type") or "")
        action_counts[action_type] = action_counts.get(action_type, 0) + 1

    ranked: list[tuple[int, int, dict[str, Any], str]] = []
    rejected_notes: list[str] = []
    for index, action in enumerate(actions):
        score, note = action_priority(action, phase, action_counts)
        if score > 0:
            ranked.append((score, -index, action, note))
        else:
            label = str(action.get("label") or action.get("type") or "unknown")
            rejected_notes.append(f"{label}: {note}")

    if not ranked:
        return CandidateSelection(
            action=None,
            reason="当前窗口不存在满足默认安全策略的候选动作。",
            phase=phase,
            notes=rejected_notes or ["没有可执行动作。"],
        )

    ranked.sort(reverse=True)
    _, _, action, note = ranked[0]
    notes = [note]
    if rejected_notes:
        notes.extend(rejected_notes)
    return CandidateSelection(action=action, reason=note, phase=phase, notes=notes)


def extract_hand_card_ids(snapshot: dict[str, Any]) -> set[str]:
    player = snapshot.get("player")
    if not isinstance(player, dict):
        return set()
    hand = player.get("hand")
    if not isinstance(hand, list):
        return set()
    values: set[str] = set()
    for card in hand:
        if isinstance(card, dict):
            card_id = card.get("card_id")
            if isinstance(card_id, str) and card_id:
                values.add(card_id)
    return values


def detect_progress(
    before_snapshot: dict[str, Any],
    before_actions: list[dict[str, Any]],
    after_snapshot: dict[str, Any],
    after_actions: list[dict[str, Any]],
    candidate: dict[str, Any],
) -> list[str]:
    evidence: list[str] = []

    if after_snapshot.get("decision_id") != before_snapshot.get("decision_id"):
        evidence.append("decision_id_changed")
    if after_snapshot.get("phase") != before_snapshot.get("phase"):
        evidence.append("phase_changed")
    if after_snapshot.get("state_version") != before_snapshot.get("state_version"):
        evidence.append("state_version_changed")

    before_action_ids = {str(action.get("action_id")) for action in before_actions if action.get("action_id")}
    after_action_ids = {str(action.get("action_id")) for action in after_actions if action.get("action_id")}
    candidate_action_id = candidate.get("action_id")
    if candidate_action_id in before_action_ids and candidate_action_id not in after_action_ids:
        evidence.append("action_no_longer_legal")

    before_hand = extract_hand_card_ids(before_snapshot)
    after_hand = extract_hand_card_ids(after_snapshot)
    params = candidate.get("params")
    params = params if isinstance(params, dict) else {}
    card_id = params.get("card_id")
    if isinstance(card_id, str) and card_id and card_id in before_hand and card_id not in after_hand:
        evidence.append("selected_card_left_hand")

    before_player = before_snapshot.get("player")
    after_player = after_snapshot.get("player")
    if isinstance(before_player, dict) and isinstance(after_player, dict):
        if before_player.get("energy") != after_player.get("energy"):
            evidence.append("player_energy_changed")

    return evidence


def summarize_snapshot_schema(snapshot: dict[str, Any]) -> dict[str, Any]:
    player = snapshot.get("player")
    hand = player.get("hand") if isinstance(player, dict) else []
    hand = hand if isinstance(hand, list) else []
    enemies = snapshot.get("enemies")
    enemies = enemies if isinstance(enemies, list) else []
    pile_fields = {
        "hand": hand,
        "draw_pile_cards": player.get("draw_pile_cards") if isinstance(player, dict) else [],
        "discard_pile_cards": player.get("discard_pile_cards") if isinstance(player, dict) else [],
        "exhaust_pile_cards": player.get("exhaust_pile_cards") if isinstance(player, dict) else [],
    }
    cards_with_description = 0
    cards_with_glossary = 0
    cards_without_description_diagnostics = 0
    enemies_with_move_name = 0
    enemies_with_move_description = 0
    enemies_with_move_glossary = 0
    enemies_with_traits = 0
    enemies_with_keywords = 0
    pile_counts: dict[str, int] = {}
    for pile_name, pile_cards in pile_fields.items():
        pile_cards = pile_cards if isinstance(pile_cards, list) else []
        pile_counts[pile_name] = len(pile_cards)
        for card in pile_cards:
            if not isinstance(card, dict):
                continue
            if card.get("description"):
                cards_with_description += 1
            if isinstance(card.get("glossary"), list) and card.get("glossary"):
                cards_with_glossary += 1
            if all(key not in card for key in ("description_quality", "description_source", "description_vars")):
                cards_without_description_diagnostics += 1
    for enemy in enemies:
        if not isinstance(enemy, dict):
            continue
        if enemy.get("move_name"):
            enemies_with_move_name += 1
        if enemy.get("move_description"):
            enemies_with_move_description += 1
        if isinstance(enemy.get("move_glossary"), list) and enemy.get("move_glossary"):
            enemies_with_move_glossary += 1
        if isinstance(enemy.get("traits"), list) and enemy.get("traits"):
            enemies_with_traits += 1
        if isinstance(enemy.get("keywords"), list) and enemy.get("keywords"):
            enemies_with_keywords += 1
    return {
        "hand_count": len(hand),
        "enemy_count": len(enemies),
        "enemies_with_move_name": enemies_with_move_name,
        "enemies_with_move_description": enemies_with_move_description,
        "enemies_with_move_glossary": enemies_with_move_glossary,
        "enemies_with_traits": enemies_with_traits,
        "enemies_with_keywords": enemies_with_keywords,
        "draw_pile_count": int(player.get("draw_pile") or 0) if isinstance(player, dict) else 0,
        "discard_pile_count": int(player.get("discard_pile") or 0) if isinstance(player, dict) else 0,
        "exhaust_pile_count": int(player.get("exhaust_pile") or 0) if isinstance(player, dict) else 0,
        "draw_pile_cards_count": pile_counts["draw_pile_cards"],
        "discard_pile_cards_count": pile_counts["discard_pile_cards"],
        "exhaust_pile_cards_count": pile_counts["exhaust_pile_cards"],
        "cards_with_description": cards_with_description,
        "cards_with_glossary": cards_with_glossary,
        "cards_without_description_diagnostics": cards_without_description_diagnostics,
    }


def discover_runtime_log() -> Path | None:
    log_dir = ROOT / "tmp" / "sts2-debug"
    candidates = sorted(log_dir.glob("sts2-runtime-*.log"), key=lambda path: path.stat().st_mtime, reverse=True)
    return candidates[0] if candidates else None


def summarize_runtime_log(log_path: Path | None) -> dict[str, Any] | None:
    if log_path is None or not log_path.exists():
        return None
    try:
        lines = log_path.read_text(encoding="utf-8", errors="ignore").splitlines()
    except OSError:
        return None
    interesting = [
        line.strip()
        for line in lines
        if "Description " in line or "Text resolution issues" in line
    ]
    return {
        "path": str(log_path),
        "interesting_line_count": len(interesting),
        "interesting_lines_tail": interesting[-20:],
    }


def poll_for_progress(
    base_url: str,
    before_snapshot: dict[str, Any],
    before_actions: list[dict[str, Any]],
    candidate: dict[str, Any],
    timeout_seconds: float,
    interval_seconds: float,
) -> tuple[dict[str, Any], list[dict[str, Any]], list[str]]:
    latest_snapshot = before_snapshot
    latest_actions = before_actions
    last_error: str | None = None
    deadline = time.time() + timeout_seconds

    while time.time() < deadline:
        time.sleep(interval_seconds)
        try:
            latest_snapshot = read_snapshot(base_url)
            latest_actions = read_actions(base_url)
            evidence = detect_progress(before_snapshot, before_actions, latest_snapshot, latest_actions, candidate)
            if evidence:
                return latest_snapshot, latest_actions, evidence
        except (URLError, OSError, ValueError, TypeError) as exc:
            last_error = repr(exc)

    evidence = []
    if last_error:
        evidence.append(f"poll_error:{last_error}")
    return latest_snapshot, latest_actions, evidence


def build_result(
    *,
    mode: str,
    health: dict[str, Any],
    candidate: CandidateSelection,
    verdict: str,
    summary: str,
    artifact_dir: Path,
    apply_requested: bool,
    apply_payload: dict[str, Any] | None = None,
    apply_response: dict[str, Any] | None = None,
    http_status: int | None = None,
    progress_evidence: list[str] | None = None,
    launched_pid: int | None = None,
    snapshot_schema_summary: dict[str, Any] | None = None,
    before_schema_summary: dict[str, Any] | None = None,
    after_schema_summary: dict[str, Any] | None = None,
    runtime_log_summary: dict[str, Any] | None = None,
) -> dict[str, Any]:
    return {
        "mode": mode,
        "timestamp": datetime.now().isoformat(),
        "artifact_dir": str(artifact_dir),
        "provider_mode": health.get("provider_mode"),
        "read_only": health.get("read_only"),
        "apply_requested": apply_requested,
        "launched_pid": launched_pid,
        "phase": candidate.phase,
        "candidate_reason": candidate.reason,
        "candidate_notes": candidate.notes,
        "candidate_action": candidate.action,
        "snapshot_schema_summary": snapshot_schema_summary,
        "before_schema_summary": before_schema_summary,
        "after_schema_summary": after_schema_summary,
        "runtime_log_summary": runtime_log_summary,
        "verdict": verdict,
        "summary": summary,
        "http_status": http_status,
        "apply_request": apply_payload,
        "apply_response": apply_response,
        "progress_evidence": progress_evidence or [],
    }


def ensure_live_runtime(health: dict[str, Any]) -> str | None:
    provider_mode = health.get("provider_mode")
    if provider_mode != "in-game-runtime":
        return f"bridge 当前 provider_mode={provider_mode!r}，不是 in-game-runtime。"
    if not health.get("healthy", False):
        return "bridge /health 显示 healthy=false。"
    return None


def run_validation(args: argparse.Namespace) -> int:
    artifact_dir = create_artifact_dir(Path(args.artifact_root) if args.artifact_root else None)
    base_url = f"http://127.0.0.1:{args.port}"
    launched_pid: int | None = None

    if args.launch:
        game_dir = discover_game_dir(args.game_dir)
        output_dir = build_mod(game_dir)
        install_mod(game_dir, output_dir)
        launched_pid = launch_game(game_dir, args.port, args.enable_writes, args.wait_seconds)

    health = wait_for_bridge(args.port, args.wait_seconds)
    if health is None:
        result = {
            "mode": "apply" if args.apply else "discovery",
            "timestamp": datetime.now().isoformat(),
            "artifact_dir": str(artifact_dir),
            "verdict": "failed",
            "summary": "在超时时间内未探测到 bridge /health。",
            "apply_requested": args.apply,
            "launched_pid": launched_pid,
        }
        write_json(artifact_dir / "result.json", result)
        print(json.dumps(result, ensure_ascii=False, indent=2))
        return 1

    write_json(artifact_dir / "health.json", health)
    live_error = ensure_live_runtime(health)

    try:
        snapshot = read_snapshot(base_url)
        actions = read_actions(base_url)
    except (HTTPError, URLError, OSError, ValueError, TypeError) as exc:
        result = {
            "mode": "apply" if args.apply else "discovery",
            "timestamp": datetime.now().isoformat(),
            "artifact_dir": str(artifact_dir),
            "provider_mode": health.get("provider_mode"),
            "read_only": health.get("read_only"),
            "apply_requested": args.apply,
            "launched_pid": launched_pid,
            "verdict": "failed",
            "summary": "bridge 已附着，但当前无法导出 live snapshot/actions。",
            "error": repr(exc),
            "health_status": health.get("status"),
        }
        write_json(artifact_dir / "result.json", result)
        print(json.dumps(result, ensure_ascii=False, indent=2))
        return 1

    write_json(artifact_dir / "before_snapshot.json", snapshot)
    write_json(artifact_dir / "before_actions.json", actions)
    before_schema_summary = summarize_snapshot_schema(snapshot)
    write_json(artifact_dir / "before_schema_summary.json", before_schema_summary)
    runtime_log_summary = summarize_runtime_log(discover_runtime_log())
    if runtime_log_summary is not None:
        write_json(artifact_dir / "runtime_log_summary.json", runtime_log_summary)

    candidate = select_candidate(snapshot, actions, args.action_id)
    write_json(
        artifact_dir / "candidate.json",
        {
            "phase": candidate.phase,
            "reason": candidate.reason,
            "notes": candidate.notes,
            "action": candidate.action,
        },
    )

    if live_error:
        result = build_result(
            mode="apply" if args.apply else "discovery",
            health=health,
            candidate=candidate,
            verdict="failed",
            summary=live_error,
            artifact_dir=artifact_dir,
            apply_requested=args.apply,
            launched_pid=launched_pid,
            snapshot_schema_summary=before_schema_summary,
            before_schema_summary=before_schema_summary,
            runtime_log_summary=runtime_log_summary,
        )
        write_json(artifact_dir / "result.json", result)
        print(json.dumps(result, ensure_ascii=False, indent=2))
        return 1

    if candidate.action is None:
        result = build_result(
            mode="apply" if args.apply else "discovery",
            health=health,
            candidate=candidate,
            verdict="no_candidate",
            summary=candidate.reason,
            artifact_dir=artifact_dir,
            apply_requested=args.apply,
            launched_pid=launched_pid,
            snapshot_schema_summary=before_schema_summary,
            before_schema_summary=before_schema_summary,
            runtime_log_summary=runtime_log_summary,
        )
        write_json(artifact_dir / "result.json", result)
        print(json.dumps(result, ensure_ascii=False, indent=2))
        return 0

    if not args.apply:
        result = build_result(
            mode="discovery",
            health=health,
            candidate=candidate,
            verdict="discovery_only",
            summary="已完成只读 discovery，未发起真实 POST /apply。",
            artifact_dir=artifact_dir,
            apply_requested=False,
            launched_pid=launched_pid,
            snapshot_schema_summary=before_schema_summary,
            before_schema_summary=before_schema_summary,
            runtime_log_summary=runtime_log_summary,
        )
        write_json(artifact_dir / "result.json", result)
        print(json.dumps(result, ensure_ascii=False, indent=2))
        return 0

    if not args.allow_write:
        result = build_result(
            mode="apply",
            health=health,
            candidate=candidate,
            verdict="rejected",
            summary="缺少 --allow-write 显式确认，拒绝发送真实 POST /apply。",
            artifact_dir=artifact_dir,
            apply_requested=True,
            launched_pid=launched_pid,
            snapshot_schema_summary=before_schema_summary,
            before_schema_summary=before_schema_summary,
            runtime_log_summary=runtime_log_summary,
        )
        write_json(artifact_dir / "result.json", result)
        print(json.dumps(result, ensure_ascii=False, indent=2))
        return 1

    if health.get("read_only", True):
        result = build_result(
            mode="apply",
            health=health,
            candidate=candidate,
            verdict="rejected",
            summary="bridge 当前仍是 read_only=true，拒绝发送真实 POST /apply。",
            artifact_dir=artifact_dir,
            apply_requested=True,
            launched_pid=launched_pid,
            snapshot_schema_summary=before_schema_summary,
            before_schema_summary=before_schema_summary,
            runtime_log_summary=runtime_log_summary,
        )
        write_json(artifact_dir / "result.json", result)
        print(json.dumps(result, ensure_ascii=False, indent=2))
        return 1

    if not (args.enable_writes or parse_bool_env("STS2_BRIDGE_ENABLE_WRITES")):
        result = build_result(
            mode="apply",
            health=health,
            candidate=candidate,
            verdict="rejected",
            summary="当前进程未显式开启 STS2_BRIDGE_ENABLE_WRITES，也未传入 --enable-writes。",
            artifact_dir=artifact_dir,
            apply_requested=True,
            launched_pid=launched_pid,
            snapshot_schema_summary=before_schema_summary,
            before_schema_summary=before_schema_summary,
            runtime_log_summary=runtime_log_summary,
        )
        write_json(artifact_dir / "result.json", result)
        print(json.dumps(result, ensure_ascii=False, indent=2))
        return 1

    apply_payload = {
        "decision_id": snapshot.get("decision_id"),
        "action_id": candidate.action.get("action_id"),
        "params": candidate.action.get("params") if isinstance(candidate.action.get("params"), dict) else {},
    }
    write_json(artifact_dir / "apply_request.json", apply_payload)

    http_status, apply_response = post_json(base_url, "/apply", apply_payload)
    write_json(artifact_dir / "apply_response.json", {"http_status": http_status, "body": apply_response})

    after_snapshot = snapshot
    after_actions = actions
    progress_evidence: list[str] = []

    if apply_response.get("status") == "accepted" and http_status < 400:
        after_snapshot, after_actions, progress_evidence = poll_for_progress(
            base_url=base_url,
            before_snapshot=snapshot,
            before_actions=actions,
            candidate=candidate.action,
            timeout_seconds=args.apply_timeout_seconds,
            interval_seconds=args.poll_interval_seconds,
        )
    else:
        progress_evidence = [f"apply_status:{apply_response.get('status')}"]

    write_json(artifact_dir / "after_snapshot.json", after_snapshot)
    write_json(artifact_dir / "after_actions.json", after_actions)
    after_schema_summary = summarize_snapshot_schema(after_snapshot)
    write_json(artifact_dir / "after_schema_summary.json", after_schema_summary)
    runtime_log_summary = summarize_runtime_log(discover_runtime_log())
    if runtime_log_summary is not None:
        write_json(artifact_dir / "runtime_log_summary.json", runtime_log_summary)

    if apply_response.get("status") != "accepted" or http_status >= 400:
        verdict = "rejected"
        summary = f"bridge 未接受动作，http_status={http_status} status={apply_response.get('status')!r}。"
        exit_code = 1
    elif progress_evidence and not any(item.startswith("poll_error:") for item in progress_evidence):
        verdict = "success"
        summary = "动作已被接受，且观测到 live 状态推进。"
        exit_code = 0
    else:
        verdict = "inconclusive"
        summary = "动作已被接受，但在超时窗口内未确认到明确的 live 状态推进。"
        exit_code = 1

    result = build_result(
        mode="apply",
        health=health,
        candidate=candidate,
        verdict=verdict,
        summary=summary,
        artifact_dir=artifact_dir,
        apply_requested=True,
        apply_payload=apply_payload,
        apply_response=apply_response,
        http_status=http_status,
        progress_evidence=progress_evidence,
        launched_pid=launched_pid,
        snapshot_schema_summary=after_schema_summary,
        before_schema_summary=before_schema_summary,
        after_schema_summary=after_schema_summary,
        runtime_log_summary=runtime_log_summary,
    )
    write_json(artifact_dir / "result.json", result)
    print(json.dumps(result, ensure_ascii=False, indent=2))
    return exit_code


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Validate live in-game STS2 bridge apply behavior.")
    parser.add_argument("--game-dir", help="Path to the Slay the Spire 2 install directory.")
    parser.add_argument("--port", type=int, default=DEFAULT_PORT, help="Bridge port exposed by the in-game mod.")
    parser.add_argument("--wait-seconds", type=float, default=30.0, help="How long to wait for bridge /health.")
    parser.add_argument("--artifact-root", help="Override output root for validation artifacts.")
    parser.add_argument("--action-id", help="Use a specific action_id instead of automatic candidate selection.")
    parser.add_argument("--launch", action="store_true", help="Build, install, and launch the game before validation.")
    parser.add_argument(
        "--enable-writes",
        action="store_true",
        help="When used with --launch, start the game with STS2_BRIDGE_ENABLE_WRITES=true. Also counts as explicit write intent.",
    )
    parser.add_argument("--apply", action="store_true", help="Submit a real POST /apply after discovery.")
    parser.add_argument(
        "--allow-write",
        action="store_true",
        help="Explicitly acknowledge that a real in-game write will be sent when --apply is used.",
    )
    parser.add_argument(
        "--apply-timeout-seconds",
        type=float,
        default=12.0,
        help="How long to wait for post-apply state progression.",
    )
    parser.add_argument(
        "--poll-interval-seconds",
        type=float,
        default=0.5,
        help="Polling interval while waiting for post-apply state progression.",
    )
    return parser


def main() -> int:
    parser = build_parser()
    args = parser.parse_args()
    return run_validation(args)


if __name__ == "__main__":
    sys.exit(main())
