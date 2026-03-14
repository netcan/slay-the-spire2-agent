## ADDED Requirements

### Requirement: Bridge 必须在 combat 快照中导出 richer enemy runtime state
系统 MUST 在 `snapshot.phase="combat"` 时，为 `snapshot.enemies[]` 导出比基础血量与 intent 更丰富的敌人观测信息。对外字段可以是 `move_name`、`move_description`、`move_glossary`、`traits`、`keywords` 或等效稳定名称，但调用方 MUST 能稳定区分“当前招式信息”和“敌人自身 trait / keyword 信息”。

#### Scenario: 战斗快照包含敌人的招式文本与机制标签
- **WHEN** agent 在玩家可行动的战斗回合请求当前快照
- **THEN** `snapshot.enemies[]` MUST 在基础 `intent`、`intent_damage`、`powers` 之外，额外导出当前敌人的行动文本或等效 richer fields
- **THEN** 调用方 MUST 不必只依赖 `intent_damage` 与敌人名称猜测当前怪物机制

#### Scenario: richer enemy fields 与现有基础字段保持兼容
- **WHEN** bridge 导出 richer enemy state
- **THEN** 现有 `enemy_id`、`name`、`hp`、`block`、`intent`、`powers` 等基础字段 MUST 继续保留
- **THEN** 新增字段 MUST 作为向后兼容增强，而不是要求旧调用方迁移到全新 enemy 嵌套对象
