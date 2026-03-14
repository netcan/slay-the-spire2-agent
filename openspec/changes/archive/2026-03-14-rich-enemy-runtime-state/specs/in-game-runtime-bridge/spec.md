## ADDED Requirements

### Requirement: live runtime bridge 必须稳定提取 enemy enrich fields 并按字段降级
当 `snapshot.phase="combat"` 时，bridge MUST 尽可能从 live runtime 读取敌人的当前招式文本、trait/tag、关键词或等效 richer enemy fields。若某个敌人的某个扩展字段暂时不可读，bridge MUST 对该 enemy 独立降级，而不是让整个 enemy 列表或 combat snapshot 失败。

#### Scenario: live runtime 成功读取敌人的当前招式说明
- **WHEN** 某个敌人的当前行动对象、显示文本或等效节点在 runtime 中可访问
- **THEN** `snapshot.enemies[]` MUST 导出该敌人的 `move_name`、`move_description` 或等效 richer fields
- **THEN** 若能解析 glossary，bridge MUST 一并导出对应的结构化 glossary anchors

#### Scenario: 单个敌人的 enrich 字段读取失败时保持 fail-safe
- **WHEN** 某个敌人的 move 文本、trait 容器或关键词来源暂时不可读
- **THEN** bridge MUST 仍然返回可序列化的 `snapshot.enemies[]`
- **THEN** 该敌人的基础字段（如 `name`、`hp`、`intent`、`powers`）MUST 继续可用
- **THEN** metadata MUST 提供该 enemy 或字段的 source、fallback 或等效 diagnostics
