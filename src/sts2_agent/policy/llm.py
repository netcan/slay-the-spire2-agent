from __future__ import annotations

import json
from dataclasses import dataclass
from typing import Any
from urllib.error import HTTPError, URLError
from urllib.request import Request, urlopen

from sts2_agent.models import DecisionSnapshot, LegalAction, PolicyDecision, to_dict
from sts2_agent.policy.base import PolicyError


class ChatCompletionsRequestError(PolicyError):
    error_code = "llm_request_error"


class ChatCompletionsTimeoutError(PolicyError):
    error_code = "llm_timeout"


class ChatCompletionsParseError(PolicyError):
    error_code = "llm_parse_error"


@dataclass(slots=True)
class ChatCompletionsConfig:
    base_url: str = "http://127.0.0.1:8080/v1"
    model: str = "default"
    api_key: str | None = None
    timeout_seconds: float = 20.0
    temperature: float = 0.2
    max_tokens: int = 256


class ChatCompletionsPolicy:
    def __init__(self, config: ChatCompletionsConfig | None = None) -> None:
        self.config = config or ChatCompletionsConfig()

    def decide(self, snapshot: DecisionSnapshot, legal_actions: list[LegalAction]) -> PolicyDecision:
        messages = self._build_messages(snapshot, legal_actions)
        request_payload = {
            "model": self.config.model,
            "messages": messages,
            "temperature": self.config.temperature,
            "max_tokens": self.config.max_tokens,
            "stream": False,
        }
        response_payload = self._post_json("/chat/completions", request_payload)
        raw_content = self._extract_content(response_payload)
        parsed = self._parse_response_text(raw_content)
        decision = PolicyDecision(
            action_id=parsed["action_id"],
            reason=parsed["reason"],
            halt=parsed["halt"],
            metadata={
                "provider": "chat_completions",
                "model": self.config.model,
                "parse_status": "ok",
                "request_payload_summary": {
                    "message_count": len(messages),
                    "legal_action_count": len(legal_actions),
                    "phase": snapshot.phase,
                },
                "raw_response_text": raw_content,
            },
        )
        if parsed["args"]:
            decision.metadata["action_args"] = parsed["args"]
        return decision

    def _build_messages(self, snapshot: DecisionSnapshot, legal_actions: list[LegalAction]) -> list[dict[str, str]]:
        system_prompt = (
            "你是 Slay the Spire 2 自动打牌 agent。"
            "只能从给定 legal actions 中选择一个 action_id。"
            "必须返回 JSON，字段为 action_id、reason、halt、args。"
            "如果你认为当前不应继续自动操作，可以返回 halt=true 且 action_id=null。"
            "args 可省略或填空对象；若所选动作有多个 target_constraints，必须在 args.target_id 中返回其中一个合法目标。"
            "snapshot 里的手牌、敌人、powers、intent 和 run_state 都是当前局面的事实层信息，应优先基于这些字段判断，而不是只猜卡名。"
            "当 snapshot.phase=reward 时，你需要在 choose_reward 或 skip_reward 等奖励动作中做选择；"
            "若不确定，优先返回 halt=true 或选择 skip_reward（并在 reason 说明原因）。"
            "当 snapshot.phase=map 时，只能在 choose_map_node 中选择一个可达节点；"
            "若没有足够信息判断路线，请优先选择更保守的普通战斗节点。"
        )
        user_payload = {
            "snapshot": self._summarize_snapshot(snapshot),
            "legal_actions": [self._summarize_action(action) for action in legal_actions],
            "output_schema": {
                "action_id": "string|null",
                "reason": "string",
                "halt": "boolean",
                "args": {
                    "target_id": "string, required when selected action has multiple target_constraints",
                },
            },
        }
        return [
            {"role": "system", "content": system_prompt},
            {"role": "user", "content": json.dumps(user_payload, ensure_ascii=False)},
        ]

    def _post_json(self, path: str, payload: dict[str, Any]) -> dict[str, Any]:
        headers = {"Content-Type": "application/json"}
        if self.config.api_key:
            headers["Authorization"] = f"Bearer {self.config.api_key}"
        request = Request(
            self.config.base_url.rstrip("/") + path,
            data=json.dumps(payload).encode("utf-8"),
            headers=headers,
            method="POST",
        )
        try:
            with urlopen(request, timeout=self.config.timeout_seconds) as response:
                return json.loads(response.read().decode("utf-8"))
        except HTTPError as exc:
            message = self._read_error_body(exc) or f"http {exc.code}"
            raise ChatCompletionsRequestError(message) from exc
        except TimeoutError as exc:
            raise ChatCompletionsTimeoutError("chat completions request timed out") from exc
        except URLError as exc:
            raise ChatCompletionsRequestError(f"chat completions request failed: {exc.reason}") from exc

    @staticmethod
    def _read_error_body(exc: HTTPError) -> str | None:
        try:
            payload = json.loads(exc.read().decode("utf-8"))
        except Exception:
            return None
        if isinstance(payload, dict):
            error = payload.get("error")
            if isinstance(error, dict):
                return str(error.get("message") or error)
            return str(payload.get("message") or payload)
        return None

    @staticmethod
    def _extract_content(payload: dict[str, Any]) -> str:
        choices = payload.get("choices")
        if not isinstance(choices, list) or not choices:
            raise ChatCompletionsParseError("chat completions response missing choices")
        first = choices[0]
        if not isinstance(first, dict):
            raise ChatCompletionsParseError("chat completions choice payload is invalid")
        message = first.get("message")
        if not isinstance(message, dict):
            raise ChatCompletionsParseError("chat completions response missing message")
        content = message.get("content")
        if not isinstance(content, str):
            raise ChatCompletionsParseError("chat completions response content is not a string")
        return content.strip()

    @staticmethod
    def _parse_response_text(text: str) -> dict[str, Any]:
        candidate = text.strip()
        if candidate.startswith("```"):
            lines = candidate.splitlines()
            if lines and lines[0].startswith("```"):
                lines = lines[1:]
            if lines and lines[-1].strip().startswith("```"):
                lines = lines[:-1]
            candidate = "\n".join(lines).strip()
        try:
            payload = json.loads(candidate)
        except json.JSONDecodeError as exc:
            extracted = ChatCompletionsPolicy._extract_json_object(candidate)
            if extracted is None:
                raise ChatCompletionsParseError("chat completions response is not valid JSON") from exc
            try:
                payload = json.loads(extracted)
            except json.JSONDecodeError as nested_exc:
                raise ChatCompletionsParseError("chat completions response is not valid JSON") from nested_exc
        if not isinstance(payload, dict):
            raise ChatCompletionsParseError("chat completions response JSON must be an object")
        action_id = payload.get("action_id")
        reason = payload.get("reason")
        halt = payload.get("halt")
        args = payload.get("args")
        if action_id is not None and not isinstance(action_id, str):
            raise ChatCompletionsParseError("action_id must be a string or null")
        if not isinstance(reason, str) or not reason.strip():
            raise ChatCompletionsParseError("reason must be a non-empty string")
        if not isinstance(halt, bool):
            raise ChatCompletionsParseError("halt must be a boolean")
        if args is None:
            normalized_args: dict[str, Any] = {}
        else:
            if not isinstance(args, dict):
                raise ChatCompletionsParseError("args must be an object when provided")
            normalized_args = dict(args)
            target_id = normalized_args.get("target_id")
            if target_id is not None and not isinstance(target_id, str):
                raise ChatCompletionsParseError("args.target_id must be a string when provided")
        return {
            "action_id": action_id,
            "reason": reason.strip(),
            "halt": halt,
            "args": normalized_args,
        }

    @staticmethod
    def _extract_json_object(text: str) -> str | None:
        start = text.find("{")
        if start < 0:
            return None
        depth = 0
        in_string = False
        escaped = False
        for index in range(start, len(text)):
            char = text[index]
            if in_string:
                if escaped:
                    escaped = False
                elif char == "\\":
                    escaped = True
                elif char == '"':
                    in_string = False
                continue
            if char == '"':
                in_string = True
                continue
            if char == "{":
                depth += 1
            elif char == "}":
                depth -= 1
                if depth == 0:
                    return text[start : index + 1]
        return None

    @staticmethod
    def _summarize_action(action: LegalAction) -> dict[str, Any]:
        payload = {
            "action_id": action.action_id,
            "type": action.type,
            "label": action.label,
            "params": to_dict(action.params),
            "target_constraints": to_dict(action.target_constraints),
        }
        card_preview = action.metadata.get("card_preview")
        if isinstance(card_preview, dict):
            payload["card_preview"] = to_dict(card_preview)
        return payload

    @staticmethod
    def _summarize_snapshot(snapshot: DecisionSnapshot) -> dict[str, Any]:
        payload: dict[str, Any] = {
            "session_id": snapshot.session_id,
            "decision_id": snapshot.decision_id,
            "state_version": snapshot.state_version,
            "phase": snapshot.phase,
            "terminal": snapshot.terminal,
        }
        if snapshot.player is not None:
            payload["player"] = {
                "hp": snapshot.player.hp,
                "max_hp": snapshot.player.max_hp,
                "block": snapshot.player.block,
                "energy": snapshot.player.energy,
                "gold": snapshot.player.gold,
                "draw_pile": snapshot.player.draw_pile,
                "discard_pile": snapshot.player.discard_pile,
                "exhaust_pile": snapshot.player.exhaust_pile,
                "hand": [ChatCompletionsPolicy._summarize_card(card) for card in snapshot.player.hand],
                "draw_pile_cards": [ChatCompletionsPolicy._summarize_card(card) for card in snapshot.player.draw_pile_cards],
                "discard_pile_cards": [ChatCompletionsPolicy._summarize_card(card) for card in snapshot.player.discard_pile_cards],
                "exhaust_pile_cards": [ChatCompletionsPolicy._summarize_card(card) for card in snapshot.player.exhaust_pile_cards],
                "relics": list(snapshot.player.relics),
                "potions": list(snapshot.player.potions),
                "powers": [ChatCompletionsPolicy._summarize_power(power) for power in snapshot.player.powers],
            }
        if snapshot.enemies:
            payload["enemies"] = [ChatCompletionsPolicy._summarize_enemy(enemy) for enemy in snapshot.enemies]
        if snapshot.rewards:
            payload["rewards"] = list(snapshot.rewards)
        if snapshot.map_nodes:
            payload["map_nodes"] = list(snapshot.map_nodes)
        if snapshot.run_state is not None:
            payload["run_state"] = ChatCompletionsPolicy._summarize_run_state(snapshot)
        if snapshot.metadata:
            metadata_summary = {
                key: snapshot.metadata[key]
                for key in ("window_kind", "reward_subphase", "map_ready", "reward_pending")
                if key in snapshot.metadata
            }
            if metadata_summary:
                payload["metadata"] = metadata_summary
        return payload

    @staticmethod
    def _summarize_card(card: Any) -> dict[str, Any]:
        payload = {
            "card_id": card.card_id,
            "name": card.name,
            "cost": card.cost,
            "playable": card.playable,
        }
        optional_values = {
            "canonical_card_id": card.canonical_card_id,
            "description": ChatCompletionsPolicy._preferred_description_text(card),
            "glossary": ChatCompletionsPolicy._summarize_glossary(getattr(card, "glossary", [])),
            "cost_for_turn": card.cost_for_turn,
            "upgraded": card.upgraded,
            "target_type": card.target_type,
            "card_type": card.card_type,
            "rarity": card.rarity,
            "traits": list(card.traits),
            "keywords": list(card.keywords),
        }
        for key, value in optional_values.items():
            if value not in (None, [], ""):
                payload[key] = value
        return payload

    @staticmethod
    def _summarize_power(power: Any) -> dict[str, Any]:
        payload = {
            "power_id": power.power_id,
            "name": power.name,
        }
        preferred_description = ChatCompletionsPolicy._preferred_description_text(power)
        if power.amount is not None:
            payload["amount"] = power.amount
        if preferred_description:
            payload["description"] = preferred_description
        glossary = ChatCompletionsPolicy._summarize_glossary(getattr(power, "glossary", []))
        if glossary:
            payload["glossary"] = glossary
        if power.canonical_power_id:
            payload["canonical_power_id"] = power.canonical_power_id
        return payload

    @staticmethod
    def _summarize_enemy(enemy: Any) -> dict[str, Any]:
        payload = {
            "enemy_id": enemy.enemy_id,
            "name": enemy.name,
            "hp": enemy.hp,
            "max_hp": enemy.max_hp,
            "block": enemy.block,
            "intent": enemy.intent,
            "is_alive": enemy.is_alive,
        }
        optional_values = {
            "canonical_enemy_id": enemy.canonical_enemy_id,
            "intent_raw": enemy.intent_raw,
            "intent_type": enemy.intent_type,
            "intent_damage": enemy.intent_damage,
            "intent_hits": enemy.intent_hits,
            "intent_block": enemy.intent_block,
            "intent_effects": list(enemy.intent_effects),
            "move_name": enemy.move_name,
            "move_description": ChatCompletionsPolicy._preferred_description_text(enemy),
            "move_glossary": ChatCompletionsPolicy._summarize_glossary(getattr(enemy, "move_glossary", [])),
            "traits": list(enemy.traits),
            "keywords": list(enemy.keywords),
        }
        for key, value in optional_values.items():
            if value not in (None, [], ""):
                payload[key] = value
        if enemy.powers:
            payload["powers"] = [ChatCompletionsPolicy._summarize_power(power) for power in enemy.powers]
        return payload

    @staticmethod
    def _summarize_run_state(snapshot: DecisionSnapshot) -> dict[str, Any]:
        assert snapshot.run_state is not None
        payload = {
            "act": snapshot.run_state.act,
            "floor": snapshot.run_state.floor,
            "current_room_type": snapshot.run_state.current_room_type,
            "current_location_type": snapshot.run_state.current_location_type,
            "current_act_index": snapshot.run_state.current_act_index,
            "ascension_level": snapshot.run_state.ascension_level,
        }
        payload = {key: value for key, value in payload.items() if value is not None}
        if snapshot.run_state.map is not None:
            map_payload = {
                "current_coord": snapshot.run_state.map.current_coord,
                "current_node_type": snapshot.run_state.map.current_node_type,
                "reachable_nodes": list(snapshot.run_state.map.reachable_nodes),
                "source": snapshot.run_state.map.source,
            }
            payload["map"] = {
                key: value
                for key, value in map_payload.items()
                if value not in (None, [], "")
            }
        return payload

    @staticmethod
    def _preferred_description_text(item: Any) -> str | None:
        move_description = getattr(item, "move_description", None)
        if isinstance(move_description, str) and move_description:
            return move_description
        description = getattr(item, "description", None)
        if isinstance(description, str) and description:
            return description
        return None

    @staticmethod
    def _summarize_glossary(glossary: Any) -> list[dict[str, str]]:
        if not isinstance(glossary, list):
            return []
        payload: list[dict[str, str]] = []
        for item in glossary[:4]:
            glossary_id = getattr(item, "glossary_id", None)
            display_text = getattr(item, "display_text", None)
            if not isinstance(glossary_id, str) or not glossary_id:
                continue
            if not isinstance(display_text, str) or not display_text:
                continue
            entry = {"id": glossary_id, "text": display_text}
            hint = getattr(item, "hint", None)
            if isinstance(hint, str) and hint:
                entry["hint"] = hint
            payload.append(entry)
        return payload
