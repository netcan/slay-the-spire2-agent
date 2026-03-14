## Context

目前 live bridge 已能在 `reward` 界面导出 `snapshot.phase = reward`、`snapshot.rewards`，并提供 `choose_reward` / `skip_reward` 等 legal actions。但 Python 侧 `AutoplayOrchestrator` 的“可执行窗口”逻辑仅覆盖 `combat` 的玩家回合：`snapshot.phase != "combat"` 会被视为非玩家回合而进入等待；在 battle mode 下又会把 `phase != combat` 视为 “battle_completed” 从而提前结束。因此 reward 阶段无法被 agent 处理，阻断了“战斗结束 -> 奖励 -> 地图”的自动闭环。

同时，reward 属于高影响写操作，默认自动领取会污染跑局结果。设计上需要明确的安全开关与可复盘 trace，确保不会在未配置的情况下误拿奖励。

## Goals / Non-Goals

**Goals:**

- runner/orchestrator 能在 `snapshot.phase = reward` 时做出一次或多次 reward 决策，并通过 bridge 提交 `choose_reward` / `skip_reward`。
- 提供 reward 决策模式开关，默认安全（不自动领取），可配置为仅自动跳过或交给 LLM 决策。
- reward 决策必须使用当前 legal actions 进行选择与校验，并在 trace 中完整记录 observation、legal actions、policy output 与 bridge result。
- CLI/环境变量可控制 reward 模式，便于联调与回归。

**Non-Goals:**

- 不在本 change 内支持 “choose_reward 之后的二级选择界面”（如 card reward 的卡牌网格选择、遗物挑选等）。这类界面需要 bridge 侧新增 phase/window 支持，另开 change 处理。
- 不引入新的外部依赖（如 OCR/图像识别）或新的桥接协议字段。

## Decisions

1. 用显式 `reward_mode` 控制 reward 行为，默认 `halt`
原因：reward 写入对跑局影响大，默认必须 fail-safe。提供三档模式即可覆盖联调与产品化需要：

- `halt`（默认）：一旦进入 `phase=reward`，runner 停止并返回明确 reason（例如 `reward_phase_reached`），不做任何写操作。
- `skip`：仅在 legal actions 中存在 `skip_reward` 时自动提交 `skip_reward`，否则停止。
- `llm`：把 reward legal actions 交给 LLM policy 决策；若模型返回 `halt=true` 或非法动作，runner 按现有失败回退/重试语义处理。

备选：默认自动 `skip_reward`。实现更简单但会改变默认行为并隐藏风险，不采纳。

2. 将 reward 视为“可执行决策窗口”，而不是“非玩家回合等待”
实现方式：在 orchestrator 主循环中引入 `is_actionable_window(snapshot)`：

- `combat` 且玩家回合：保持现有逻辑
- `reward` 且 `reward_mode != halt`：进入新的 reward handler

这样保持主循环结构与 trace 形态一致，并复用动作合法性校验与 stale action 重试能力。

3. 对 battle completion 判定做最小改动，避免 reward 被当作战斗完成
在 battle mode 下，现有逻辑会把 `phase != combat` 直接判为 `battle_completed`。调整为：

- `phase = reward` 且 `reward_mode != halt` 时，不视为 battle completed，而是继续执行 reward handler
- `phase = map` / `terminal` 仍可作为 battle 结束信号（与现有逻辑一致）

备选：引入更通用的“battle lifecycle 状态机”，覆盖 event/treasure/merchant 等更多窗口。范围过大，本 change 不做。

4. policy 提示词补充 reward 语义，但仍以 legal actions 为唯一约束
`ChatCompletionsPolicy` 现有 system prompt 已要求“只能从 legal actions 选择 action_id”。本 change 额外补充：

- 当 `snapshot.phase = reward` 时，模型需要在 `choose_reward` / `skip_reward` 中做选择
- 不确定时可返回 `halt=true`

提示词仅改善模型输出质量，不改变“只允许从 legal actions 选择”的硬约束。

## Risks / Trade-offs

- [reward 决策会影响跑局结果] → 默认 `reward_mode=halt`，并在 CLI 上要求显式启用；`skip` 模式作为低风险联调默认。
- [模型在 reward 阶段输出不稳定] → 复用现有 action_id 校验、失败计数与重试机制；并在 trace 中保留原始响应文本。
- [choose_reward 后进入未支持窗口导致 runner 卡住] → 明确 Non-Goals；进入未知 phase 时按既有逻辑停止并输出 reason，后续另开 change 扩展 bridge/window。

