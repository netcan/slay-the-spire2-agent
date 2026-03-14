from __future__ import annotations

import importlib.util
import pathlib
import sys
import unittest
from unittest.mock import patch


ROOT = pathlib.Path(__file__).resolve().parents[1]
MODULE_PATH = ROOT / "tools" / "bridge_action.py"
SPEC = importlib.util.spec_from_file_location("bridge_action", MODULE_PATH)
assert SPEC is not None and SPEC.loader is not None
bridge_action = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = bridge_action
SPEC.loader.exec_module(bridge_action)


class BridgeActionToolTests(unittest.TestCase):
    def test_resolve_action_by_type_returns_selected_match(self) -> None:
        context = bridge_action.BridgeContext("http://127.0.0.1:17654", 5.0)
        snapshot = {"phase": "menu", "decision_id": "dec-1"}
        actions = [
            {"action_id": "act-1", "type": "continue_run", "label": "Continue", "params": {}, "target_constraints": []},
            {"action_id": "act-2", "type": "start_new_run", "label": "New Run", "params": {}, "target_constraints": []},
        ]

        with patch.object(bridge_action, "read_snapshot", return_value=snapshot), patch.object(
            bridge_action, "read_actions", return_value=actions
        ):
            resolved = bridge_action.resolve_action_by_type(context, "continue_run")

        self.assertEqual("act-1", resolved.action["action_id"])
        self.assertEqual("menu", resolved.snapshot["phase"])
        self.assertEqual(1, len(resolved.matches))

    def test_resolve_action_by_type_rejects_missing_match(self) -> None:
        context = bridge_action.BridgeContext("http://127.0.0.1:17654", 5.0)
        snapshot = {"phase": "reward"}
        with patch.object(bridge_action, "read_snapshot", return_value=snapshot), patch.object(
            bridge_action, "read_actions", return_value=[]
        ):
            with self.assertRaises(LookupError):
                bridge_action.resolve_action_by_type(context, "advance_reward")

    def test_common_command_maps_to_expected_action_type(self) -> None:
        parser = bridge_action.build_parser()
        args = parser.parse_args(["continue-run"])

        self.assertEqual("continue-run", args.command)
        self.assertEqual(17654, args.port)
        self.assertEqual(5.0, args.timeout_seconds)

    def test_command_common_action_submits_selected_action(self) -> None:
        parser = bridge_action.build_parser()
        args = parser.parse_args(["end-turn"])
        snapshot = {"phase": "combat", "decision_id": "dec-1"}
        action = {
            "action_id": "act-end",
            "type": "end_turn",
            "label": "End Turn",
            "params": {},
            "target_constraints": [],
        }
        resolved = bridge_action.ResolvedAction(snapshot=snapshot, action=action, matches=[action])

        with patch.object(bridge_action, "resolve_action_by_type", return_value=resolved) as resolve_mock, patch.object(
            bridge_action, "submit_action", return_value={"request": {}, "http_status": 200, "response": {"status": "accepted"}}
        ), patch.object(
            bridge_action, "print_json"
        ):
            exit_code = bridge_action.command_common_action(args)

        resolve_mock.assert_called_once()
        self.assertEqual(0, exit_code)


if __name__ == "__main__":
    unittest.main()
