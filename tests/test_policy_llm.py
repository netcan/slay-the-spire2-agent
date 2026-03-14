from __future__ import annotations

import json
import unittest
from unittest.mock import patch
from urllib.error import URLError

from sts2_agent.models import (
    CardView,
    DecisionSnapshot,
    EnemyState,
    GlossaryAnchor,
    LegalAction,
    PlayerState,
    PowerView,
    RunMapState,
    RunState,
)
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
            hand=[
                CardView(
                    card_id="card-1",
                    name="防御",
                    cost=1,
                    playable=True,
                    canonical_card_id="defend_red",
                    description="获得5点**格挡**。",
                    glossary=[GlossaryAnchor(glossary_id="block", display_text="格挡", hint="在下个回合前，阻挡伤害。", source="description_text")],
                    cost_for_turn=1,
                    upgraded=False,
                    target_type="Self",
                    card_type="Skill",
                    rarity="Starter",
                    traits=["starter"],
                    keywords=["block"],
                )
            ],
            draw_pile=1,
            discard_pile=1,
            exhaust_pile=0,
            draw_pile_cards=[
                CardView(
                    card_id="card-draw-1",
                    name="抽牌攻击",
                    cost=1,
                    playable=False,
                    canonical_card_id="pommel_strike",
                    description="造成9点**伤害**，抽1张牌。",
                    glossary=[GlossaryAnchor(glossary_id="damage", display_text="伤害", hint="会降低目标生命值。", source="description_text")],
                    cost_for_turn=1,
                    upgraded=False,
                    target_type="AnyEnemy",
                    card_type="Attack",
                    rarity="Common",
                    traits=["draw"],
                    keywords=["damage", "draw"],
                )
            ],
            discard_pile_cards=[
                CardView(
                    card_id="card-discard-1",
                    name="打击",
                    cost=1,
                    playable=False,
                    canonical_card_id="strike_red",
                    description="造成6点**伤害**。",
                    glossary=[GlossaryAnchor(glossary_id="damage", display_text="伤害", hint="会降低目标生命值。", source="description_text")],
                    cost_for_turn=1,
                    upgraded=False,
                    target_type="AnyEnemy",
                    card_type="Attack",
                    rarity="Starter",
                    traits=["starter"],
                    keywords=["damage"],
                )
            ],
            relics=["燃烧之血"],
            potions=[],
            powers=[
                PowerView(
                    power_id="metallicize",
                    name="金属化",
                    amount=3,
                    description="回合结束时获得3点**格挡**。",
                    glossary=[GlossaryAnchor(glossary_id="block", display_text="格挡", hint="在下个回合前，阻挡伤害。", source="description_text")],
                )
            ],
        ),
        enemies=[
            EnemyState(
                enemy_id="1",
                name="小啃兽",
                hp=46,
                max_hp=46,
                block=0,
                intent="unknown",
                canonical_enemy_id="jaw_worm",
                intent_raw="Attack",
                intent_type="attack",
                intent_damage=11,
                intent_hits=1,
                move_name="撕咬",
                move_description="造成11点**伤害**并获得6点**格挡**。",
                move_glossary=[
                    GlossaryAnchor(glossary_id="damage", display_text="伤害", hint="会降低目标生命值。", source="description_text"),
                    GlossaryAnchor(glossary_id="block", display_text="格挡", hint="在下个回合前，阻挡伤害。", source="description_text"),
                ],
                traits=["beast"],
                keywords=["damage", "block", "strength", "beast"],
                powers=[
                    PowerView(
                        power_id="strength",
                        name="力量",
                        amount=3,
                        description="增加攻击伤害。",
                        glossary=[GlossaryAnchor(glossary_id="strength", display_text="力量", hint="使攻击造成更多伤害。", source="canonical_id")],
                    )
                ],
            )
        ],
        terminal=False,
        run_state=RunState(
            act=1,
            floor=3,
            current_room_type="CombatRoom",
            current_location_type="Act1",
            current_act_index=0,
            ascension_level=0,
            map=RunMapState(current_coord="1,2", current_node_type="monster", reachable_nodes=["monster@1,3", "elite@2,3"], source="current_map_point"),
        ),
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

    def test_policy_persists_action_args_metadata(self) -> None:
        response_payload = {
            "choices": [
                {
                    "message": {
                        "content": json.dumps(
                            {
                                "action_id": "act-1",
                                "reason": "打击需要明确目标",
                                "halt": False,
                                "args": {"target_id": "enemy-2"},
                            },
                            ensure_ascii=False,
                        )
                    }
                }
            ]
        }

        with patch("sts2_agent.policy.llm.urlopen", return_value=FakeHttpResponse(response_payload)):
            decision = self.policy.decide(build_snapshot(), build_actions())

        self.assertEqual(decision.metadata["action_args"]["target_id"], "enemy-2")

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

    def test_policy_rejects_non_object_args(self) -> None:
        response_payload = {
            "choices": [
                {
                    "message": {
                        "content": "{\"action_id\":\"act-1\",\"reason\":\"bad args\",\"halt\":false,\"args\":\"enemy-1\"}"
                    }
                }
            ]
        }
        with patch("sts2_agent.policy.llm.urlopen", return_value=FakeHttpResponse(response_payload)):
            with self.assertRaises(ChatCompletionsParseError):
                self.policy.decide(build_snapshot(), build_actions())

    def test_policy_rejects_non_string_target_id(self) -> None:
        response_payload = {
            "choices": [
                {
                    "message": {
                        "content": "{\"action_id\":\"act-1\",\"reason\":\"bad args\",\"halt\":false,\"args\":{\"target_id\":1}}"
                    }
                }
            ]
        }
        with patch("sts2_agent.policy.llm.urlopen", return_value=FakeHttpResponse(response_payload)):
            with self.assertRaises(ChatCompletionsParseError):
                self.policy.decide(build_snapshot(), build_actions())

    def test_policy_maps_network_errors(self) -> None:
        with patch("sts2_agent.policy.llm.urlopen", side_effect=URLError("refused")):
            with self.assertRaises(ChatCompletionsRequestError):
                self.policy.decide(build_snapshot(), build_actions())

    def test_summarize_snapshot_includes_rich_runtime_fields(self) -> None:
        payload = self.policy._summarize_snapshot(build_snapshot())

        self.assertEqual(payload["player"]["hand"][0]["description"], "获得5点**格挡**。")
        self.assertEqual(payload["player"]["hand"][0]["glossary"][0]["id"], "block")
        self.assertEqual(payload["player"]["draw_pile_cards"][0]["canonical_card_id"], "pommel_strike")
        self.assertEqual(payload["player"]["discard_pile_cards"][0]["name"], "打击")
        self.assertEqual(payload["player"]["exhaust_pile_cards"], [])
        self.assertEqual(payload["player"]["powers"][0]["amount"], 3)
        self.assertEqual(payload["enemies"][0]["intent_damage"], 11)
        self.assertEqual(payload["enemies"][0]["move_name"], "撕咬")
        self.assertEqual(payload["enemies"][0]["move_glossary"][0]["id"], "damage")
        self.assertIn("beast", payload["enemies"][0]["traits"])
        self.assertEqual(payload["enemies"][0]["powers"][0]["name"], "力量")
        self.assertEqual(payload["run_state"]["map"]["current_coord"], "1,2")
        self.assertNotIn("intent_raw", payload["enemies"][0])

    def test_summarize_snapshot_hides_duplicate_move_name(self) -> None:
        snapshot = build_snapshot()
        snapshot.enemies[0].intent = "策略"
        snapshot.enemies[0].intent_raw = "策略"
        snapshot.enemies[0].intent_type = "debuff"
        snapshot.enemies[0].move_name = "策略"

        payload = self.policy._summarize_snapshot(snapshot)

        self.assertNotIn("move_name", payload["enemies"][0])
        self.assertEqual(payload["enemies"][0]["intent_type"], "debuff")

    def test_preferred_description_downgrades_template_rendered_text(self) -> None:
        card = CardView(
            card_id="c1",
            name="打击",
            cost=1,
            description="造成{Damage:diff()}点伤害。",
        )

        self.assertEqual(self.policy._preferred_description_text(card), "造成{Damage:diff()}点伤害。")

    def test_summarize_snapshot_handles_missing_rich_fields(self) -> None:
        snapshot = DecisionSnapshot(
            session_id="sess-old",
            decision_id="dec-old",
            state_version=1,
            phase="combat",
            player=PlayerState(hp=10, max_hp=10, block=0, energy=3, gold=0, hand=[CardView(card_id="c1", name="打击", cost=1)]),
            enemies=[EnemyState(enemy_id="e1", name="小史莱姆", hp=8, max_hp=8, block=0, intent="attack")],
            terminal=False,
        )

        payload = self.policy._summarize_snapshot(snapshot)

        self.assertNotIn("run_state", payload)
        self.assertEqual(payload["player"]["hand"][0]["name"], "打击")
        self.assertEqual(payload["player"]["draw_pile_cards"], [])
        self.assertEqual(payload["enemies"][0]["intent"], "attack")


if __name__ == "__main__":
    unittest.main()
