## Context

当前实测中，当 agent 在奖励列表（`NRewardsScreen`）选择了“将一张牌添加到你的牌组。”之后，游戏会进入选牌二级界面（卡牌网格选择）。此时 bridge 无法识别该界面，`DetectPhase(...)` 会回落到 `combat`，并在无存活敌人时导出 `metadata.window_kind="combat_transition"` 且 `actions=[]`。结果是 reward 链路无法闭环：agent 已经“选了奖励类型”，但无法继续在二级界面里选具体卡牌或跳过，从而卡在不再可决策的窗口。

现有实现对 reward 的判定主要依赖 `NRewardsScreen`（屏幕追踪器或 overlay stack 顶部对象），并在 `AnalyzeRewardPhase(...)` 的规则下决定是否 treat as reward。选牌二级界面不是 `NRewardsScreen`，因此会被误判为 combat transition。

本 change 的目标是在不改动 Python 侧协议/策略逻辑的前提下，让 mod 将该二级界面也导出为 `phase=reward`，并复用现有 `choose_reward` / `skip_reward` action 语义完成卡牌选择。

## Goals / Non-Goals

**Goals:**

- 在奖励链路进入“选牌二级界面”时，bridge MUST 导出为 `snapshot.phase="reward"` 的可执行窗口，而不是 `combat_transition`。
- 在该窗口导出可选卡牌列表（以用户可读文本为主），并生成 `choose_reward` legal actions，使 agent 能继续做选择。
- 在可跳过时生成 `skip_reward` legal action，并保证执行后进入后续窗口或回到奖励列表/地图。
- 在 `metadata` 中补充 diagnostics：标记 reward 子类型（例如 `reward_choice` vs `card_reward_selection`）、检测来源、运行时类型名等，方便联调与回归。
- 保持协议兼容：Python 侧继续只依赖 `phase=reward` + `choose_reward|skip_reward` 即可工作，不引入新 action type。

**Non-Goals:**

- 不在本 change 内覆盖所有“卡牌网格选择”场景（例如商店买牌、事件选牌、移除牌等）。本次聚焦于 reward 链路中的卡牌奖励选择。
- 不在本 change 内引入新的 reward 数据结构或协议字段（例如单独的 `reward_cards[]`）；优先复用已有 `snapshot.rewards` 与 `choose_reward` 参数。
- 不在本 change 内实现完整的“奖励链路全流程”策略（例如自动拿金币/遗物/药水等的策略优化），只保证可识别、可决策、可执行。

## Decisions

1. **用“reward 子窗口”概念扩展 phase 检测，而不是把二级界面当作 combat**
   - 方案：在 `AnalyzeRewardPhase(...)` 中引入对“卡牌奖励选择界面”的识别逻辑。一旦识别到该界面，直接 `TreatAsReward=true`，并在 metadata 中暴露 `reward_subphase="card_reward_selection"`。
   - 理由：reward 语义上是一个链路，二级界面仍然属于 reward 决策窗口；把它当 combat transition 会导致 actions 为空，破坏 agent 自动化闭环。
   - 备选：在 combat transition 时注入 reward actions。该方案会混淆 phase/window_kind 语义，并让 Python 侧需要额外处理，不采用。

2. **识别策略采用“强类型名 + 宽松特征”组合，避免仅依赖单一 FullName**
   - 方案：优先通过 overlay stack 顶部对象的 `GetType().FullName` 与已知/候选类型名匹配（新增常量），同时加入特征探测作为 fallback（例如是否存在可枚举的卡牌集合字段、是否存在选择/跳过相关方法或按钮集合）。
   - 理由：STS2 仍处于迭代期，内部类型名或字段名可能变动；纯 type-name hardcode 脆弱。组合策略允许在变更后通过 diagnostics 快速定位并补齐匹配。
   - 输出：`metadata.reward_phase_detection` 里记录 `overlay_top_type`、`card_reward_screen_detected`、`detection_source` 等字段。

3. **导出 choices 仍使用 `snapshot.rewards: string[]`，并以“卡牌可读名称”为主**
   - 方案：在 card reward selection 窗口，将可选卡牌的显示名作为 rewards labels（必要时附加少量信息如费用），生成 `choose_reward` actions 对应 `reward_index`。
   - 理由：Python 侧已经具备 reward 决策与动作提交闭环，复用能最小化改动与回归面；更结构化的 card schema 可在后续单独扩展。
   - 约束：labels 必须稳定且可读；若解析失败，允许使用 `card_<index>` 并在 diagnostics 标注 fallback。

4. **动作执行映射复用 `choose_reward` / `skip_reward`，并根据当前 reward 子窗口选择不同 handler**
   - 方案：在 `ExecuteChooseReward(...)` / `ExecuteSkipReward(...)` 内部增加分派逻辑：
     - 若当前是 `NRewardsScreen`，沿用现有 reward button 流程；
     - 若当前是 card reward selection screen，则按 `reward_index` 定位卡牌条目并调用该界面的选择/跳过钩子。
   - 理由：对外 action type 不变；内部根据窗口做不同反射调用，保持接口稳定。
   - 备选：新增 `choose_reward_card` action type。会引入协议改动与 Python 侧适配成本，不采用。

## Risks / Trade-offs

- [运行时类型名/字段名变化导致无法识别] → Mitigation: 组合识别策略 + metadata diagnostics，保留 `overlay_top_type` 与探测结果，便于现场快速加规则。
- [二级界面在动画/过渡期短暂出现，导致动作 stale] → Mitigation: actions 仅在界面可交互时生成；`apply` 时再次校验当前屏幕对象与可选项数量，不满足则返回 `stale_action`。
- [导出的 rewards labels 不够稳定或不可读] → Mitigation: 优先走 `RuntimeTextResolver` 的本地化解析；失败时使用稳定 placeholder，并在 `text_diagnostics` 中给出来源与 fallback。
- [skip 语义不一致：某些卡牌奖励可能没有跳过] → Mitigation: 仅当探测到明确的 skip/close 控件时才导出 `skip_reward`，否则不生成该 action 并在 metadata 标注原因。

