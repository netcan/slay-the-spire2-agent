## Context

当前代码已经具备以下基础：一是 mod 侧可以稳定导出 `reward`、`map`、`combat` 等 phase，并支持 `choose_reward`、`skip_reward`、`choose_map_node`；二是 Python 侧 runner 已能完成 menu 开局、战斗内多步决策、部分 reward 决策与 trace 落盘。真实联调中，当前断点主要出现在“奖励链路结束后如何继续”“地图出现后何时选路”“选路后如何等待房间加载并重新接入下一场战斗”三个阶段。

这次变更跨越 `src/sts2_agent/` 的 orchestrator/policy/CLI，以及 `mod/Sts2Mod.StateBridge` 的 live diagnostics，属于典型的跨模块执行语义提升，而不是单点 bug 修复。设计需要明确 phase 状态机、安全边界，以及何时由 LLM 决策、何时由本地策略接管。

## Goals / Non-Goals

**Goals:**
- 让 autoplay 能从 `reward` 连续推进到 `map`，再推进到下一房间，并在进入下一场 `combat` 后继续运行。
- 为 `reward_choice`、`card_reward_selection`、`map`、房间过渡/等待建立统一的 runner 状态机与停止条件。
- 保持动作提交仍然严格受当前 legal actions 约束，不引入脱离 bridge 的“猜测式点击”。
- 提供可复盘 trace，明确每一步属于奖励决策、选图决策、等待过渡还是重新接战。
- 补充 live diagnostics，让 runner 能区分“当前无动作但仍在正常过渡”和“状态卡死/识别失败”。

**Non-Goals:**
- 不在本次变更中解决完整 run 的所有节点类型（如商店、事件、休息点、Boss 奖励）的自动策略。
- 不要求一次性做出最优 reward/map 策略；允许先使用可配置的保守策略或简单启发式。
- 不新增新的 HTTP 协议端点；优先复用现有 `snapshot`、`actions`、`apply` 与 metadata。

## Decisions

### 1. 以“跨 phase 自动化状态机”扩展 runner，而不是把所有 phase 混成同一循环
runner 将显式区分 `combat`、`reward_choice`、`card_reward_selection`、`map`、`transition_wait`、`unknown_window` 等子状态。这样可以为每类窗口定义不同的动作来源、超时、重试与停止条件。

- 选择原因：现有 battle autoplay 默认假设“phase 变化 = 停止”，继续堆补丁会让控制流越来越脆弱。
- 备选方案：继续沿用单循环 + 若干 `if phase == ...` 分支。问题是 transition/wait 与真正可决策窗口难以区分，trace 也不清晰。

### 2. reward 与 map 的动作来源允许“LLM + 本地保守策略”双轨
对于 `reward` 与 `map`，runner 支持两类模式：
- `llm`：把当前 `snapshot` + legal actions 交给模型选择。
- `safe-default`：使用本地保守策略，例如金币优先、卡牌奖励默认跳过/按启发式选第一、地图默认选可达普通战斗节点或最左节点。

默认先偏保守，避免 live 调试阶段因为 reward/map 策略不成熟导致跑局不稳定。

- 选择原因：这段链路的目标首先是“连续跑通”，而不是立刻追求高策略质量。
- 备选方案：所有非战斗决策都强制交给 LLM。问题是调试成本高，且难与桥接问题区分。

### 3. 将“等待过渡”建模为显式状态，并依赖 metadata + 时间预算判断是否正常
在 reward 领完、地图选路后，短时间内可能既没有新的 legal actions，也还没进入下一场战斗。runner 将进入 `transition_wait`，持续轮询：
- 若看到 `window_kind`/`reward_subphase`/`phase_detected` 等 metadata 正常推进，则继续等待；
- 若超过超时预算仍停在不变状态，判定为 `transition_timeout`；
- 若进入新的 `combat` 且 legal actions 可用，则恢复战斗 autoplay。

- 选择原因：真实游戏中房间加载和界面切换存在自然空窗，不能简单把“无动作”视为失败。
- 备选方案：一旦 `/actions` 为空就停止。这样无法打通下一房间。

### 4. bridge 侧优先补 diagnostics 和稳定导出，不扩大写协议
mod 侧本次优先补三个方向：
- reward 完成后到 map 出现前的窗口/metadata 诊断；
- map 可达节点导出的稳定性；
- 进入下一房间后 phase/window 的推进信号。

除非 live 证明现有 `choose_reward` / `choose_map_node` 不足，否则不新增写接口。

- 选择原因：当前主要缺口是“识别与编排”，不是动作协议本身。
- 备选方案：新增专用 `continue_after_reward` 或 `continue_run_flow` 动作。问题是会把编排逻辑塞进 bridge，降低透明度。

### 5. trace 与 summary 需要新增跨窗口运行语义
现有 trace 更偏战斗内动作。此次将补充：
- `step_kind` / `phase_kind`：标记 combat、reward、map、wait；
- `transition_attempt` / `transition_elapsed_ms`：标记等待房间切换的过程；
- `next_combat_entered`、`map_choices_taken`、`reward_choices_taken`：写入最终 summary。

这样 live 失败时可以快速判断是 reward 卡住、map 卡住，还是成功进入下一战但模型后续失败。

## Risks / Trade-offs

- [Risk] reward/map 的保守策略过于简单，可能导致路径质量差或错过收益。 -> Mitigation：策略做成显式配置，先以“稳定跑通”为目标，后续再替换为 LLM 或更强启发式。
- [Risk] 游戏过渡窗口没有稳定 legal actions，runner 可能误判卡死。 -> Mitigation：增加基于 metadata/state_version 的等待判定，并设置分层超时（短轮询 + 总预算）。
- [Risk] bridge 某些 map/reward 文本仍可能 fallback，影响模型选择质量。 -> Mitigation：继续保留 `params.reward_index`、`params.node` 等结构化键，trace 中记录 diagnostics。
- [Risk] 跨 phase 自动化会拉长单次运行时长，live 调试更难复现。 -> Mitigation：CLI 提供 `max_non_combat_steps`、`transition_timeout`、`stop_after_next_combat` 等开关，缩短定位路径。
- [Risk] 当前变更与已存在的 `full-battle-llm-autoplay`、`agent-reward-decision` 在实现层面有交叠。 -> Mitigation：本次 OpenSpec 明确聚焦“reward -> map -> next combat”闭环，把 battle 内与单个 reward 决策视为前置能力并在实现时复用。 
