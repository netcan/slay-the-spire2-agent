from __future__ import annotations

import argparse
import json
import sys
from dataclasses import dataclass
from typing import Any
from urllib.error import HTTPError
from urllib.request import Request, urlopen


DEFAULT_PORT = 17654


@dataclass(frozen=True)
class BridgeContext:
    base_url: str
    timeout_seconds: float


@dataclass(frozen=True)
class ResolvedAction:
    snapshot: dict[str, Any]
    action: dict[str, Any]
    matches: list[dict[str, Any]]


COMMON_ACTION_COMMANDS: dict[str, str] = {
    "continue-run": "continue_run",
    "start-new-run": "start_new_run",
    "confirm-start-run": "confirm_start_run",
    "end-turn": "end_turn",
    "skip-reward": "skip_reward",
    "advance-reward": "advance_reward",
}


def fetch_json(context: BridgeContext, path: str) -> dict[str, Any] | list[Any]:
    with urlopen(context.base_url + path, timeout=context.timeout_seconds) as response:
        return json.loads(response.read().decode("utf-8"))


def post_json(context: BridgeContext, path: str, payload: dict[str, Any]) -> tuple[int, dict[str, Any]]:
    request = Request(
        context.base_url + path,
        data=json.dumps(payload).encode("utf-8"),
        headers={"Content-Type": "application/json"},
        method="POST",
    )
    try:
        with urlopen(request, timeout=context.timeout_seconds) as response:
            return response.status, json.loads(response.read().decode("utf-8"))
    except HTTPError as ex:
        return ex.code, json.loads(ex.read().decode("utf-8"))


def require_object(payload: dict[str, Any] | list[Any], path: str) -> dict[str, Any]:
    if not isinstance(payload, dict):
        raise TypeError(f"{path} returned a non-object payload")
    return payload


def require_list(payload: dict[str, Any] | list[Any], path: str) -> list[dict[str, Any]]:
    if not isinstance(payload, list):
        raise TypeError(f"{path} returned a non-list payload")
    return [item for item in payload if isinstance(item, dict)]


def read_snapshot(context: BridgeContext) -> dict[str, Any]:
    return require_object(fetch_json(context, "/snapshot"), "/snapshot")


def read_actions(context: BridgeContext) -> list[dict[str, Any]]:
    return require_list(fetch_json(context, "/actions"), "/actions")


def read_health(context: BridgeContext) -> dict[str, Any]:
    return require_object(fetch_json(context, "/health"), "/health")


def summarize_action(action: dict[str, Any]) -> dict[str, Any]:
    return {
        "action_id": action.get("action_id"),
        "type": action.get("type"),
        "label": action.get("label"),
        "params": action.get("params") if isinstance(action.get("params"), dict) else {},
        "target_constraints": action.get("target_constraints") if isinstance(action.get("target_constraints"), list) else [],
    }


def resolve_action_by_type(
    context: BridgeContext,
    action_type: str,
    index: int = 0,
    preferred_label: str | None = None,
) -> ResolvedAction:
    snapshot = read_snapshot(context)
    actions = read_actions(context)
    matches = [action for action in actions if str(action.get("type") or "") == action_type]
    if preferred_label:
        narrowed = [action for action in matches if str(action.get("label") or "") == preferred_label]
        if narrowed:
            matches = narrowed
    if not matches:
        raise LookupError(
            f"no legal action with type={action_type!r} in phase={snapshot.get('phase')!r}; "
            f"available={[str(action.get('type') or '') for action in actions]}"
        )
    if index < 0 or index >= len(matches):
        raise IndexError(f"action index {index} is out of range for type={action_type!r}; matches={len(matches)}")
    return ResolvedAction(snapshot=snapshot, action=matches[index], matches=matches)


def submit_action(context: BridgeContext, snapshot: dict[str, Any], action: dict[str, Any], params: dict[str, Any] | None = None) -> dict[str, Any]:
    payload = {
        "decision_id": snapshot.get("decision_id"),
        "action_id": action.get("action_id"),
        "params": params if params is not None else (action.get("params") if isinstance(action.get("params"), dict) else {}),
    }
    status, response = post_json(context, "/apply", payload)
    return {
        "request": payload,
        "http_status": status,
        "response": response,
    }


def print_json(payload: Any) -> None:
    print(json.dumps(payload, ensure_ascii=False, indent=2))


def command_health(args: argparse.Namespace) -> int:
    print_json(read_health(build_context(args)))
    return 0


def command_snapshot(args: argparse.Namespace) -> int:
    print_json(read_snapshot(build_context(args)))
    return 0


