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
        return PolicyDecision(
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

    def _build_messages(self, snapshot: DecisionSnapshot, legal_actions: list[LegalAction]) -> list[dict[str, str]]:
        system_prompt = (
            "你是 Slay the Spire 2 自动打牌 agent。"
            "只能从给定 legal actions 中选择一个 action_id。"
            "必须返回 JSON，字段为 action_id、reason、halt。"
            "如果你认为当前不应继续自动操作，可以返回 halt=true 且 action_id=null。"
        )
        user_payload = {
            "snapshot": self._summarize_snapshot(snapshot),
            "legal_actions": [self._summarize_action(action) for action in legal_actions],
            "output_schema": {
                "action_id": "string|null",
                "reason": "string",
                "halt": "boolean",
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
        if action_id is not None and not isinstance(action_id, str):
            raise ChatCompletionsParseError("action_id must be a string or null")
        if not isinstance(reason, str) or not reason.strip():
            raise ChatCompletionsParseError("reason must be a non-empty string")
        if not isinstance(halt, bool):
            raise ChatCompletionsParseError("halt must be a boolean")
        return {
            "action_id": action_id,
            "reason": reason.strip(),
            "halt": halt,
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
        return {
            "action_id": action.action_id,
            "type": action.type,
            "label": action.label,
            "params": to_dict(action.params),
            "target_constraints": to_dict(action.target_constraints),
        }

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
                "hand": [to_dict(card) for card in snapshot.player.hand],
                "relics": list(snapshot.player.relics),
                "potions": list(snapshot.player.potions),
            }
        if snapshot.enemies:
            payload["enemies"] = [to_dict(enemy) for enemy in snapshot.enemies]
        if snapshot.rewards:
            payload["rewards"] = list(snapshot.rewards)
        if snapshot.map_nodes:
            payload["map_nodes"] = list(snapshot.map_nodes)
        return payload
