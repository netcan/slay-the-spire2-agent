## ADDED Requirements

### Requirement: Bridge 必须在 combat 快照中导出主要牌堆内容
系统 MUST 在 `snapshot.phase="combat"` 时，除当前手牌外，还导出当前玩家抽牌堆、弃牌堆、消耗堆的结构化卡牌内容。对外字段名可以是 `draw_pile_cards`、`discard_pile_cards`、`exhaust_pile_cards` 或等效稳定名称，但三类 pile MUST 可被调用方稳定区分。

#### Scenario: 战斗快照同时包含 hand 与 pile contents
- **WHEN** agent 在玩家可行动的战斗回合请求当前快照
- **THEN** `snapshot.player` MUST 同时包含 `hand` 与 draw/discard/exhaust 的 pile contents
- **THEN** 调用方 MUST 不必只依赖 `draw_pile`、`discard_pile`、`exhaust_pile` 计数字段猜测具体牌组成

#### Scenario: pile cards 与 hand cards 保持一致的基础语义
- **WHEN** bridge 导出 `draw_pile_cards`、`discard_pile_cards` 或 `exhaust_pile_cards`
- **THEN** 每个 pile card MUST 复用与 `hand[]` 一致的基础卡牌字段语义，例如 `name`、`canonical_card_id`、`description` 或等效字段
- **THEN** 这些 pile cards MUST 作为观察态信息导出，而不是自动被视为当前可执行动作
