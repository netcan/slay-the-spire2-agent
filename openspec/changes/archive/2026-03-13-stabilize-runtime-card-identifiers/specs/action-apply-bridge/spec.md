## ADDED Requirements

### Requirement: play_card 动作必须使用实例级 card_id 执行
系统 MUST 在 `apply play_card` 请求中使用 `card_id` 作为实例级定位键，而不是仅依赖卡牌名称、费用或其他可能重复的表面属性。当请求中的 `card_id` 无法匹配当前 live 手中的具体实例时，bridge MUST 拒绝执行，并返回明确的 stale action、invalid action 或等效错误原因。

#### Scenario: 同名手牌按 card_id 精确出牌
- **WHEN** 当前手牌中存在多张同名卡，且 agent 提交某个 `play_card` action 对应的 `card_id`
- **THEN** bridge MUST 按该 `card_id` 定位并执行对应的具体手牌实例
- **THEN** bridge MUST NOT 因同名冲突而误打出另一张牌

#### Scenario: card_id 已失效时拒绝执行
- **WHEN** 外部 agent 提交的 `play_card` 请求中的 `card_id` 已不再对应当前 live 手牌实例
- **THEN** bridge MUST 拒绝执行该动作
- **THEN** 返回结果 MUST 标记为 stale action、invalid action 或等效错误状态
