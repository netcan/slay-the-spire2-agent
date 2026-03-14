from __future__ import annotations

import json
from copy import deepcopy
from dataclasses import dataclass
from importlib.resources import files
from typing import Any

from sts2_agent.bridge.base import (
    BridgeSession,
    GameBridge,
    InterruptedSessionError,
    InvalidPayloadError,
    SessionNotFoundError,
    StaleActionError,
)
from sts2_agent.ids import create_action_id, create_decision_id, create_session_id, ensure_state_version, validate_identifier
from sts2_agent.models import (
    ActionResult,
    ActionStatus,
    ActionSubmission,
    CardView,
    DecisionSnapshot,
    EnemyState,
    LegalAction,
    PlayerState,
    PowerView,
    RunMapState,
    RunState,
)


@dataclass(slots=True)
class WindowFixture:
    phase: str
    player: dict[str, Any] | None
    enemies: list[dict[str, Any]]
    rewards: list[str]
    map_nodes: list[str]
    legal_actions: list[dict[str, Any]]
    metadata: dict[str, Any]
    run_state: dict[str, Any] | None = None
    terminal: bool = False


class MockGameBridge(GameBridge):
    def __init__(self, scenario: str = "combat_reward_map_terminal") -> None:
        self._default_scenario = scenario
        self._sessions: dict[str, BridgeSession] = {}
        self._states: dict[str, dict[str, Any]] = {}

    def attach_or_start(self, scenario: str = "combat_reward_map_terminal") -> BridgeSession:
        active_scenario = scenario or self._default_scenario
        session = BridgeSession(session_id=create_session_id(active_scenario), scenario=active_scenario)
        self._sessions[session.session_id] = session
        self._states[session.session_id] = {
            "index": 0,
            "windows": self._load_scenario(active_scenario),
            "stopped": False,
        }
        return session

    def get_snapshot(self, session_id: str) -> DecisionSnapshot:
        session, _ = self._require_session(session_id)
        window = self._current_window(session_id)
        decision_id = create_decision_id(session.session_id, session.state_version, window.phase)
        return DecisionSnapshot(
            session_id=session.session_id,
            decision_id=decision_id,
            state_version=session.state_version,
            phase=window.phase,
            player=self._build_player(window.player),
            enemies=[self._build_enemy(enemy) for enemy in window.enemies],
            rewards=deepcopy(window.rewards),
            map_nodes=deepcopy(window.map_nodes),
            terminal=window.terminal,
            metadata={"scenario": session.scenario, **deepcopy(window.metadata)},
            run_state=self._build_run_state(window.run_state),
        )

    def get_legal_actions(self, session_id: str) -> list[LegalAction]:
        session, _ = self._require_session(session_id)
        window = self._current_window(session_id)
        if window.terminal:
            return []
        decision_id = create_decision_id(session.session_id, session.state_version, window.phase)
        actions = []
        for action in window.legal_actions:
            payload = {k: v for k, v in action.items() if k not in {"label", "type"}}
            actions.append(
                LegalAction(
                    action_id=create_action_id(decision_id, action["type"], payload),
                    type=action["type"],
                    label=action["label"],
                    params={k: v for k, v in action.items() if k not in {"label", "type", "target_constraints"}},
                    target_constraints=action.get("target_constraints", []),
                    metadata={"decision_id": decision_id},
                )
            )
        return actions

    def submit_action(self, submission: ActionSubmission) -> ActionResult:
        session, state = self._require_session(submission.session_id)
        validate_identifier(submission.session_id, "sess")
        ensure_state_version(submission.state_version)
        if state["stopped"] or session.interrupted:
            raise InterruptedSessionError("session is interrupted")

        snapshot = self.get_snapshot(submission.session_id)
        if submission.state_version != snapshot.state_version or submission.decision_id != snapshot.decision_id:
            raise StaleActionError("submitted action does not match active decision window")

        legal_actions = {action.action_id: action for action in self.get_legal_actions(submission.session_id)}
        if submission.action_id not in legal_actions:
            raise InvalidPayloadError("action is not legal for the active decision window")

        accepted = legal_actions[submission.action_id]
        if not snapshot.terminal and state["index"] < len(state["windows"]) - 1:
            state["index"] += 1
            session.state_version += 1
        next_snapshot = self.get_snapshot(submission.session_id)
        return ActionResult(
            status=ActionStatus.ACCEPTED,
            session_id=session.session_id,
            decision_id=next_snapshot.decision_id,
            state_version=next_snapshot.state_version,
            accepted_action_id=accepted.action_id,
            message=f"accepted {accepted.type}",
            terminal=next_snapshot.terminal,
            metadata={"phase": next_snapshot.phase},
        )

    def stop(self, session_id: str) -> BridgeSession:
        session, state = self._require_session(session_id)
        state["stopped"] = True
        session.interrupted = True
        return session

    def reset(self, session_id: str) -> BridgeSession:
        session, _ = self._require_session(session_id)
        return self.attach_or_start(session.scenario)

    def _require_session(self, session_id: str) -> tuple[BridgeSession, dict[str, Any]]:
        if session_id not in self._sessions:
            raise SessionNotFoundError(f"unknown session: {session_id}")
        return self._sessions[session_id], self._states[session_id]

    def _current_window(self, session_id: str) -> WindowFixture:
        session, state = self._require_session(session_id)
        if state["stopped"] or session.interrupted:
            raise InterruptedSessionError("session is interrupted")
        raw = state["windows"][state["index"]]
        return WindowFixture(**deepcopy(raw))

    def _load_scenario(self, scenario: str) -> list[dict[str, Any]]:
        if scenario != "combat_reward_map_terminal":
            raise InvalidPayloadError(f"unsupported scenario: {scenario}")
        fixture_dir = files("sts2_agent.fixtures")
        ordered = ["combat_turn.json", "reward_choice.json", "map_choice.json", "terminal.json"]
        windows = []
        for name in ordered:
            windows.append(json.loads((fixture_dir / name).read_text(encoding="utf-8-sig")))
        return windows

    @staticmethod
    def _build_player(raw: dict[str, Any] | None) -> PlayerState | None:
        if raw is None:
            return None
        hand = [MockGameBridge._build_card(card) for card in raw.get("hand", []) if isinstance(card, dict)]
        powers = [MockGameBridge._build_power(power) for power in raw.get("powers", []) if isinstance(power, dict)]
        return PlayerState(
            hp=int(raw.get("hp") or 0),
            max_hp=int(raw.get("max_hp") or 0),
            block=int(raw.get("block") or 0),
            energy=int(raw.get("energy") or 0),
            gold=int(raw.get("gold") or 0),
            hand=hand,
            draw_pile=int(raw.get("draw_pile") or 0),
            discard_pile=int(raw.get("discard_pile") or 0),
            exhaust_pile=int(raw.get("exhaust_pile") or 0),
            relics=list(raw.get("relics") or []),
            potions=list(raw.get("potions") or []),
            powers=powers,
        )

    @staticmethod
    def _build_card(raw: dict[str, Any]) -> CardView:
        return CardView(
            card_id=str(raw.get("card_id") or ""),
            name=str(raw.get("name") or ""),
            cost=int(raw.get("cost") or 0),
            playable=bool(raw.get("playable", True)),
            instance_card_id=raw.get("instance_card_id"),
            canonical_card_id=raw.get("canonical_card_id"),
            description=raw.get("description"),
            cost_for_turn=MockGameBridge._optional_int(raw.get("cost_for_turn")),
            upgraded=raw.get("upgraded") if isinstance(raw.get("upgraded"), bool) else None,
            target_type=raw.get("target_type"),
            card_type=raw.get("card_type"),
            rarity=raw.get("rarity"),
            traits=list(raw.get("traits") or []),
            keywords=list(raw.get("keywords") or []),
        )

    @staticmethod
    def _build_power(raw: dict[str, Any]) -> PowerView:
        return PowerView(
            power_id=str(raw.get("power_id") or ""),
            name=str(raw.get("name") or ""),
            amount=MockGameBridge._optional_int(raw.get("amount")),
            description=raw.get("description"),
            canonical_power_id=raw.get("canonical_power_id"),
        )

    @staticmethod
    def _build_enemy(raw: dict[str, Any]) -> EnemyState:
        return EnemyState(
            enemy_id=str(raw.get("enemy_id") or ""),
            name=str(raw.get("name") or ""),
            hp=int(raw.get("hp") or 0),
            max_hp=int(raw.get("max_hp") or 0),
            block=int(raw.get("block") or 0),
            intent=str(raw.get("intent") or "unknown"),
            is_alive=bool(raw.get("is_alive", True)),
            instance_enemy_id=raw.get("instance_enemy_id"),
            canonical_enemy_id=raw.get("canonical_enemy_id"),
            intent_raw=raw.get("intent_raw"),
            intent_type=raw.get("intent_type"),
            intent_damage=MockGameBridge._optional_int(raw.get("intent_damage")),
            intent_hits=MockGameBridge._optional_int(raw.get("intent_hits")),
            intent_block=MockGameBridge._optional_int(raw.get("intent_block")),
            intent_effects=list(raw.get("intent_effects") or []),
            powers=[MockGameBridge._build_power(power) for power in raw.get("powers", []) if isinstance(power, dict)],
        )

    @staticmethod
    def _build_run_state(raw: Any) -> RunState | None:
        if not isinstance(raw, dict):
            return None
        map_payload = raw.get("map")
        map_state = None
        if isinstance(map_payload, dict):
            map_state = RunMapState(
                current_coord=map_payload.get("current_coord"),
                current_node_type=map_payload.get("current_node_type"),
                reachable_nodes=list(map_payload.get("reachable_nodes") or []),
                source=map_payload.get("source"),
            )
        return RunState(
            act=MockGameBridge._optional_int(raw.get("act")),
            floor=MockGameBridge._optional_int(raw.get("floor")),
            current_room_type=raw.get("current_room_type"),
            current_location_type=raw.get("current_location_type"),
            current_act_index=MockGameBridge._optional_int(raw.get("current_act_index")),
            ascension_level=MockGameBridge._optional_int(raw.get("ascension_level")),
            map=map_state,
        )

    @staticmethod
    def _optional_int(value: Any) -> int | None:
        if value is None:
            return None
        try:
            return int(value)
        except (TypeError, ValueError):
            return None
