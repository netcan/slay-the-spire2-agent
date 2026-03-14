from __future__ import annotations

import time
from concurrent.futures import ThreadPoolExecutor, TimeoutError as FutureTimeoutError
from dataclasses import dataclass
from datetime import UTC, datetime
from pathlib import Path
from typing import Any

from sts2_agent.bridge import BridgeError, GameBridge, InvalidPayloadError, InterruptedSessionError, StaleActionError
from sts2_agent.models import ActionSubmission, PolicyDecision, RunSummary, TraceEntry, to_dict
from sts2_agent.policy import PolicyError
from sts2_agent.trace import JsonlTraceRecorder


@dataclass(slots=True)
class OrchestratorConfig:
    timeout_seconds: float = 2.0
    max_steps: int = 32
    max_actions_per_turn: int | None = None
    stop_after_player_turn: bool = True
    auto_end_turn_when_only_end_turn: bool = True
    state_sync_retries: int = 3
    stale_action_retries: int = 2
    max_turns_per_battle: int | None = None
    max_total_actions: int | None = None
    max_consecutive_failures: int = 6
    wait_for_next_player_turn_seconds: float = 30.0
    poll_interval_seconds: float = 0.5
    trace_dir: str = "traces"
    dry_run: bool = False


class AutoplayOrchestrator:
    def __init__(self, bridge: GameBridge, policy, config: OrchestratorConfig | None = None) -> None:
        self.bridge = bridge
        self.policy = policy
        self.config = config or OrchestratorConfig()
        self._stop_requested = False

    def request_stop(self) -> None:
        self._stop_requested = True

    def run(self, scenario: str = "combat_reward_map_terminal") -> RunSummary:
        session = self.bridge.attach_or_start(scenario=scenario)
        trace_path = Path(self.config.trace_dir) / f"{session.session_id}.jsonl"
        recorder = JsonlTraceRecorder(trace_path)
        total_actions = 0
        current_turn_actions = 0
        current_turn_index = 0
        turns_completed = 0
        current_turn_marker: object | None = None
        waiting_since: float | None = None
        pending_end_turn_transition: tuple[str, int] | None = None
        stale_action_attempts = 0
        consecutive_failures = 0
        step_index = 0

        while step_index < self.config.max_steps:
            snapshot, legal_actions = self._read_consistent_state(session.session_id)
            legal_actions = self._effective_legal_actions(snapshot, legal_actions)
            if pending_end_turn_transition is not None:
                if (snapshot.decision_id, snapshot.state_version) != pending_end_turn_transition:
                    pending_end_turn_transition = None
            player_turn = self._is_player_turn(snapshot)
            current_turn_marker, current_turn_index, current_turn_actions = self._update_turn_state(
                snapshot,
                player_turn,
                current_turn_marker,
                current_turn_index,
                current_turn_actions,
            )

            if not player_turn and current_turn_index > turns_completed:
                turns_completed = current_turn_index

            if snapshot.terminal:
                return self._finish(
                    session_id=session.session_id,
                    trace_path=trace_path,
                    reason="terminal_state_reached",
                    completed=True,
                    interrupted=False,
                    turn_completed=current_turn_index > 0,
                    battle_completed=True,
                    actions_this_turn=current_turn_actions,
                    turns_completed=turns_completed,
                    total_actions=total_actions,
                    current_turn_index=current_turn_index,
                )

            if self._stop_requested:
                try:
                    self.bridge.stop(session.session_id)
                except BridgeError:
                    pass
                return self._finish(
                    session_id=session.session_id,
                    trace_path=trace_path,
                    reason="manual_stop",
                    completed=False,
                    interrupted=True,
                    turn_completed=turns_completed > 0,
                    actions_this_turn=current_turn_actions,
                    turns_completed=turns_completed,
                    total_actions=total_actions,
                    current_turn_index=current_turn_index,
                )

            battle_stop_reason = self._battle_completion_reason(snapshot)
            if battle_stop_reason:
                if current_turn_index > turns_completed:
                    turns_completed = current_turn_index
                return self._finish(
                    session_id=session.session_id,
                    trace_path=trace_path,
                    reason=battle_stop_reason,
                    completed=True,
                    interrupted=False,
                    turn_completed=turns_completed > 0,
                    battle_completed=True,
                    actions_this_turn=current_turn_actions,
                    turns_completed=turns_completed,
                    total_actions=total_actions,
                    current_turn_index=current_turn_index,
                )

            budget_stop_reason = self._battle_budget_stop_reason(
                total_actions=total_actions,
                turns_completed=turns_completed,
                current_turn_index=current_turn_index,
                consecutive_failures=consecutive_failures,
            )
            if budget_stop_reason:
                effective_turns_completed = turns_completed
                if budget_stop_reason == "max_turns_per_battle" and current_turn_index > turns_completed:
                    effective_turns_completed = max(turns_completed, current_turn_index - 1)
                return self._finish(
                    session_id=session.session_id,
                    trace_path=trace_path,
                    reason=budget_stop_reason,
                    completed=False,
                    interrupted=True,
                    turn_completed=effective_turns_completed > 0,
                    actions_this_turn=current_turn_actions,
                    turns_completed=effective_turns_completed,
                    total_actions=total_actions,
                    current_turn_index=current_turn_index,
                )

            if pending_end_turn_transition is not None:
                outcome = self._handle_pending_end_turn_transition(
                    recorder=recorder,
                    snapshot=snapshot,
                    legal_actions=legal_actions,
                    step_index=step_index,
                    current_turn_index=current_turn_index,
                    current_turn_actions=current_turn_actions,
                    total_actions=total_actions,
                    turns_completed=turns_completed,
                    trace_path=trace_path,
                    waiting_since=waiting_since,
                )
                step_index = outcome["step_index"]
                waiting_since = outcome["waiting_since"]
                if outcome["summary"] is not None:
                    return outcome["summary"]
                continue

            if not player_turn:
                outcome = self._handle_non_player_turn(
                    recorder=recorder,
                    snapshot=snapshot,
                    legal_actions=legal_actions,
                    step_index=step_index,
                    current_turn_index=current_turn_index,
                    current_turn_actions=current_turn_actions,
                    total_actions=total_actions,
                    turns_completed=turns_completed,
                    trace_path=trace_path,
                    waiting_since=waiting_since,
                )
                step_index = outcome["step_index"]
                waiting_since = outcome["waiting_since"]
                if outcome["summary"] is not None:
                    return outcome["summary"]
                continue

            waiting_since = None

            preflight_summary = self._player_turn_preflight(
                session_id=session.session_id,
                trace_path=trace_path,
                legal_actions=legal_actions,
                current_turn_actions=current_turn_actions,
                current_turn_index=current_turn_index,
                turns_completed=turns_completed,
                total_actions=total_actions,
            )
            if preflight_summary is not None:
                return preflight_summary

            auto_end_result = self._handle_auto_end_turn(
                recorder=recorder,
                snapshot=snapshot,
                legal_actions=legal_actions,
                step_index=step_index,
                current_turn_index=current_turn_index,
                current_turn_actions=current_turn_actions,
                turns_completed=turns_completed,
                total_actions=total_actions,
                trace_path=trace_path,
                stale_action_attempts=stale_action_attempts,
                consecutive_failures=consecutive_failures,
                session_id=session.session_id,
            )
            if auto_end_result["summary"] is not None:
                return auto_end_result["summary"]
            if auto_end_result["consumed"]:
                step_index = auto_end_result["step_index"]
                current_turn_actions = auto_end_result["current_turn_actions"]
                total_actions = auto_end_result["total_actions"]
                stale_action_attempts = auto_end_result["stale_action_attempts"]
                consecutive_failures = auto_end_result["consecutive_failures"]
                pending_end_turn_transition = auto_end_result["pending_end_turn_transition"]
                continue

            action_result = self._run_player_step(
                recorder=recorder,
                snapshot=snapshot,
                legal_actions=legal_actions,
                step_index=step_index,
                current_turn_index=current_turn_index,
                current_turn_actions=current_turn_actions,
                turns_completed=turns_completed,
                total_actions=total_actions,
                stale_action_attempts=stale_action_attempts,
                consecutive_failures=consecutive_failures,
                trace_path=trace_path,
                session_id=session.session_id,
            )
            step_index = action_result["step_index"]
            current_turn_actions = action_result["current_turn_actions"]
            total_actions = action_result["total_actions"]
            stale_action_attempts = action_result["stale_action_attempts"]
            consecutive_failures = action_result["consecutive_failures"]
            pending_end_turn_transition = action_result["pending_end_turn_transition"]
            if action_result["summary"] is not None:
                return action_result["summary"]

        return self._finish(
            session_id=session.session_id,
            trace_path=trace_path,
            reason="max_steps_exceeded",
            completed=False,
            interrupted=True,
            turn_completed=turns_completed > 0,
            actions_this_turn=current_turn_actions,
            turns_completed=turns_completed,
            total_actions=total_actions,
            current_turn_index=current_turn_index,
        )

    def _handle_non_player_turn(
        self,
        *,
        recorder: JsonlTraceRecorder,
        snapshot,
        legal_actions,
        step_index: int,
        current_turn_index: int,
        current_turn_actions: int,
        total_actions: int,
        turns_completed: int,
        trace_path: Path,
        waiting_since: float | None,
    ) -> dict[str, object]:
        if self.config.stop_after_player_turn:
            return {
                "step_index": step_index,
                "waiting_since": waiting_since,
                "summary": self._finish(
                    session_id=snapshot.session_id,
                    trace_path=trace_path,
                    reason="phase_changed",
                    completed=total_actions > 0,
                    interrupted=total_actions == 0,
                    turn_completed=total_actions > 0,
                    battle_completed=False,
                    actions_this_turn=current_turn_actions,
                    turns_completed=turns_completed,
                    total_actions=total_actions,
                    current_turn_index=current_turn_index,
                ),
            }

        step_index += 1
        if waiting_since is None:
            waiting_since = time.monotonic()
        if time.monotonic() - waiting_since > self.config.wait_for_next_player_turn_seconds:
            self._record(
                recorder=recorder,
                snapshot=snapshot,
                legal_actions=legal_actions,
                policy_output=PolicyDecision(action_id=None, reason="waiting for next player turn", halt=True),
                bridge_result={"status": "waiting", "reason": "next_player_turn_timeout"},
                interrupted=True,
                step_index=step_index,
                current_turn_index=current_turn_index,
                actions_this_turn=current_turn_actions,
                total_actions=total_actions,
                waiting_for_player_turn=True,
                is_final_step=True,
                stop_reason="next_player_turn_timeout",
                battle_stop_reason="next_player_turn_timeout",
            )
            return {
                "step_index": step_index,
                "waiting_since": waiting_since,
                "summary": self._finish(
                    session_id=snapshot.session_id,
                    trace_path=trace_path,
                    reason="next_player_turn_timeout",
                    completed=False,
                    interrupted=True,
                    turn_completed=turns_completed > 0,
                    actions_this_turn=current_turn_actions,
                    turns_completed=turns_completed,
                    total_actions=total_actions,
                    current_turn_index=current_turn_index,
                ),
            }

        self._record(
            recorder=recorder,
            snapshot=snapshot,
            legal_actions=legal_actions,
            policy_output=PolicyDecision(action_id=None, reason="waiting for next player turn", halt=True),
            bridge_result={"status": "waiting", "reason": "enemy_turn_or_animation"},
            interrupted=False,
            step_index=step_index,
            current_turn_index=current_turn_index,
            actions_this_turn=current_turn_actions,
            total_actions=total_actions,
            waiting_for_player_turn=True,
            is_final_step=False,
            stop_reason="",
            battle_stop_reason="",
        )
        time.sleep(self.config.poll_interval_seconds)
        return {
            "step_index": step_index,
            "waiting_since": waiting_since,
            "summary": None,
        }

    def _handle_pending_end_turn_transition(
        self,
        *,
        recorder: JsonlTraceRecorder,
        snapshot,
        legal_actions,
        step_index: int,
        current_turn_index: int,
        current_turn_actions: int,
        total_actions: int,
        turns_completed: int,
        trace_path: Path,
        waiting_since: float | None,
    ) -> dict[str, object]:
        step_index += 1
        if waiting_since is None:
            waiting_since = time.monotonic()
        if time.monotonic() - waiting_since > self.config.wait_for_next_player_turn_seconds:
            self._record(
                recorder=recorder,
                snapshot=snapshot,
                legal_actions=legal_actions,
                policy_output=PolicyDecision(action_id=None, reason="waiting for end_turn transition", halt=True),
                bridge_result={"status": "waiting", "reason": "next_player_turn_timeout"},
                interrupted=True,
                step_index=step_index,
                current_turn_index=current_turn_index,
                actions_this_turn=current_turn_actions,
                total_actions=total_actions,
                waiting_for_player_turn=True,
                is_final_step=True,
                stop_reason="next_player_turn_timeout",
                battle_stop_reason="next_player_turn_timeout",
            )
            return {
                "step_index": step_index,
                "waiting_since": waiting_since,
                "summary": self._finish(
                    session_id=snapshot.session_id,
                    trace_path=trace_path,
                    reason="next_player_turn_timeout",
                    completed=False,
                    interrupted=True,
                    turn_completed=turns_completed > 0,
                    actions_this_turn=current_turn_actions,
                    turns_completed=turns_completed,
                    total_actions=total_actions,
                    current_turn_index=current_turn_index,
                ),
            }

        self._record(
            recorder=recorder,
            snapshot=snapshot,
            legal_actions=legal_actions,
            policy_output=PolicyDecision(action_id=None, reason="waiting for end_turn transition", halt=True),
            bridge_result={"status": "waiting", "reason": "pending_end_turn_transition"},
            interrupted=False,
            step_index=step_index,
            current_turn_index=current_turn_index,
            actions_this_turn=current_turn_actions,
            total_actions=total_actions,
            waiting_for_player_turn=True,
            is_final_step=False,
            stop_reason="",
            battle_stop_reason="",
        )
        time.sleep(self.config.poll_interval_seconds)
        return {
            "step_index": step_index,
            "waiting_since": waiting_since,
            "summary": None,
        }

    def _player_turn_preflight(
        self,
        *,
        session_id: str,
        trace_path: Path,
        legal_actions,
        current_turn_actions: int,
        current_turn_index: int,
        turns_completed: int,
        total_actions: int,
    ) -> RunSummary | None:
        if current_turn_actions >= self._max_actions_per_turn():
            return self._finish(
                session_id=session_id,
                trace_path=trace_path,
                reason="max_actions_per_turn",
                completed=False,
                interrupted=True,
                actions_this_turn=current_turn_actions,
                turns_completed=turns_completed,
                total_actions=total_actions,
                current_turn_index=current_turn_index,
            )
        if not legal_actions:
            return self._finish(
                session_id=session_id,
                trace_path=trace_path,
                reason="no_legal_actions",
                completed=total_actions > 0,
                interrupted=total_actions == 0,
                turn_completed=total_actions > 0,
                battle_completed=False,
                actions_this_turn=current_turn_actions,
                turns_completed=turns_completed,
                total_actions=total_actions,
                current_turn_index=current_turn_index,
            )
        return None

    def _handle_auto_end_turn(
        self,
        *,
        recorder: JsonlTraceRecorder,
        snapshot,
        legal_actions,
        step_index: int,
        current_turn_index: int,
        current_turn_actions: int,
        turns_completed: int,
        total_actions: int,
        trace_path: Path,
        stale_action_attempts: int,
        consecutive_failures: int,
        session_id: str,
    ) -> dict[str, object]:
        if not self._is_only_end_turn(legal_actions):
            return {
                "consumed": False,
                "summary": None,
                "step_index": step_index,
                "current_turn_actions": current_turn_actions,
                "total_actions": total_actions,
                "stale_action_attempts": stale_action_attempts,
                "consecutive_failures": consecutive_failures,
                "pending_end_turn_transition": None,
            }

        if self.config.auto_end_turn_when_only_end_turn and not self.config.dry_run:
            try:
                step_index += 1
                auto_end_turn = legal_actions[0]
                policy_output = PolicyDecision(
                    action_id=auto_end_turn.action_id,
                    reason="only end_turn remains; runner auto ends turn",
                    metadata={"auto_end_turn": True},
                )
                result = self.bridge.submit_action(
                    ActionSubmission(
                        session_id=snapshot.session_id,
                        decision_id=snapshot.decision_id,
                        state_version=snapshot.state_version,
                        action_id=auto_end_turn.action_id,
                        args=self._build_action_args(auto_end_turn),
                    )
                )
                total_actions += 1
                current_turn_actions += 1
                stale_action_attempts = 0
                consecutive_failures = 0
                stop_reason = "auto_end_turn" if self.config.stop_after_player_turn else ""
                self._record(
                    recorder=recorder,
                    snapshot=snapshot,
                    legal_actions=legal_actions,
                    policy_output=policy_output,
                    bridge_result=to_dict(result),
                    interrupted=False,
                    step_index=step_index,
                    current_turn_index=current_turn_index,
                    actions_this_turn=current_turn_actions,
                    total_actions=total_actions,
                    waiting_for_player_turn=False,
                    is_final_step=bool(stop_reason),
                    stop_reason=stop_reason,
                    battle_stop_reason=stop_reason,
                )
            except StaleActionError as exc:
                stale_action_attempts += 1
                consecutive_failures += 1
                retrying = stale_action_attempts <= self.config.stale_action_retries
                self._record(
                    recorder=recorder,
                    snapshot=snapshot,
                    legal_actions=legal_actions,
                    policy_output=PolicyDecision(
                        action_id=legal_actions[0].action_id,
                        reason="only end_turn remains; runner auto ends turn",
                        metadata={"auto_end_turn": True},
                    ),
                    bridge_result={
                        "status": "interrupted",
                        "error_code": getattr(exc, "error_code", "stale_action"),
                        "message": str(exc),
                        "retrying": retrying,
                        "stale_action_attempts": stale_action_attempts,
                        "consecutive_failures": consecutive_failures,
                    },
                    interrupted=not retrying,
                    step_index=step_index,
                    current_turn_index=current_turn_index,
                    actions_this_turn=current_turn_actions,
                    total_actions=total_actions,
                    waiting_for_player_turn=False,
                    is_final_step=not retrying,
                    stop_reason="stale_action" if not retrying else "stale_action_retry",
                    battle_stop_reason="stale_action" if not retrying else "",
                )
                return {
                    "consumed": retrying,
                    "step_index": step_index,
                    "current_turn_actions": current_turn_actions,
                    "total_actions": total_actions,
                    "stale_action_attempts": stale_action_attempts,
                    "consecutive_failures": consecutive_failures,
                    "pending_end_turn_transition": None,
                    "summary": None if retrying else self._finish(
                        session_id=session_id,
                        trace_path=trace_path,
                        reason=getattr(exc, "error_code", "stale_action"),
                        completed=False,
                        interrupted=True,
                        actions_this_turn=current_turn_actions,
                        turns_completed=turns_completed,
                        total_actions=total_actions,
                        current_turn_index=current_turn_index,
                    ),
                }
            except (InvalidPayloadError, InterruptedSessionError, BridgeError) as exc:
                failure = self._finalize_failure(
                    exc=exc,
                    recorder=recorder,
                    snapshot=snapshot,
                    legal_actions=legal_actions,
                    step_index=step_index,
                    current_turn_index=current_turn_index,
                    current_turn_actions=current_turn_actions,
                    turns_completed=turns_completed,
                    total_actions=total_actions,
                    stale_action_attempts=stale_action_attempts,
                    consecutive_failures=consecutive_failures + 1,
                    trace_path=trace_path,
                    session_id=session_id,
                )
                return {
                    "consumed": True,
                    "step_index": failure["step_index"],
                    "current_turn_actions": failure["current_turn_actions"],
                    "total_actions": failure["total_actions"],
                    "stale_action_attempts": failure["stale_action_attempts"],
                    "consecutive_failures": failure["consecutive_failures"],
                    "pending_end_turn_transition": failure["pending_end_turn_transition"],
                    "summary": failure["summary"],
                }
            if self.config.stop_after_player_turn:
                return {
                    "consumed": True,
                    "step_index": step_index,
                    "current_turn_actions": current_turn_actions,
                    "total_actions": total_actions,
                    "stale_action_attempts": stale_action_attempts,
                    "consecutive_failures": consecutive_failures,
                    "pending_end_turn_transition": None,
                    "summary": self._finish(
                        session_id=snapshot.session_id,
                        trace_path=trace_path,
                        reason="auto_end_turn",
                        completed=True,
                        interrupted=False,
                        turn_completed=True,
                        actions_this_turn=current_turn_actions,
                        turns_completed=max(turns_completed, current_turn_index),
                        total_actions=total_actions,
                        current_turn_index=current_turn_index,
                    ),
                }
            return {
                "consumed": True,
                "step_index": step_index,
                "current_turn_actions": current_turn_actions,
                "total_actions": total_actions,
                "stale_action_attempts": stale_action_attempts,
                "consecutive_failures": consecutive_failures,
                "pending_end_turn_transition": (snapshot.decision_id, snapshot.state_version),
                "summary": None,
            }

        return {
            "consumed": True,
            "step_index": step_index,
            "current_turn_actions": current_turn_actions,
            "total_actions": total_actions,
            "stale_action_attempts": stale_action_attempts,
            "consecutive_failures": consecutive_failures,
            "pending_end_turn_transition": None,
            "summary": self._finish(
                session_id=snapshot.session_id,
                trace_path=trace_path,
                reason="end_turn_only",
                completed=True,
                interrupted=False,
                turn_completed=True,
                actions_this_turn=current_turn_actions,
                turns_completed=max(turns_completed, current_turn_index),
                total_actions=total_actions,
                current_turn_index=current_turn_index,
            ),
        }

    def _run_player_step(
        self,
        *,
        recorder: JsonlTraceRecorder,
        snapshot,
        legal_actions,
        step_index: int,
        current_turn_index: int,
        current_turn_actions: int,
        turns_completed: int,
        total_actions: int,
        stale_action_attempts: int,
        consecutive_failures: int,
        trace_path: Path,
        session_id: str,
    ) -> dict[str, object]:
        try:
            step_index += 1
            policy_output = self._decide(snapshot, legal_actions)
            if policy_output.halt or not policy_output.action_id:
                self._record(
                    recorder=recorder,
                    snapshot=snapshot,
                    legal_actions=legal_actions,
                    policy_output=policy_output,
                    bridge_result={"status": "interrupted", "reason": "policy_halt"},
                    interrupted=True,
                    step_index=step_index,
                    current_turn_index=current_turn_index,
                    actions_this_turn=current_turn_actions,
                    total_actions=total_actions,
                    waiting_for_player_turn=False,
                    is_final_step=True,
                    stop_reason="policy_halt",
                    battle_stop_reason="policy_halt",
                )
                return self._step_result(
                    step_index=step_index,
                    current_turn_actions=current_turn_actions,
                    total_actions=total_actions,
                    stale_action_attempts=stale_action_attempts,
                    consecutive_failures=consecutive_failures,
                    pending_end_turn_transition=None,
                    summary=self._finish(
                        session_id=session_id,
                        trace_path=trace_path,
                        reason="policy_halt",
                        completed=False,
                        interrupted=True,
                        actions_this_turn=current_turn_actions,
                        turns_completed=turns_completed,
                        total_actions=total_actions,
                        current_turn_index=current_turn_index,
                    ),
                )

            legal_actions_by_id = {action.action_id: action for action in legal_actions}
            if policy_output.action_id not in legal_actions_by_id:
                raise InvalidPayloadError("policy returned an action outside the legal action set")
            selected_action = legal_actions_by_id[policy_output.action_id]

            if self.config.dry_run:
                self._record(
                    recorder=recorder,
                    snapshot=snapshot,
                    legal_actions=legal_actions,
                    policy_output=policy_output,
                    bridge_result={
                        "status": "dry_run",
                        "planned_action_id": policy_output.action_id,
                        "message": "dry run enabled; bridge submission skipped",
                    },
                    interrupted=False,
                    step_index=step_index,
                    current_turn_index=current_turn_index,
                    actions_this_turn=current_turn_actions,
                    total_actions=total_actions,
                    waiting_for_player_turn=False,
                    is_final_step=True,
                    stop_reason="dry_run",
                    battle_stop_reason="dry_run",
                )
                return self._step_result(
                    step_index=step_index,
                    current_turn_actions=current_turn_actions,
                    total_actions=total_actions,
                    stale_action_attempts=stale_action_attempts,
                    consecutive_failures=consecutive_failures,
                    pending_end_turn_transition=None,
                    summary=self._finish(
                        session_id=session_id,
                        trace_path=trace_path,
                        reason="dry_run",
                        completed=False,
                        interrupted=True,
                        actions_this_turn=current_turn_actions,
                        turns_completed=turns_completed,
                        total_actions=total_actions,
                        current_turn_index=current_turn_index,
                    ),
                )

            result = self.bridge.submit_action(
                ActionSubmission(
                    session_id=snapshot.session_id,
                    decision_id=snapshot.decision_id,
                    state_version=snapshot.state_version,
                    action_id=policy_output.action_id,
                    args=self._build_action_args(selected_action),
                )
            )
            total_actions += 1
            current_turn_actions += 1
            stale_action_attempts = 0
            consecutive_failures = 0
            stop_reason = self._post_action_stop_reason(selected_action.type, policy_output, result)
            self._record(
                recorder=recorder,
                snapshot=snapshot,
                legal_actions=legal_actions,
                policy_output=policy_output,
                bridge_result=to_dict(result),
                interrupted=False,
                step_index=step_index,
                current_turn_index=current_turn_index,
                actions_this_turn=current_turn_actions,
                total_actions=total_actions,
                waiting_for_player_turn=False,
                is_final_step=bool(stop_reason),
                stop_reason=stop_reason,
                battle_stop_reason=stop_reason,
            )
            if stop_reason:
                return self._step_result(
                    step_index=step_index,
                    current_turn_actions=current_turn_actions,
                    total_actions=total_actions,
                    stale_action_attempts=stale_action_attempts,
                    consecutive_failures=consecutive_failures,
                    pending_end_turn_transition=None if self.config.stop_after_player_turn or selected_action.type != "end_turn" else (snapshot.decision_id, snapshot.state_version),
                    summary=self._finish(
                        session_id=session_id,
                        trace_path=trace_path,
                        reason=stop_reason,
                        completed=True,
                        interrupted=False,
                        turn_completed=True,
                        battle_completed=stop_reason == "terminal_action_accepted",
                        actions_this_turn=current_turn_actions,
                        turns_completed=max(turns_completed, current_turn_index),
                        total_actions=total_actions,
                        current_turn_index=current_turn_index,
                    ),
                )
            return self._step_result(
                step_index=step_index,
                current_turn_actions=current_turn_actions,
                total_actions=total_actions,
                stale_action_attempts=stale_action_attempts,
                consecutive_failures=consecutive_failures,
                pending_end_turn_transition=None if selected_action.type != "end_turn" or self.config.stop_after_player_turn else (snapshot.decision_id, snapshot.state_version),
                summary=None,
            )
        except StaleActionError as exc:
            stale_action_attempts += 1
            consecutive_failures += 1
            retrying = stale_action_attempts <= self.config.stale_action_retries
            interrupted_payload = {
                "status": "interrupted",
                "error_code": getattr(exc, "error_code", "stale_action"),
                "message": str(exc),
                "retrying": retrying,
                "stale_action_attempts": stale_action_attempts,
                "consecutive_failures": consecutive_failures,
            }
            fallback_output = locals().get("policy_output", PolicyDecision(action_id=None, reason="policy unavailable", halt=True))
            self._record(
                recorder=recorder,
                snapshot=snapshot,
                legal_actions=legal_actions,
                policy_output=fallback_output,
                bridge_result=interrupted_payload,
                interrupted=not retrying,
                step_index=step_index,
                current_turn_index=current_turn_index,
                actions_this_turn=current_turn_actions,
                total_actions=total_actions,
                waiting_for_player_turn=False,
                is_final_step=not retrying,
                stop_reason="stale_action" if not retrying else "stale_action_retry",
                battle_stop_reason="stale_action" if not retrying else "",
            )
            if retrying:
                return self._step_result(
                    step_index=step_index,
                    current_turn_actions=current_turn_actions,
                    total_actions=total_actions,
                    stale_action_attempts=stale_action_attempts,
                    consecutive_failures=consecutive_failures,
                    pending_end_turn_transition=None,
                    summary=None,
                )
            return self._step_result(
                step_index=step_index,
                current_turn_actions=current_turn_actions,
                total_actions=total_actions,
                stale_action_attempts=stale_action_attempts,
                consecutive_failures=consecutive_failures,
                pending_end_turn_transition=None,
                summary=self._finish(
                    session_id=session_id,
                    trace_path=trace_path,
                    reason=interrupted_payload["error_code"],
                    completed=False,
                    interrupted=True,
                    actions_this_turn=current_turn_actions,
                    turns_completed=turns_completed,
                    total_actions=total_actions,
                    current_turn_index=current_turn_index,
                ),
            )
        except (InvalidPayloadError, InterruptedSessionError, BridgeError) as exc:
            return self._finalize_failure(
                exc=exc,
                recorder=recorder,
                snapshot=snapshot,
                legal_actions=legal_actions,
                step_index=step_index,
                current_turn_index=current_turn_index,
                current_turn_actions=current_turn_actions,
                turns_completed=turns_completed,
                total_actions=total_actions,
                stale_action_attempts=stale_action_attempts,
                consecutive_failures=consecutive_failures + 1,
                trace_path=trace_path,
                session_id=session_id,
            )
        except PolicyError as exc:
            return self._finalize_failure(
                exc=exc,
                recorder=recorder,
                snapshot=snapshot,
                legal_actions=legal_actions,
                step_index=step_index,
                current_turn_index=current_turn_index,
                current_turn_actions=current_turn_actions,
                turns_completed=turns_completed,
                total_actions=total_actions,
                stale_action_attempts=stale_action_attempts,
                consecutive_failures=consecutive_failures + 1,
                trace_path=trace_path,
                session_id=session_id,
                is_policy_error=True,
            )

    def _finalize_failure(
        self,
        *,
        exc: Exception,
        recorder: JsonlTraceRecorder,
        snapshot,
        legal_actions,
        step_index: int,
        current_turn_index: int,
        current_turn_actions: int,
        turns_completed: int,
        total_actions: int,
        stale_action_attempts: int,
        consecutive_failures: int,
        trace_path: Path,
        session_id: str,
        is_policy_error: bool = False,
    ) -> dict[str, object]:
        error_code = getattr(exc, "error_code", "policy_error" if is_policy_error else "bridge_error")
        interrupted_payload = {
            "status": "interrupted",
            "error_code": error_code,
            "message": str(exc),
            "consecutive_failures": consecutive_failures,
        }
        if is_policy_error:
            fallback_output = PolicyDecision(
                action_id=None,
                reason=str(exc),
                halt=True,
                metadata={"error_code": error_code},
            )
        else:
            fallback_output = PolicyDecision(action_id=None, reason="policy unavailable", halt=True)
        self._record(
            recorder=recorder,
            snapshot=snapshot,
            legal_actions=legal_actions,
            policy_output=fallback_output,
            bridge_result=interrupted_payload,
            interrupted=True,
            step_index=step_index,
            current_turn_index=current_turn_index,
            actions_this_turn=current_turn_actions,
            total_actions=total_actions,
            waiting_for_player_turn=False,
            is_final_step=True,
            stop_reason=error_code,
            battle_stop_reason=error_code,
        )
        return self._step_result(
            step_index=step_index,
            current_turn_actions=current_turn_actions,
            total_actions=total_actions,
            stale_action_attempts=stale_action_attempts,
            consecutive_failures=consecutive_failures,
            pending_end_turn_transition=None,
            summary=self._finish(
                session_id=session_id,
                trace_path=trace_path,
                reason=error_code,
                completed=False,
                interrupted=True,
                actions_this_turn=current_turn_actions,
                turns_completed=turns_completed,
                total_actions=total_actions,
                current_turn_index=current_turn_index,
            ),
        )

    @staticmethod
    def _step_result(
        *,
        step_index: int,
        current_turn_actions: int,
        total_actions: int,
            stale_action_attempts: int,
            consecutive_failures: int,
            pending_end_turn_transition: tuple[str, int] | None,
            summary: RunSummary | None,
    ) -> dict[str, object]:
        return {
            "step_index": step_index,
            "current_turn_actions": current_turn_actions,
            "total_actions": total_actions,
            "stale_action_attempts": stale_action_attempts,
            "consecutive_failures": consecutive_failures,
            "pending_end_turn_transition": pending_end_turn_transition,
            "summary": summary,
        }

    def _decide(self, snapshot, legal_actions):
        with ThreadPoolExecutor(max_workers=1) as executor:
            future = executor.submit(self.policy.decide, snapshot, legal_actions)
            try:
                return future.result(timeout=self.config.timeout_seconds)
            except FutureTimeoutError as exc:
                raise InterruptedSessionError("policy timed out") from exc

    def _record(
        self,
        *,
        recorder: JsonlTraceRecorder,
        snapshot,
        legal_actions,
        policy_output,
        bridge_result,
        interrupted: bool,
        step_index: int,
        current_turn_index: int,
        actions_this_turn: int,
        total_actions: int,
        waiting_for_player_turn: bool,
        is_final_step: bool,
        stop_reason: str,
        battle_stop_reason: str,
    ) -> None:
        recorder.append(
            TraceEntry(
                session_id=snapshot.session_id,
                decision_id=snapshot.decision_id,
                state_version=snapshot.state_version,
                phase=snapshot.phase,
                legal_actions=[to_dict(action) for action in legal_actions],
                observation=to_dict(snapshot),
                policy_output=to_dict(policy_output),
                bridge_result=to_dict(bridge_result),
                step_index=step_index,
                current_turn_index=current_turn_index,
                actions_this_turn=actions_this_turn,
                total_actions=total_actions,
                waiting_for_player_turn=waiting_for_player_turn,
                is_final_step=is_final_step,
                stop_reason=stop_reason,
                battle_stop_reason=battle_stop_reason,
                interrupted=interrupted,
                timestamp=datetime.now(UTC).isoformat(),
            )
        )

    def _finish(
        self,
        session_id: str,
        trace_path: Path,
        *,
        reason: str,
        completed: bool,
        interrupted: bool,
        turn_completed: bool = False,
        battle_completed: bool = False,
        actions_this_turn: int = 0,
        turns_completed: int = 0,
        total_actions: int = 0,
        current_turn_index: int = 0,
    ) -> RunSummary:
        return RunSummary(
            session_id=session_id,
            completed=completed,
            interrupted=interrupted,
            decisions=total_actions,
            trace_path=str(trace_path),
            reason=reason,
            turn_completed=turn_completed,
            actions_this_turn=actions_this_turn,
            battle_completed=battle_completed,
            turns_completed=turns_completed,
            total_actions=total_actions,
            current_turn_index=current_turn_index,
            ended_by=reason,
        )

    def _battle_completion_reason(self, snapshot) -> str:
        if self.config.stop_after_player_turn:
            return ""
        if snapshot.phase != "combat":
            return "battle_completed"
        enemies = getattr(snapshot, "enemies", []) or []
        if enemies and not any(getattr(enemy, "is_alive", True) for enemy in enemies):
            return "battle_completed"
        if not enemies:
            return "battle_completed"
        return ""

    def _battle_budget_stop_reason(
        self,
        *,
        total_actions: int,
        turns_completed: int,
        current_turn_index: int,
        consecutive_failures: int,
    ) -> str:
        if self.config.max_total_actions is not None and total_actions >= self.config.max_total_actions:
            return "max_total_actions"
        if self.config.max_turns_per_battle is not None:
            if turns_completed >= self.config.max_turns_per_battle or current_turn_index > self.config.max_turns_per_battle:
                return "max_turns_per_battle"
        if self.config.max_consecutive_failures >= 0 and consecutive_failures >= self.config.max_consecutive_failures:
            return "max_consecutive_failures"
        return ""

    def _update_turn_state(
        self,
        snapshot,
        player_turn: bool,
        current_turn_marker: object | None,
        current_turn_index: int,
        current_turn_actions: int,
    ) -> tuple[object | None, int, int]:
        marker = self._current_turn_marker(snapshot, player_turn)
        if player_turn:
            if marker != current_turn_marker:
                current_turn_index += 1
                current_turn_actions = 0
                current_turn_marker = marker
        else:
            current_turn_marker = marker
        return current_turn_marker, current_turn_index, current_turn_actions

    def _current_turn_marker(self, snapshot, player_turn: bool) -> object:
        metadata = getattr(snapshot, "metadata", {}) or {}
        for key in ("round_number", "round", "turn_index", "turn_number"):
            value = metadata.get(key)
            if value is not None and str(value) != "":
                return ("round", str(value), player_turn)
        side = metadata.get("current_side") or metadata.get("turn_owner")
        if isinstance(side, str) and side.strip():
            return ("side", side.strip().lower())
        return ("player_turn", player_turn)

    def _is_player_turn(self, snapshot) -> bool:
        if snapshot.phase != "combat":
            return False
        metadata = getattr(snapshot, "metadata", {}) or {}
        side = metadata.get("current_side") or metadata.get("turn_owner")
        if isinstance(side, str) and side.strip():
            return side.strip().lower() == "player"
        return True

    def _max_actions_per_turn(self) -> int:
        if self.config.max_actions_per_turn is not None:
            return self.config.max_actions_per_turn
        return self.config.max_steps

    @staticmethod
    def _is_only_end_turn(legal_actions) -> bool:
        return len(legal_actions) == 1 and legal_actions[0].type == "end_turn"

    def _post_action_stop_reason(self, action_type: str, policy_output: PolicyDecision, result) -> str:
        if result.terminal:
            return "terminal_action_accepted"
        metadata_phase = str(result.metadata.get("phase") or "")
        if self.config.stop_after_player_turn and action_type == "end_turn":
            if policy_output.metadata.get("auto_end_turn"):
                return "auto_end_turn"
            return "end_turn_submitted"
        if self.config.stop_after_player_turn and metadata_phase and metadata_phase != "combat":
            return "phase_changed"
        return ""

    def _read_consistent_state(self, session_id: str) -> tuple[Any, list[Any]]:
        snapshot = self.bridge.get_snapshot(session_id)
        legal_actions = self.bridge.get_legal_actions(session_id)
        retries = max(1, self.config.state_sync_retries)
        for _ in range(retries):
            confirm_snapshot = self.bridge.get_snapshot(session_id)
            if confirm_snapshot.decision_id == snapshot.decision_id and confirm_snapshot.state_version == snapshot.state_version:
                return snapshot, legal_actions
            snapshot = confirm_snapshot
            legal_actions = self.bridge.get_legal_actions(session_id)
        return snapshot, legal_actions

    @staticmethod
    def _effective_legal_actions(snapshot, legal_actions):
        player = getattr(snapshot, "player", None)
        if player is None:
            return legal_actions
        hand_by_id = {card.card_id: card for card in player.hand}
        effective_actions = []
        for action in legal_actions:
            if action.type != "play_card":
                effective_actions.append(action)
                continue
            card_id = action.params.get("card_id")
            if not isinstance(card_id, str):
                effective_actions.append(action)
                continue
            card = hand_by_id.get(card_id)
            if card is None:
                continue
            if card.playable and card.cost <= player.energy:
                effective_actions.append(action)
        return effective_actions

    @staticmethod
    def _build_action_args(action) -> dict[str, object]:
        args = dict(action.params)
        if len(action.target_constraints) == 1 and "target_id" not in args:
            args["target_id"] = action.target_constraints[0]
        return args
