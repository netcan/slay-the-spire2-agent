## ADDED Requirements

### Requirement: runner 必须基于卡牌描述质量做策略输入降级
当 snapshot 中的卡牌 `description_rendered`、`description_vars` 或等效质量字段显示该描述仍处于模板回退时，runner MUST 采用安全降级策略组织模型输入，而不是把模板占位符文本直接当作高置信事实。若描述已经完成真实值解析，runner MUST 优先向策略层提供这些高质量字段。

#### Scenario: 已解析真实值的卡牌优先进入策略输入
- **WHEN** 当前 snapshot 中某张卡牌已提供不含模板占位符的 `description_rendered` 与可用的 `description_vars`
- **THEN** runner MUST 优先将这些字段纳入策略输入摘要
- **THEN** 策略层 MUST 不再只依赖卡名和 glossary 猜测效果

#### Scenario: 模板回退卡牌触发安全降级
- **WHEN** 当前 snapshot 中某张卡牌的 `description_rendered` 仍含模板占位符，或 `description_vars` 缺少真实值
- **THEN** runner MUST 将其视为低质量描述输入
- **THEN** runner MUST 优先回退到卡名、traits、glossary 与其他稳定事实，而不是把未解析模板原样提升为高置信描述
