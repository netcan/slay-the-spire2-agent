## ADDED Requirements

### Requirement: 卡牌奖励选择界面必须作为 reward phase 导出并可连续决策
当奖励链路进入“选牌二级界面”（卡牌奖励选择）时，bridge MUST 将当前窗口导出为 `snapshot.phase="reward"`，并导出可选卡牌的用户向文本到 `snapshot.rewards`，同时生成对应的 `choose_reward` legal actions，使外部 agent 可以在同一 reward 链路中继续选择具体卡牌或完成该奖励步骤。

#### Scenario: 进入卡牌奖励选择界面时导出 reward phase
- **WHEN** 玩家在 `NRewardsScreen` 选择了“将一张牌添加到你的牌组。”并进入卡牌奖励选择界面
- **THEN** `snapshot.phase` MUST 等于 `reward`
- **THEN** `metadata.window_kind` MUST 标记为可区分的 reward 子窗口（例如 `reward_card_selection`）
- **THEN** `metadata.reward_subphase` MUST 标记为 `card_reward_selection`
- **THEN** `snapshot.rewards` MUST 为非空数组，按展示顺序包含每张可选卡牌的可读名称
- **THEN** `actions` MUST 为每个可选卡牌生成一个 `type="choose_reward"` 的 legal action，且其 `params.reward_index` MUST 与 `snapshot.rewards` 的索引一致

#### Scenario: reward buttons 不可用时不得回落到 combat_transition
- **WHEN** 当前没有存活敌人且 `NRewardsScreen` 不可见或 reward buttons 为 0，但卡牌奖励选择界面处于可交互状态
- **THEN** bridge MUST NOT 导出 `metadata.window_kind="combat_transition"` 作为当前决策窗口
- **THEN** bridge MUST 按卡牌奖励选择界面导出 reward 决策窗口与 legal actions

#### Scenario: 文本解析失败时仍提供可诊断且可执行的 choices
- **WHEN** 某些可选卡牌的本地化文本无法解析，只能使用 fallback 文本
- **THEN** `snapshot.rewards` MUST 仍包含与可选项数量一致的条目（例如 `card_<index>`）
- **THEN** bridge MUST 在 `metadata.text_diagnostics` 或等效 diagnostics 中指出对应条目使用了 fallback 与解析来源
- **THEN** `choose_reward` legal actions MUST 仍可按 `reward_index` 执行，不得因单个文本失败而缺失动作

#### Scenario: 不可跳过时不生成 skip_reward
- **WHEN** 当前卡牌奖励选择界面不存在跳过/关闭控件或该奖励规则不允许跳过
- **THEN** bridge MUST NOT 生成 `type="skip_reward"` 的 legal action
- **THEN** bridge MUST 在 `metadata` 中标记跳过不可用的原因（例如 `reward_skip_available=false` 与 `reward_skip_reason`）

