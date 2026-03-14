## Why

当前 `llm-autoplay-runner` 的 battle autoplay 在进入 `reward` 阶段后通常会停止（或把 `phase != combat` 视为“战斗已结束”而提前退出），导致无法完成“战斗结束 -> 领取/跳过奖励 -> 进入地图”的闭环。这会直接卡住“打一整场战斗/跑一整局”的目标，也让 reward 相关状态与动作无法被 agent 利用。

## What Changes

- 扩展 Python 侧 orchestrator/runner，使其在 `snapshot.phase = reward` 时可以执行 reward 决策并调用 bridge 的 `choose_reward` / `skip_reward`。
- 引入 reward 决策的安全开关与模式（默认不自动领取奖励；可配置为仅自动跳过或交给 LLM 决策），避免在未确认策略前误操作导致跑局结果不可控。
- 为 reward 决策补齐 trace 记录与联调脚本参数，确保 reward 行为可复盘、可回归。

## Capabilities

### New Capabilities

- 无

### Modified Capabilities

- `llm-autoplay-runner`: runner 在 battle autoplay 中不再把 `reward` 简化为“战斗结束即停止”，而是支持 reward 决策与可配置的安全策略。

## Impact

- 影响代码主要在 `src/sts2_agent/orchestrator.py`、`src/sts2_agent/live_autoplay.py`、`src/sts2_agent/policy/llm.py` 与 `tools/run_llm_autoplay.py`。
- 对外接口层面会新增/扩展 runner 的配置项（CLI/env），并扩展 `llm-autoplay-runner` spec 的行为约束。
- 不修改 mod 侧 HTTP 协议；reward 动作仍使用现有 bridge 的 `choose_reward` / `skip_reward`。

