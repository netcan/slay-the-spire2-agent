## ADDED Requirements

### Requirement: 描述 schema 必须区分模板文本、渲染文本与变量槽位
系统 MUST 为卡牌、powers 或等效文本对象提供分层描述字段，至少覆盖原始模板文本、当前战斗下的渲染文本与结构化变量槽位。调用方 MUST 能区分“这是模板文本”还是“这是当前已渲染的事实文本”，而不是继续依赖单个模糊 `description` 字段猜测。

#### Scenario: 当前卡牌带有动态格挡数值
- **WHEN** agent 读取一张当前效果会随战斗状态变化的防御牌
- **THEN** snapshot MUST 能同时表示其原始模板文本与当前回合实际渲染后的描述
- **THEN** snapshot MUST 允许通过结构化变量字段读取对应的动态数值

### Requirement: 描述 schema 必须为关键术语预留稳定 glossary 锚点
系统 MUST 为“格挡”“力量”“易伤”“虚弱”等关键术语提供稳定 glossary 锚点，例如 `keyword_ids`、`glossary_keys` 或等效 canonical 标识。上层调用方 MUST 能基于这些锚点补充解释，而不是只依赖显示字符串做模糊匹配。

#### Scenario: 渲染文本中包含格挡术语
- **WHEN** snapshot 中某张卡牌或 power 的渲染文本包含“格挡”
- **THEN** 对应对象 MUST 同时携带可稳定识别“格挡”语义的 glossary 锚点
- **THEN** 上层策略 MUST 不需要只靠中文字符串猜测它的含义
