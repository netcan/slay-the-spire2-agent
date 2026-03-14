## Context

当前 `llm-autoplay-runner` 已经能在真实战斗中完成一次模型驱动的单步动作执行，证明了 `HttpGameBridge`、`ChatCompletionsPolicy` 和 `POST /apply` 的最小闭环是可用的。但 runner 仍然主要围绕“打一手就退出”设计：`max_steps=1` 时能做 smoke，`max_steps>1` 时虽然会继续循环，却缺少清晰的“玩家回合边界”“何时认为本回合已正常结束”“何时应该主动停手”语义，因此还不适合作为完整回合 autoplay 体验。

本次设计目标不是无限制连续 autoplay，而是把现有执行模型提升为“以当前玩家回合为边界的多步自动打牌”：在同一 `combat` 玩家回合中持续决策和执行，直到能量耗尽、只剩 `end_turn`、模型显式 halt、phase 变化、进入终局，或命中安全停止条件。

## Goals / Non-Goals

**Goals:**
- 明确定义并实现“完整玩家回合 autoplay”的停止边界，而不是只依赖粗糙的 `max_steps`。
- 在连续多步执行过程中持续读取最新 `snapshot` / `actions`，保证每一步都基于最新 live state 决策。
- 为多步运行补充回合级 trace / summary，至少能回答“本回合出了几手牌”“为何停下”“是否完成自动结束回合”。
- 为真实 battle 联调提供可控的安全参数，如单回合最大动作数与可选自动 `end_turn` 策略。

**Non-Goals:**
- 不在本次 change 中解决策略质量问题，例如更聪明的出牌顺序或长程规划。
- 不扩展模型接口或 bridge 协议本身；本次重点是 runner 执行语义。
- 不尝试跨多个玩家回合、整场战斗或整局 run 的全自动托管。

## Decisions

### 1. 以“玩家回合结束”为主停止条件，而不是单纯放大 `max_steps`

继续保留 `max_steps` / `max_actions_per_turn` 作为硬上限，但正常停止条件改为回合边界优先：只要检测到 phase 不再适合继续玩家侧决策，就应认为本回合结束。具体判断以最新 `snapshot` / `actions` 为基础，包括：

- `snapshot.terminal = true`
- `snapshot.phase != "combat"`
- legal actions 只剩 `end_turn`
- 玩家已经没有可打牌/可用动作，且允许自动结束回合
- bridge 回执表明已进入非玩家操作窗口

备选方案：
- 仍完全依赖 `max_steps`：实现简单，但无法区分“安全结束本回合”和“被硬截断”。

### 2. 引入回合级配置，而不是重写新的 runner

沿用 `AutoplayOrchestrator` 与 `LiveAutoplayConfig`，增加回合级参数，例如：

- `max_actions_per_turn`
- `stop_after_player_turn`
- `auto_end_turn_when_only_end_turn`

这样可以最小化对现有 `HttpGameBridge`、`ChatCompletionsPolicy` 和 CLI 的破坏，并保持单步 smoke 用法仍然可用。

### 3. `end_turn` 采用显式策略开关

当 legal actions 只剩 `end_turn` 时，runner 默认可以自动结束当前回合；但是否在“仍有其他 legal action 但模型反复选择保守动作”场景下主动补 `end_turn`，需要与模型控制权分开。因此本次只对“只剩 `end_turn`”启用自动结束回合逻辑，避免 runner 擅自替模型做过多策略判断。

备选方案：
- 每次都让模型自己决定是否 `end_turn`：更纯粹，但可能在能量耗尽后反复输出非法动作或 halt。

### 4. 扩展 `RunSummary` 与 trace，记录回合级执行结果

当前 `RunSummary` 只有 `completed`、`interrupted`、`decisions`、`reason`，不足以表达“本回合正常跑完但整个战斗未结束”。本次计划补充：

- `turn_completed`：是否正常走到本回合结束边界
- `actions_this_turn`
- `ended_by`：如 `auto_end_turn`、`phase_changed`、`policy_halt`、`max_actions_per_turn`

trace 仍保持逐步记录，但每条记录会增加回合内序号、是否为本回合最后一步等信息。

### 5. 真实联调先限定“一个完整玩家回合”

CLI 先支持“完整打一回合”而不是“自动托管整场战斗”。这样风险更可控，也更方便人工观察模型在一整个回合里的连续决策表现。若后续质量足够，再继续扩展到跨回合 / 跨窗口 autoplay。

## Risks / Trade-offs

- [回合结束判断不稳定，可能在敌方动画或中间窗口误判] → 优先依赖最新 `snapshot.phase`、legal actions 集合与 bridge 回执，而不是猜测 UI 状态。
- [多步执行更容易积累模型错误] → 增加单回合动作上限，并保留人工中止与 dry-run。
- [自动 `end_turn` 可能掩盖模型策略问题] → 仅在 legal set 明确只剩 `end_turn` 时触发，并在 trace 中明确标记为 runner 自动结束。
- [summary 语义变化可能影响现有脚本] → 保持旧字段兼容，新字段只增不删。

## Migration Plan

1. 先扩展 orchestrator / models，加入回合级配置与 summary 字段。
2. 再调整 live runner 和 CLI，暴露多步回合级参数。
3. 补齐单元测试，覆盖只剩 `end_turn`、phase 切换、动作上限等路径。
4. 在真实战斗中完成一次“从玩家回合开始，到该回合结束”为止的多步 autoplay 冒烟。

回滚方式：
- 若多步逻辑不稳定，可把 CLI 默认重新收回到 `max_steps=1` 的单步模式，不影响现有单步 autoplay 能力。

## Open Questions

- `turn_completed=true` 是否应在自动 `end_turn` 成功后立即返回，还是继续观察到敌方回合/下一玩家回合切换。
- 是否要在首版就支持“只自动打牌，不自动 `end_turn`”与“自动打完整回合”两个模式并存。
