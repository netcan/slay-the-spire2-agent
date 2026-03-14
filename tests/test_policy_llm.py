from __future__ import annotations

import json
import unittest
from unittest.mock import patch
from urllib.error import URLError

from sts2_agent.models import CardView, DecisionSnapshot, EnemyState, LegalAction, PlayerState
from sts2_agent.policy import (
    ChatCompletionsConfig,
    ChatCompletionsParseError,
    ChatCompletionsPolicy,
    ChatCompletionsRequestError,
    ChatCompletionsTimeoutError,
)


class FakeHttpResponse:
    def __init__(self, payload):
        self._payload = payload

    def read(self) -> bytes:
        return json.dumps(self._payload, ensure_ascii=False).encode("utf-8")

    def __enter__(self):
        return self

    def __exit__(self, exc_type, exc, tb) -> None:
        return None


def build_snapshot() -> DecisionSnapshot:
    return DecisionSnapshot(
        session_id="sess-live1234",
        decision_id="dec-live1234",
        state_version=7,
        phase="combat",
        player=PlayerState(
            hp=80,
            max_hp=80,
            block=0,
            energy=3,
            gold=99,
            hand=[CardView(card_id="card-1", name="防御", cost=1, playable=True)],
            relics=["燃烧之血"],
            potions=[],
        ),
        enemies=[EnemyState(enemy_id="1", name="小啃兽", hp=46, max_hp=46, block=0, intent="unknown")],
        terminal=False,
    )


def build_actions() -> list[LegalAction]:
    return [
        LegalAction(action_id="act-1", type="play_card", label="Play 防御", params={"card_id": "card-1"}, target_constraints=[], metadata={}),
        LegalAction(action_id="act-2", type="end_turn", label="End Turn", params={}, target_constraints=[], metadata={}),
    ]


class ChatCompletionsPolicyTests(unittest.TestCase):
    def setUp(self) -> None:
        self.policy = ChatCompletionsPolicy(
            ChatCompletionsConfig(
                base_url="http://127.0.0.1:8080/v1",
                model="test-model",
                timeout_seconds=3.0,
            )
        )

    def test_policy_returns_structured_decision(self) -> None:
        response_payload = {
            "choices": [
                {
                    "message": {
                        "content": json.dumps(
                            {"action_id": "act-1", "reason": "先出防御更稳", "halt": False},
                            ensure_ascii=False,
                        )
                    }
                }
            ]
        }

        with patch("sts2_agent.policy.llm.urlopen", return_value=FakeHttpResponse(response_payload)):
            decision = self.policy.decide(build_snapshot(), build_actions())

        self.assertEqual(decision.action_id, "act-1")
        self.assertFalse(decision.halt)
        self.assertEqual(decision.metadata["provider"], "chat_completions")
        self.assertIn("raw_response_text", decision.metadata)

    def test_policy_allows_halt_true(self) -> None:
        response_payload = {
            "choices": [
                {
                    "message": {
                        "content": "{\"action_id\": null, \"reason\": \"交还人工\", \"halt\": true}"
                    }
                }
            ]
        }

        with patch("sts2_agent.policy.llm.urlopen", return_value=FakeHttpResponse(response_payload)):
            decision = self.policy.decide(build_snapshot(), build_actions())

        self.assertIsNone(decision.action_id)
        self.assertTrue(decision.halt)

    def test_policy_accepts_json_inside_markdown_fence(self) -> None:
        response_payload = {
            "choices": [
                {
                    "message": {
                        "content": "```json\n{\"action_id\": \"act-1\", \"reason\": \"先出防御\", \"halt\": false}\n```"
                    }
                }
            ]
        }

        with patch("sts2_agent.policy.llm.urlopen", return_value=FakeHttpResponse(response_payload)):
            decision = self.policy.decide(build_snapshot(), build_actions())

        self.assertEqual(decision.action_id, "act-1")
        self.assertFalse(decision.halt)

    def test_policy_accepts_json_embedded_in_extra_text(self) -> None:
        response_payload = {
            "choices": [
                {
                    "message": {
                        "content": "我建议这样操作：\n{\"action_id\": \"act-1\", \"reason\": \"先出防御\", \"halt\": false}\n这样更稳。"
                    }
                }
            ]
        }

        with patch("sts2_agent.policy.llm.urlopen", return_value=FakeHttpResponse(response_payload)):
            decision = self.policy.decide(build_snapshot(), build_actions())

        self.assertEqual(decision.action_id, "act-1")
        self.assertEqual(decision.reason, "先出防御")
        self.assertFalse(decision.halt)

    def test_policy_raises_timeout(self) -> None:
        with patch("sts2_agent.policy.llm.urlopen", side_effect=TimeoutError()):
            with self.assertRaises(ChatCompletionsTimeoutError):
                self.policy.decide(build_snapshot(), build_actions())

    def test_policy_rejects_invalid_json(self) -> None:
        response_payload = {"choices": [{"message": {"content": "not json"}}]}
        with patch("sts2_agent.policy.llm.urlopen", return_value=FakeHttpResponse(response_payload)):
            with self.assertRaises(ChatCompletionsParseError):
                self.policy.decide(build_snapshot(), build_actions())

    def test_policy_rejects_missing_required_fields(self) -> None:
        response_payload = {"choices": [{"message": {"content": "{\"action_id\":\"act-1\"}"}}]}
        with patch("sts2_agent.policy.llm.urlopen", return_value=FakeHttpResponse(response_payload)):
            with self.assertRaises(ChatCompletionsParseError):
                self.policy.decide(build_snapshot(), build_actions())

    def test_policy_maps_network_errors(self) -> None:
        with patch("sts2_agent.policy.llm.urlopen", side_effect=URLError("refused")):
            with self.assertRaises(ChatCompletionsRequestError):
                self.policy.decide(build_snapshot(), build_actions())


if __name__ == "__main__":
    unittest.main()
