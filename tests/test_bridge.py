from __future__ import annotations

import io
import json
import unittest
from unittest.mock import patch
from urllib.error import HTTPError, URLError

from sts2_agent.bridge import (
    BridgeSession,
    HttpGameBridge,
    HttpGameBridgeConfig,
    InvalidPayloadError,
    MockGameBridge,
    RemoteBridgeError,
    StaleActionError,
)
from sts2_agent.models import ActionSubmission


class MockBridgeTests(unittest.TestCase):
    def setUp(self) -> None:
        self.bridge = MockGameBridge()
        self.session = self.bridge.attach_or_start()

    def test_combat_snapshot_exposes_expected_actions(self) -> None:
        snapshot = self.bridge.get_snapshot(self.session.session_id)
        actions = self.bridge.get_legal_actions(self.session.session_id)

        self.assertEqual(snapshot.phase, "combat")
        self.assertEqual(len(actions), 4)
        self.assertEqual({action.type for action in actions}, {"play_card", "use_potion", "end_turn"})
        self.assertTrue(any(card.description for card in snapshot.player.hand))
        self.assertTrue(all(not hasattr(card, "description_quality") for card in snapshot.player.hand))
        self.assertEqual(snapshot.player.draw_pile, len(snapshot.player.draw_pile_cards))
        self.assertEqual(snapshot.player.discard_pile, len(snapshot.player.discard_pile_cards))
        self.assertEqual(snapshot.player.exhaust_pile, len(snapshot.player.exhaust_pile_cards))
        self.assertTrue(snapshot.player.draw_pile_cards[0].description)
        self.assertTrue(snapshot.player.discard_pile_cards[0].glossary)
        self.assertEqual(snapshot.enemies[0].move_name, "Chomp")
        self.assertEqual(snapshot.enemies[0].move_glossary[0].glossary_id, "damage")
        self.assertIn("beast", snapshot.enemies[0].traits)
        self.assertIn("strength", snapshot.enemies[0].keywords)

    def test_bridge_rejects_stale_action_without_state_mutation(self) -> None:
        snapshot = self.bridge.get_snapshot(self.session.session_id)
        first_action = self.bridge.get_legal_actions(self.session.session_id)[0]

        self.bridge.submit_action(
            ActionSubmission(
                session_id=snapshot.session_id,
                decision_id=snapshot.decision_id,
                state_version=snapshot.state_version,
                action_id=first_action.action_id,
            )
        )

        with self.assertRaises(StaleActionError):
            self.bridge.submit_action(
                ActionSubmission(
                    session_id=snapshot.session_id,
                    decision_id=snapshot.decision_id,
                    state_version=snapshot.state_version,
                    action_id=first_action.action_id,
                )
            )

        latest = self.bridge.get_snapshot(self.session.session_id)
        self.assertEqual(latest.state_version, 1)
        self.assertEqual(latest.phase, "reward")

    def test_bridge_rejects_invalid_action(self) -> None:
        snapshot = self.bridge.get_snapshot(self.session.session_id)
        with self.assertRaises(InvalidPayloadError):
            self.bridge.submit_action(
                ActionSubmission(
                    session_id=snapshot.session_id,
                    decision_id=snapshot.decision_id,
                    state_version=snapshot.state_version,
                    action_id="act-not-legal",
                )
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


def make_http_error(url: str, payload: dict[str, object]) -> HTTPError:
    return HTTPError(
        url=url,
        code=409,
        msg="conflict",
        hdrs=None,
        fp=io.BytesIO(json.dumps(payload, ensure_ascii=False).encode("utf-8")),
    )


class HttpBridgeTests(unittest.TestCase):
    def test_attach_or_start_rejects_unreachable_bridge(self) -> None:
        bridge = HttpGameBridge(HttpGameBridgeConfig(base_url="http://127.0.0.1:17654"))
        with patch("sts2_agent.bridge.http.urlopen", side_effect=URLError("refused")):
            with self.assertRaises(RemoteBridgeError) as ctx:
                bridge.attach_or_start()

        self.assertEqual(ctx.exception.error_code, "bridge_unreachable")

    def test_http_bridge_reads_snapshot_actions_and_accepted_apply(self) -> None:
        bridge = HttpGameBridge(HttpGameBridgeConfig(base_url="http://127.0.0.1:17654"))
        calls = iter(
            [
                {"healthy": True},
                {
                    "session_id": "sess-live1234",
                    "decision_id": "dec-live1234",
                    "state_version": 7,
                    "phase": "combat",
                    "player": {"hp": 80, "max_hp": 80, "block": 0, "energy": 3, "gold": 99, "hand": []},
                    "enemies": [],
                    "rewards": [],
                    "map_nodes": [],
                    "terminal": False,
                    "metadata": {"source": "runtime"},
                },
                {
                    "session_id": "sess-live1234",
                    "decision_id": "dec-live1234",
                    "state_version": 7,
                    "phase": "combat",
                    "player": {"hp": 80, "max_hp": 80, "block": 0, "energy": 3, "gold": 99, "hand": []},
                    "enemies": [],
                    "rewards": [],
                    "map_nodes": [],
                    "terminal": False,
                    "metadata": {"source": "runtime"},
                },
                [
                    {
                        "action_id": "act-live1234",
                        "type": "end_turn",
                        "label": "End Turn",
                        "params": {},
                        "target_constraints": [],
                        "metadata": {},
                    }
                ],
                {
                    "request_id": "req-live",
                    "decision_id": "dec-live1234",
                    "action_id": "act-live1234",
                    "status": "accepted",
                    "message": "ok",
                    "metadata": {"state_version": 8, "phase": "combat"},
                },
            ]
        )

        def fake_urlopen(request, timeout=0):
            return FakeHttpResponse(next(calls))

        with patch("sts2_agent.bridge.http.urlopen", side_effect=fake_urlopen):
            session = bridge.attach_or_start()
            snapshot = bridge.get_snapshot(session.session_id)
            actions = bridge.get_legal_actions(session.session_id)
            result = bridge.submit_action(
                ActionSubmission(
                    session_id=session.session_id,
                    decision_id=snapshot.decision_id,
                    state_version=snapshot.state_version,
                    action_id=actions[0].action_id,
                )
            )

        self.assertEqual(session.session_id, "sess-live1234")
        self.assertEqual(snapshot.phase, "combat")
        self.assertEqual(len(actions), 1)
        self.assertEqual(result.status, "accepted")
        self.assertEqual(result.accepted_action_id, "act-live1234")
        self.assertEqual(result.state_version, 8)

    def test_http_bridge_maps_stale_decision(self) -> None:
        bridge = HttpGameBridge(HttpGameBridgeConfig(base_url="http://127.0.0.1:17654"))
        bridge._sessions["sess-live1234"] = BridgeSession(session_id="sess-live1234", scenario="live", state_version=7)

        with patch(
            "sts2_agent.bridge.http.urlopen",
            side_effect=make_http_error(
                "http://127.0.0.1:17654/apply",
                {
                    "status": "rejected",
                    "error_code": "stale_decision",
                    "message": "stale",
                },
            ),
        ):
            with self.assertRaises(StaleActionError):
                bridge.submit_action(
                    ActionSubmission(
                        session_id="sess-live1234",
                        decision_id="dec-live1234",
                        state_version=7,
                        action_id="act-live1234",
                    )
                )

    def test_http_bridge_maps_illegal_action(self) -> None:
        bridge = HttpGameBridge(HttpGameBridgeConfig(base_url="http://127.0.0.1:17654"))
        bridge._sessions["sess-live1234"] = BridgeSession(session_id="sess-live1234", scenario="live", state_version=7)

        with patch(
            "sts2_agent.bridge.http.urlopen",
            side_effect=make_http_error(
                "http://127.0.0.1:17654/apply",
                {
                    "status": "rejected",
                    "error_code": "illegal_action",
                    "message": "illegal",
                },
            ),
        ):
            with self.assertRaises(InvalidPayloadError):
                bridge.submit_action(
                    ActionSubmission(
                        session_id="sess-live1234",
                        decision_id="dec-live1234",
                        state_version=7,
                        action_id="act-illegal",
                    )
                )

    def test_decode_snapshot_accepts_rich_runtime_fields(self) -> None:
        payload = {
            "session_id": "sess-live1234",
            "decision_id": "dec-live1234",
            "state_version": 7,
            "phase": "combat",
            "player": {
                "hp": 80,
                "max_hp": 80,
                "block": 4,
                "energy": 3,
                "gold": 99,
                "draw_pile": 10,
                "discard_pile": 2,
                "exhaust_pile": 0,
                "relics": ["Burning Blood"],
                "potions": ["Strength Potion"],
                "powers": [
                    {
                        "power_id": "metallicize",
                        "name": "Metallicize",
                        "amount": 3,
                        "description": "At end of turn gain 3 **Block**.",
                        "glossary": [{"glossary_id": "block", "display_text": "Block", "hint": "Prevents damage until next turn.", "source": "description_text"}],
                        "canonical_power_id": "metallicize",
                    }
                ],
                "hand": [
                    {
                        "card_id": "strike_red#0",
                        "name": "Strike",
                        "cost": 1,
                        "playable": True,
                        "instance_card_id": "strike_red#0",
                        "canonical_card_id": "strike_red",
                        "description": "Deal 6 **damage**.",
                        "glossary": [{"glossary_id": "damage", "display_text": "Damage", "hint": "Reduces HP.", "source": "description_text"}],
                        "cost_for_turn": 1,
                        "upgraded": False,
                        "target_type": "AnyEnemy",
                        "card_type": "Attack",
                        "rarity": "Starter",
                        "traits": ["starter"],
                        "keywords": ["damage"],
                    }
                ],
                "draw_pile_cards": [
                    {
                        "card_id": "pommel_strike#0",
                        "name": "Pommel Strike",
                        "cost": 1,
                        "playable": False,
                        "instance_card_id": "pommel_strike#0",
                        "canonical_card_id": "pommel_strike",
                        "description": "Deal 9 **damage**. Draw 1 card.",
                        "glossary": [{"glossary_id": "damage", "display_text": "Damage", "hint": "Reduces HP.", "source": "description_text"}],
                        "cost_for_turn": 1,
                        "upgraded": False,
                        "target_type": "AnyEnemy",
                        "card_type": "Attack",
                        "rarity": "Common",
                        "traits": ["draw"],
                        "keywords": ["damage", "draw"],
                    }
                ],
                "discard_pile_cards": [
                    {
                        "card_id": "defend_red#discard",
                        "name": "Defend",
                        "cost": 1,
                        "playable": False,
                        "instance_card_id": "defend_red#discard",
                        "canonical_card_id": "defend_red",
                        "description": "Gain 5 **Block**.",
                        "glossary": [{"glossary_id": "block", "display_text": "Block", "hint": "Prevents damage until next turn.", "source": "description_text"}],
                        "cost_for_turn": 1,
                        "upgraded": False,
                        "target_type": "Self",
                        "card_type": "Skill",
                        "rarity": "Starter",
                        "traits": ["starter"],
                        "keywords": ["block"],
                    },
                    {
                        "card_id": "anger#discard",
                        "name": "Anger",
                        "cost": 0,
                        "playable": False,
                        "instance_card_id": "anger#discard",
                        "canonical_card_id": "anger",
                        "description": "Deal 6 **damage**.",
                        "glossary": [{"glossary_id": "damage", "display_text": "Damage", "hint": "Reduces HP.", "source": "description_text"}],
                        "cost_for_turn": 0,
                        "upgraded": False,
                        "target_type": "AnyEnemy",
                        "card_type": "Attack",
                        "rarity": "Common",
                        "traits": [],
                        "keywords": ["damage"],
                    }
                ],
                "exhaust_pile_cards": [],
            },
            "enemies": [
                {
                    "enemy_id": "jaw_worm_1",
                    "name": "Jaw Worm",
                    "hp": 38,
                    "max_hp": 42,
                    "block": 0,
                    "intent": "attack_11",
                    "is_alive": True,
                    "instance_enemy_id": "jaw_worm_1",
                    "canonical_enemy_id": "jaw_worm",
                    "intent_raw": "Attack",
                    "intent_type": "attack",
                    "intent_damage": 11,
                    "intent_hits": 1,
                    "intent_effects": ["weak"],
                    "move_name": "Thrash",
                    "move_description": "Deal 11 **damage** and gain 6 **Block**.",
                    "move_glossary": [
                        {"glossary_id": "damage", "display_text": "Damage", "hint": "Reduces HP.", "source": "description_text"},
                        {"glossary_id": "block", "display_text": "Block", "hint": "Prevents damage until next turn.", "source": "description_text"}
                    ],
                    "traits": ["beast"],
                    "keywords": ["damage", "block", "weak", "strength", "beast"],
                    "powers": [
                        {
                            "power_id": "strength",
                            "name": "Strength",
                            "amount": 3,
                            "description": "Increase attack damage.",
                            "glossary": [{"glossary_id": "strength", "display_text": "Strength", "hint": "Increases attack damage.", "source": "canonical_id"}],
                            "canonical_power_id": "strength",
                        }
                    ],
                }
            ],
            "rewards": [],
            "map_nodes": [],
            "terminal": False,
            "metadata": {"source": "runtime"},
            "run_state": {
                "act": 1,
                "floor": 3,
                "current_room_type": "CombatRoom",
                "current_location_type": "Act1",
                "current_act_index": 0,
                "ascension_level": 0,
                "map": {
                    "current_coord": "1,2",
                    "current_node_type": "monster",
                    "reachable_nodes": ["monster@1,3", "elite@2,3"],
                    "source": "current_map_point",
                },
            },
        }

        snapshot = HttpGameBridge._decode_snapshot(payload)

        self.assertEqual(snapshot.player.hand[0].canonical_card_id, "strike_red")
        self.assertEqual(snapshot.player.hand[0].description, "Deal 6 **damage**.")
        self.assertEqual(snapshot.player.hand[0].glossary[0].glossary_id, "damage")
        self.assertEqual(snapshot.player.draw_pile_cards[0].canonical_card_id, "pommel_strike")
        self.assertEqual(snapshot.player.draw_pile_cards[0].description, "Deal 9 **damage**. Draw 1 card.")
        self.assertEqual(snapshot.player.discard_pile_cards[0].glossary[0].glossary_id, "block")
        self.assertEqual(snapshot.player.exhaust_pile_cards, [])
        self.assertEqual(snapshot.player.powers[0].name, "Metallicize")
        self.assertEqual(snapshot.enemies[0].intent_type, "attack")
        self.assertEqual(snapshot.enemies[0].powers[0].canonical_power_id, "strength")
        self.assertEqual(snapshot.enemies[0].powers[0].glossary[0].glossary_id, "strength")
        self.assertEqual(snapshot.enemies[0].move_name, "Thrash")
        self.assertEqual(snapshot.enemies[0].move_glossary[1].glossary_id, "block")
        self.assertEqual(snapshot.enemies[0].traits, ["beast"])
        self.assertIn("strength", snapshot.enemies[0].keywords)
        self.assertEqual(snapshot.run_state.act, 1)
        self.assertEqual(snapshot.run_state.map.reachable_nodes, ["monster@1,3", "elite@2,3"])

    def test_decode_snapshot_keeps_backward_compatibility_when_rich_fields_missing(self) -> None:
        payload = {
            "session_id": "sess-old",
            "decision_id": "dec-old",
            "state_version": 1,
            "phase": "combat",
            "player": {"hp": 80, "max_hp": 80, "block": 0, "energy": 3, "gold": 99, "hand": []},
            "enemies": [{"enemy_id": "enemy-1", "name": "Louse", "hp": 10, "max_hp": 10, "block": 0, "intent": "attack"}],
            "rewards": [],
            "map_nodes": [],
            "terminal": False,
            "metadata": {},
        }

        snapshot = HttpGameBridge._decode_snapshot(payload)

        self.assertIsNone(snapshot.run_state)
        self.assertEqual(snapshot.player.powers, [])
        self.assertEqual(snapshot.enemies[0].intent_damage, None)
        self.assertEqual(snapshot.enemies[0].powers, [])
        self.assertIsNone(snapshot.enemies[0].move_name)
        self.assertEqual(snapshot.enemies[0].move_glossary, [])
        self.assertEqual(snapshot.enemies[0].traits, [])
        self.assertEqual(snapshot.enemies[0].keywords, [])
        self.assertEqual(snapshot.player.hand, [])
        self.assertEqual(snapshot.player.draw_pile_cards, [])
        self.assertEqual(snapshot.player.discard_pile_cards, [])
        self.assertEqual(snapshot.player.exhaust_pile_cards, [])


if __name__ == "__main__":
    unittest.main()
