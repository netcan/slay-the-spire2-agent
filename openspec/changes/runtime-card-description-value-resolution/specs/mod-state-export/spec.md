## ADDED Requirements

### Requirement: Mod 必须为 live 手牌描述优先导出真实动态数值
系统 MUST 在真实 STS2 runtime 的 `combat` 手牌导出中，优先返回与当前卡牌实例一致的动态数值描述。对于 `damage`、`block`、`draw`、`strength` 或等效高价值字段，mod MUST 尽量从 live card instance 或等效运行时状态中提取真实值，而不是长期停留在模板占位符文本。

#### Scenario: 基础攻击牌在 live combat 中导出真实伤害
- **WHEN** 玩家处于真实战斗回合，手牌中存在一张可打出的基础攻击牌，且运行时能计算当前伤害
- **THEN** 该卡牌的导出结果 MUST 反映当前实例级的真实伤害值，或在 `description_vars` 中给出对应数值
- **THEN** mod MUST NOT 只返回没有任何数值信息的 `{Damage:diff()}` 模板作为唯一有效信息

#### Scenario: 基础防御牌在 live combat 中导出真实格挡
- **WHEN** 玩家处于真实战斗回合，手牌中存在一张可打出的基础防御牌，且运行时能计算当前格挡
- **THEN** 该卡牌的导出结果 MUST 反映当前实例级的真实格挡值，或在 `description_vars` 中给出对应数值
- **THEN** 若真实值暂不可得，响应 MUST 明确处于模板回退，而不是伪装为高质量 rendered

### Requirement: Mod 必须区分高质量 rendered 描述与模板回退
系统 MUST 区分“已经解析出真实数值的 `description_rendered`”与“仅做了样式去除但仍包含模板占位符的兼容文本”。当最终文本仍包含模板占位符、且也无法提供对应变量值时，mod MUST 将该对象标记为回退状态，并为排障提供来源或质量信息。

#### Scenario: 文本仍含模板占位符时不得视为高质量 rendered
- **WHEN** 导出的卡牌描述文本中仍包含 `{Damage:diff()}`、`{Block:diff()}` 或等效模板占位符
- **THEN** mod MUST 将该结果视为模板回退或未完全渲染状态
- **THEN** 对应 diagnostics MUST 能指出这是回退路径，而不是成功渲染

#### Scenario: 已完成变量替换时可视为高质量 rendered
- **WHEN** mod 已成功解析真实动态数值，并能生成不含模板占位符的用户向描述
- **THEN** `description_rendered` MUST 返回该最终文本
- **THEN** diagnostics 或等效字段 MUST 能表明该描述来自 live value resolution，而不是模板回退
