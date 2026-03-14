from __future__ import annotations

import json
from dataclasses import dataclass
from typing import Any
from urllib.error import HTTPError, URLError
from urllib.request import Request, urlopen

from sts2_agent.bridge.base import (
    BridgeSession,
    GameBridge,
    InvalidPayloadError,
    RemoteBridgeError,
    SessionNotFoundError,
    StaleActionError,
    UnsupportedLifecycleCommandError,
)
from sts2_agent.models import (
    ActionResult,
    ActionSubmission,
    CardView,
    DecisionSnapshot,
    DescriptionVariable,
    EnemyState,
    GlossaryAnchor,
    LegalAction,
    PlayerState,
    PowerView,
    RunMapState,
    RunState,
)


@dataclass(slots=True)
class HttpGameBridgeConfig:
    base_url: str = "http://127.0.0.1:17654"
    timeout_seconds: float = 5.0
    scenario: str = "live_http_bridge"


class HttpGameBridge(GameBridge):
    def __init__(self, config: HttpGameBridgeConfig | None = None) -> None:
        self.config = config or HttpGameBridgeConfig()
        self._sessions: dict[str, BridgeSession] = {}

    def attach_or_start(self, scenario: str = "combat_reward_map_terminal") -> BridgeSession:
        self._read_json("/health")
        snapshot = self._read_json("/snapshot")
        session = BridgeSession(
            session_id=str(snapshot.get("session_id") or scenario or self.config.scenario),
            scenario=scenario or self.config.scenario,
            state_version=int(snapshot.get("state_version") or 0),
            metadata={"base_url": self.config.base_url},
        )
        self._sessions[session.session_id] = session
        return session

    def get_snapshot(self, session_id: str) -> DecisionSnapshot:
        session = self._require_session(session_id)
        payload = self._read_json("/snapshot")
        snapshot = self._decode_snapshot(payload)
        session.state_version = snapshot.state_version
        return snapshot

    def get_legal_actions(self, session_id: str) -> list[LegalAction]:
        self._require_session(session_id)
        payload = self._read_json("/actions")
        if not isinstance(payload, list):
            raise RemoteBridgeError("bridge /actions returned a non-list payload", error_code="invalid_payload")
        actions: list[LegalAction] = []
        for item in payload:
            if not isinstance(item, dict):
                raise RemoteBridgeError("bridge /actions returned an invalid action payload", error_code="invalid_payload")
            actions.append(
                LegalAction(
                    action_id=str(item.get("action_id") or ""),
                    type=str(item.get("type") or ""),
                    label=str(item.get("label") or item.get("type") or ""),
                    params=dict(item.get("params") or {}),
                    target_constraints=list(item.get("target_constraints") or []),
                    metadata=dict(item.get("metadata") or {}),
                )
            )
        return actions

    def submit_action(self, submission: ActionSubmission) -> ActionResult:
        session = self._require_session(submission.session_id)
        payload = {
            "decision_id": submission.decision_id,
            "action_id": submission.action_id,
            "params": submission.args,
        }
        response = self._post_json("/apply", payload)
        status = str(response.get("status") or "")
        error_code = response.get("error_code")
        if status == "accepted":
            metadata = dict(response.get("metadata") or {})
            if "state_version" in metadata:
                session.state_version = int(metadata["state_version"])
            return ActionResult(
                status=status,
                session_id=session.session_id,
                decision_id=str(response.get("decision_id") or submission.decision_id),
                state_version=session.state_version,
                accepted_action_id=str(response.get("action_id") or submission.action_id),
                error_code=None,
                message=str(response.get("message") or ""),
                terminal=False,
                metadata=metadata,
            )

        message = str(response.get("message") or "bridge rejected the action")
        if error_code == "stale_decision":
            raise StaleActionError(message)
        if error_code in {"illegal_action", "invalid_action"}:
            raise InvalidPayloadError(message)
        raise RemoteBridgeError(message, error_code=str(error_code or "bridge_error"))

    def stop(self, session_id: str) -> BridgeSession:
        raise UnsupportedLifecycleCommandError("http bridge does not support remote stop")

    def reset(self, session_id: str) -> BridgeSession:
        raise UnsupportedLifecycleCommandError("http bridge does not support remote reset")

    def _require_session(self, session_id: str) -> BridgeSession:
        if session_id not in self._sessions:
            raise SessionNotFoundError(f"unknown session: {session_id}")
        return self._sessions[session_id]

    def _read_json(self, path: str) -> Any:
        request = Request(self._url(path), method="GET")
        return self._send(request)

    def _post_json(self, path: str, payload: dict[str, Any]) -> dict[str, Any]:
        request = Request(
            self._url(path),
            data=json.dumps(payload).encode("utf-8"),
            headers={"Content-Type": "application/json"},
            method="POST",
        )
        response = self._send(request)
        if not isinstance(response, dict):
            raise RemoteBridgeError("bridge /apply returned a non-object payload", error_code="invalid_payload")
        return response

    def _send(self, request: Request) -> Any:
        try:
            with urlopen(request, timeout=self.config.timeout_seconds) as response:
                return json.loads(response.read().decode("utf-8"))
        except HTTPError as exc:
            body = self._decode_error_body(exc)
            if isinstance(body, dict):
                return body
            raise RemoteBridgeError(f"bridge request failed with http {exc.code}", error_code="http_error") from exc
        except URLError as exc:
            raise RemoteBridgeError(f"bridge connection failed: {exc.reason}", error_code="bridge_unreachable") from exc

    @staticmethod
    def _decode_error_body(exc: HTTPError) -> Any:
        try:
            return json.loads(exc.read().decode("utf-8"))
        except Exception:
            return None

    def _url(self, path: str) -> str:
        return self.config.base_url.rstrip("/") + path

    @staticmethod
    def _decode_snapshot(payload: Any) -> DecisionSnapshot:
        if not isinstance(payload, dict):
            raise RemoteBridgeError("bridge /snapshot returned a non-object payload", error_code="invalid_payload")

        player_payload = payload.get("player")
        player = None
        if isinstance(player_payload, dict):
            hand = [
                HttpGameBridge._decode_card(item)
                for item in player_payload.get("hand", [])
                if isinstance(item, dict)
            ]
            powers = [
                HttpGameBridge._decode_power(item)
                for item in player_payload.get("powers", [])
                if isinstance(item, dict)
            ]
            player = PlayerState(
                hp=int(player_payload.get("hp") or 0),
                max_hp=int(player_payload.get("max_hp") or 0),
                block=int(player_payload.get("block") or 0),
                energy=int(player_payload.get("energy") or 0),
                gold=int(player_payload.get("gold") or 0),
                hand=hand,
                draw_pile=int(player_payload.get("draw_pile") or 0),
                discard_pile=int(player_payload.get("discard_pile") or 0),
                exhaust_pile=int(player_payload.get("exhaust_pile") or 0),
                relics=list(player_payload.get("relics") or []),
                potions=list(player_payload.get("potions") or []),
                powers=powers,
            )

        enemies = []
        for item in payload.get("enemies", []):
            if isinstance(item, dict):
                enemies.append(HttpGameBridge._decode_enemy(item))

        metadata = dict(payload.get("metadata") or {})
        compatibility = payload.get("compatibility")
        if isinstance(compatibility, dict):
            metadata["compatibility"] = compatibility

        run_state = HttpGameBridge._decode_run_state(payload.get("run_state"))

        return DecisionSnapshot(
            session_id=str(payload.get("session_id") or ""),
            decision_id=str(payload.get("decision_id") or ""),
            state_version=int(payload.get("state_version") or 0),
            phase=str(payload.get("phase") or "unknown"),
            player=player,
            enemies=enemies,
            rewards=list(payload.get("rewards") or []),
            map_nodes=list(payload.get("map_nodes") or []),
            terminal=bool(payload.get("terminal")),
            metadata=metadata,
            run_state=run_state,
        )

    @staticmethod
    def _decode_card(payload: dict[str, Any]) -> CardView:
        description_raw = HttpGameBridge._as_optional_str(payload.get("description_raw"))
        description_rendered = HttpGameBridge._as_optional_str(payload.get("description_rendered"))
        description = HttpGameBridge._as_optional_str(payload.get("description")) or description_rendered or description_raw
        return CardView(
            card_id=str(payload.get("card_id") or ""),
            name=str(payload.get("name") or ""),
            cost=int(payload.get("cost") or 0),
            playable=bool(payload.get("playable", True)),
            instance_card_id=payload.get("instance_card_id"),
            canonical_card_id=payload.get("canonical_card_id"),
            description=description,
            cost_for_turn=HttpGameBridge._as_optional_int(payload.get("cost_for_turn")),
            upgraded=payload.get("upgraded") if isinstance(payload.get("upgraded"), bool) else None,
            target_type=payload.get("target_type"),
            card_type=payload.get("card_type"),
            rarity=payload.get("rarity"),
            traits=list(payload.get("traits") or []),
            keywords=list(payload.get("keywords") or []),
            description_raw=description_raw,
            description_rendered=description_rendered or description,
            description_quality=HttpGameBridge._as_optional_str(payload.get("description_quality")),
            description_source=HttpGameBridge._as_optional_str(payload.get("description_source")),
            description_vars=HttpGameBridge._decode_description_vars(payload.get("description_vars")),
            glossary=HttpGameBridge._decode_glossary(payload.get("glossary")),
        )

    @staticmethod
    def _decode_power(payload: dict[str, Any]) -> PowerView:
        description_raw = HttpGameBridge._as_optional_str(payload.get("description_raw"))
        description_rendered = HttpGameBridge._as_optional_str(payload.get("description_rendered"))
        description = HttpGameBridge._as_optional_str(payload.get("description")) or description_rendered or description_raw
        return PowerView(
            power_id=str(payload.get("power_id") or ""),
            name=str(payload.get("name") or ""),
            amount=HttpGameBridge._as_optional_int(payload.get("amount")),
            description=description,
            canonical_power_id=payload.get("canonical_power_id"),
            description_raw=description_raw,
            description_rendered=description_rendered or description,
            description_quality=HttpGameBridge._as_optional_str(payload.get("description_quality")),
            description_source=HttpGameBridge._as_optional_str(payload.get("description_source")),
            description_vars=HttpGameBridge._decode_description_vars(payload.get("description_vars")),
            glossary=HttpGameBridge._decode_glossary(payload.get("glossary")),
        )

    @staticmethod
    def _decode_enemy(payload: dict[str, Any]) -> EnemyState:
        return EnemyState(
            enemy_id=str(payload.get("enemy_id") or ""),
            name=str(payload.get("name") or ""),
            hp=int(payload.get("hp") or 0),
            max_hp=int(payload.get("max_hp") or 0),
            block=int(payload.get("block") or 0),
            intent=str(payload.get("intent") or "unknown"),
            is_alive=bool(payload.get("is_alive", True)),
            instance_enemy_id=payload.get("instance_enemy_id"),
            canonical_enemy_id=payload.get("canonical_enemy_id"),
            intent_raw=payload.get("intent_raw"),
            intent_type=payload.get("intent_type"),
            intent_damage=HttpGameBridge._as_optional_int(payload.get("intent_damage")),
            intent_hits=HttpGameBridge._as_optional_int(payload.get("intent_hits")),
            intent_block=HttpGameBridge._as_optional_int(payload.get("intent_block")),
            intent_effects=list(payload.get("intent_effects") or []),
            powers=[
                HttpGameBridge._decode_power(item)
                for item in payload.get("powers", [])
                if isinstance(item, dict)
            ],
        )

    @staticmethod
    def _decode_run_state(payload: Any) -> RunState | None:
        if not isinstance(payload, dict):
            return None
        map_payload = payload.get("map")
        map_state = None
        if isinstance(map_payload, dict):
            map_state = RunMapState(
                current_coord=map_payload.get("current_coord"),
                current_node_type=map_payload.get("current_node_type"),
                reachable_nodes=list(map_payload.get("reachable_nodes") or []),
                source=map_payload.get("source"),
            )
        return RunState(
            act=HttpGameBridge._as_optional_int(payload.get("act")),
            floor=HttpGameBridge._as_optional_int(payload.get("floor")),
            current_room_type=payload.get("current_room_type"),
            current_location_type=payload.get("current_location_type"),
            current_act_index=HttpGameBridge._as_optional_int(payload.get("current_act_index")),
            ascension_level=HttpGameBridge._as_optional_int(payload.get("ascension_level")),
            map=map_state,
        )

    @staticmethod
    def _as_optional_int(value: Any) -> int | None:
        if value is None:
            return None
        try:
            return int(value)
        except (TypeError, ValueError):
            return None

    @staticmethod
    def _as_optional_str(value: Any) -> str | None:
        return value if isinstance(value, str) and value else None

    @staticmethod
    def _decode_description_vars(payload: Any) -> list[DescriptionVariable]:
        if not isinstance(payload, list):
            return []
        variables: list[DescriptionVariable] = []
        for item in payload:
            if not isinstance(item, dict):
                continue
            key = item.get("key")
            if not isinstance(key, str) or not key:
                continue
            variables.append(
                DescriptionVariable(
                    key=key,
                    value=HttpGameBridge._as_optional_int(item.get("value")),
                    source=HttpGameBridge._as_optional_str(item.get("source")),
                    placeholder=HttpGameBridge._as_optional_str(item.get("placeholder")),
                )
            )
        return variables

    @staticmethod
    def _decode_glossary(payload: Any) -> list[GlossaryAnchor]:
        if not isinstance(payload, list):
            return []
        glossary: list[GlossaryAnchor] = []
        for item in payload:
            if not isinstance(item, dict):
                continue
            glossary_id = item.get("glossary_id")
            display_text = item.get("display_text")
            if not isinstance(glossary_id, str) or not glossary_id:
                continue
            if not isinstance(display_text, str) or not display_text:
                continue
            glossary.append(
                GlossaryAnchor(
                    glossary_id=glossary_id,
                    display_text=display_text,
                    hint=HttpGameBridge._as_optional_str(item.get("hint")),
                    source=HttpGameBridge._as_optional_str(item.get("source")),
                )
            )
        return glossary
