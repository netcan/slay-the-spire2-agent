from __future__ import annotations

from dataclasses import asdict, dataclass, field, is_dataclass
from enum import StrEnum
from typing import Any


class Phase(StrEnum):
    COMBAT = "combat"
    REWARD = "reward"
    MAP = "map"
    MENU = "menu"
    TERMINAL = "terminal"
    UNKNOWN = "unknown"


class ActionType(StrEnum):
    PLAY_CARD = "play_card"
    END_TURN = "end_turn"
    CHOOSE_REWARD = "choose_reward"
    ADVANCE_REWARD = "advance_reward"
    CHOOSE_MAP_NODE = "choose_map_node"
    USE_POTION = "use_potion"
    SKIP_REWARD = "skip_reward"


class ActionStatus(StrEnum):
    ACCEPTED = "accepted"
    REJECTED = "rejected"
    INTERRUPTED = "interrupted"


@dataclass(slots=True)
class DescriptionVariable:
    key: str
    value: int | None = None
    source: str | None = None
    placeholder: str | None = None


@dataclass(slots=True)
class GlossaryAnchor:
    glossary_id: str
    display_text: str
    hint: str | None = None
    source: str | None = None


@dataclass(slots=True)
class CardView:
    card_id: str
    name: str
    cost: int
    playable: bool = True
    instance_card_id: str | None = None
    canonical_card_id: str | None = None
    description: str | None = None
    cost_for_turn: int | None = None
    upgraded: bool | None = None
    target_type: str | None = None
    card_type: str | None = None
    rarity: str | None = None
    traits: list[str] = field(default_factory=list)
    keywords: list[str] = field(default_factory=list)
    description_raw: str | None = None
    description_rendered: str | None = None
    description_quality: str | None = None
    description_source: str | None = None
    description_vars: list[DescriptionVariable] = field(default_factory=list)
    glossary: list[GlossaryAnchor] = field(default_factory=list)


@dataclass(slots=True)
class PowerView:
    power_id: str
    name: str
    amount: int | None = None
    description: str | None = None
    canonical_power_id: str | None = None
    description_raw: str | None = None
    description_rendered: str | None = None
    description_quality: str | None = None
    description_source: str | None = None
    description_vars: list[DescriptionVariable] = field(default_factory=list)
    glossary: list[GlossaryAnchor] = field(default_factory=list)


@dataclass(slots=True)
class RunMapState:
    current_coord: str | None = None
    current_node_type: str | None = None
    reachable_nodes: list[str] = field(default_factory=list)
    source: str | None = None


@dataclass(slots=True)
class RunState:
    act: int | None = None
    floor: int | None = None
    current_room_type: str | None = None
    current_location_type: str | None = None
    current_act_index: int | None = None
    ascension_level: int | None = None
    map: RunMapState | None = None


@dataclass(slots=True)
class PlayerState:
    hp: int
    max_hp: int
    block: int
    energy: int
    gold: int
    hand: list[CardView] = field(default_factory=list)
    draw_pile: int = 0
    discard_pile: int = 0
    exhaust_pile: int = 0
    relics: list[str] = field(default_factory=list)
    potions: list[str] = field(default_factory=list)
    powers: list[PowerView] = field(default_factory=list)


@dataclass(slots=True)
class EnemyState:
    enemy_id: str
    name: str
    hp: int
    max_hp: int
    block: int
    intent: str
    is_alive: bool = True
    instance_enemy_id: str | None = None
    canonical_enemy_id: str | None = None
    intent_raw: str | None = None
    intent_type: str | None = None
    intent_damage: int | None = None
    intent_hits: int | None = None
    intent_block: int | None = None
    intent_effects: list[str] = field(default_factory=list)
    powers: list[PowerView] = field(default_factory=list)


@dataclass(slots=True)
class LegalAction:
    action_id: str
    type: str
    label: str
    params: dict[str, Any] = field(default_factory=dict)
    target_constraints: list[str] = field(default_factory=list)
    metadata: dict[str, Any] = field(default_factory=dict)


@dataclass(slots=True)
class DecisionSnapshot:
    session_id: str
    decision_id: str
    state_version: int
    phase: str
    player: PlayerState | None = None
    enemies: list[EnemyState] = field(default_factory=list)
    rewards: list[str] = field(default_factory=list)
    map_nodes: list[str] = field(default_factory=list)
    terminal: bool = False
    metadata: dict[str, Any] = field(default_factory=dict)
    run_state: RunState | None = None


@dataclass(slots=True)
class ActionSubmission:
    session_id: str
    decision_id: str
    state_version: int
    action_id: str
    args: dict[str, Any] = field(default_factory=dict)


@dataclass(slots=True)
class ActionResult:
    status: str
    session_id: str
    decision_id: str
    state_version: int
    accepted_action_id: str | None = None
    error_code: str | None = None
    message: str = ""
    terminal: bool = False
    metadata: dict[str, Any] = field(default_factory=dict)


@dataclass(slots=True)
class PolicyDecision:
    action_id: str | None
    reason: str
    halt: bool = False
    metadata: dict[str, Any] = field(default_factory=dict)


@dataclass(slots=True)
class TraceEntry:
    session_id: str
    decision_id: str
    state_version: int
    phase: str
    legal_actions: list[dict[str, Any]]
    observation: dict[str, Any]
    policy_output: dict[str, Any]
    bridge_result: dict[str, Any]
    step_index: int = 0
    current_turn_index: int = 0
    actions_this_turn: int = 0
    total_actions: int = 0
    waiting_for_player_turn: bool = False
    phase_kind: str = ""
    step_kind: str = ""
    transition_elapsed_seconds: float = 0.0
    transition_attempt: int = 0
    reward_actions_taken: int = 0
    map_actions_taken: int = 0
    non_combat_steps: int = 0
    transition_wait_steps: int = 0
    next_combat_entered: bool = False
    is_final_step: bool = False
    stop_reason: str = ""
    battle_stop_reason: str = ""
    interrupted: bool = False
    timestamp: str = ""


@dataclass(slots=True)
class RunSummary:
    session_id: str
    completed: bool
    interrupted: bool
    decisions: int
    trace_path: str | None = None
    reason: str = ""
    turn_completed: bool = False
    actions_this_turn: int = 0
    battle_completed: bool = False
    turns_completed: int = 0
    total_actions: int = 0
    current_turn_index: int = 0
    reward_actions_taken: int = 0
    map_actions_taken: int = 0
    non_combat_steps: int = 0
    transition_wait_steps: int = 0
    next_combat_entered: bool = False
    ended_by: str = ""


def to_dict(value: Any) -> Any:
    if isinstance(value, StrEnum):
        return value.value
    if is_dataclass(value):
        return {key: to_dict(val) for key, val in asdict(value).items()}
    if isinstance(value, dict):
        return {key: to_dict(val) for key, val in value.items()}
    if isinstance(value, list):
        return [to_dict(item) for item in value]
    return value