def command_actions(args: argparse.Namespace) -> int:
    actions = read_actions(build_context(args))
    if args.action_type:
        actions = [action for action in actions if str(action.get("type") or "") == args.action_type]
    print_json([summarize_action(action) for action in actions])
    return 0


def command_apply(args: argparse.Namespace) -> int:
    context = build_context(args)
    snapshot = read_snapshot(context)
    params = json.loads(args.params) if args.params else {}
    if not isinstance(params, dict):
        raise TypeError("--params must decode to a JSON object")
    payload = {
        "decision_id": snapshot.get("decision_id"),
        "action_id": args.action_id,
        "params": params,
    }
    status, response = post_json(context, "/apply", payload)
    print_json({"request": payload, "http_status": status, "response": response})
    return 0 if status < 400 and str(response.get("status") or "") == "accepted" else 1


def command_run_action_type(args: argparse.Namespace) -> int:
    context = build_context(args)
    resolved = resolve_action_by_type(context, args.action_type, index=args.index, preferred_label=args.label)
    result = submit_action(context, resolved.snapshot, resolved.action)
    print_json(
        {
            "phase": resolved.snapshot.get("phase"),
            "matched_action_count": len(resolved.matches),
            "selected_action": summarize_action(resolved.action),
            **result,
        }
    )
    response = result["response"]
    return 0 if result["http_status"] < 400 and str(response.get("status") or "") == "accepted" else 1


def command_common_action(args: argparse.Namespace) -> int:
    args.action_type = COMMON_ACTION_COMMANDS[args.command]
    return command_run_action_type(args)


def build_context(args: argparse.Namespace) -> BridgeContext:
    if args.base_url:
        base_url = args.base_url.rstrip("/")
    else:
        base_url = f"http://127.0.0.1:{args.port}"
    return BridgeContext(base_url=base_url, timeout_seconds=args.timeout_seconds)


def add_connection_arguments(parser: argparse.ArgumentParser) -> None:
    parser.add_argument("--base-url", help="Override bridge base URL, e.g. http://127.0.0.1:17654")
    parser.add_argument("--port", type=int, default=DEFAULT_PORT, help="Bridge port when --base-url is not provided")
    parser.add_argument("--timeout-seconds", type=float, default=5.0, help="HTTP timeout for each request")


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Run common one-step actions against the live STS2 bridge.")
    subparsers = parser.add_subparsers(dest="command", required=True)

    health_parser = subparsers.add_parser("health", help="Print /health")
    add_connection_arguments(health_parser)
    health_parser.set_defaults(handler=command_health)

    snapshot_parser = subparsers.add_parser("snapshot", help="Print /snapshot")
    add_connection_arguments(snapshot_parser)
    snapshot_parser.set_defaults(handler=command_snapshot)

    actions_parser = subparsers.add_parser("actions", help="Print /actions")
    add_connection_arguments(actions_parser)
    actions_parser.add_argument("--action-type", help="Only show actions with this type")
    actions_parser.set_defaults(handler=command_actions)

    apply_parser = subparsers.add_parser("apply", help="Apply a specific action_id from the current snapshot")
    add_connection_arguments(apply_parser)
    apply_parser.add_argument("--action-id", required=True, help="Exact action_id from /actions")
    apply_parser.add_argument("--params", help="JSON object passed to /apply params")
    apply_parser.set_defaults(handler=command_apply)

    run_action_parser = subparsers.add_parser("run-action-type", help="Pick one legal action by type and apply it")
    add_connection_arguments(run_action_parser)
    run_action_parser.add_argument("action_type", help="Legal action type, e.g. continue_run or end_turn")
    run_action_parser.add_argument("--index", type=int, default=0, help="Pick the Nth match when multiple actions share the same type")
    run_action_parser.add_argument("--label", help="Prefer a specific action label when multiple matches exist")
    run_action_parser.set_defaults(handler=command_run_action_type)

    for command_name, action_type in COMMON_ACTION_COMMANDS.items():
        common_parser = subparsers.add_parser(command_name, help=f"Resolve and apply `{action_type}`")
        add_connection_arguments(common_parser)
        common_parser.add_argument("--index", type=int, default=0, help="Pick the Nth match when multiple actions share the same type")
        common_parser.add_argument("--label", help="Prefer a specific action label when multiple matches exist")
        common_parser.set_defaults(handler=command_common_action)

    return parser


def main() -> int:
    parser = build_parser()
    args = parser.parse_args()
    try:
        return args.handler(args)
    except Exception as exc:
        print_json({"error": type(exc).__name__, "message": str(exc)})
        return 1


if __name__ == "__main__":
    sys.exit(main())
