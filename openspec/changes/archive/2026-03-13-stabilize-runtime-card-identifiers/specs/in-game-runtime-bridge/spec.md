## ADDED Requirements

### Requirement: 手牌卡牌必须导出稳定且可区分的运行时 card_id
系统 MUST 为 `snapshot.player.hand` 中的每一张手牌导出稳定的 `card_id`，并确保同名、同费用、同升级状态的重复手牌在同一决策窗口内仍然可以被区分。该 `card_id` MUST 与当前 live 牌实例保持一致，并 SHALL 被对应的 `play_card` legal action 通过 `params.card_id` 直接引用。

#### Scenario: 重复手牌仍然拥有不同 card_id
- **WHEN** 玩家当前手牌中同时存在两张或更多张表面属性相同的卡牌
- **THEN** `/snapshot.player.hand` 中每张手牌的 `card_id` MUST 彼此不同
- **THEN** 卡牌的 `name` MAY 相同，但 bridge MUST 仍能稳定区分这些实例

#### Scenario: legal actions 与 hand 中的 card_id 一一对应
- **WHEN** bridge 生成当前窗口的 `play_card` legal actions
- **THEN** 每个 `play_card` action 的 `params.card_id` MUST 对应到 `snapshot.player.hand` 中的一张具体手牌
- **THEN** `action_id` MUST 反映该 `card_id` 所代表的实例级差异
