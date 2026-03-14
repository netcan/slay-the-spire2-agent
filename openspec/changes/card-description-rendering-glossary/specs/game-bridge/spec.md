## MODIFIED Requirements

### Requirement: Bridge 暴露当前决策快照
系统 MUST 暴露当前 Slay the Spire 2 决策窗口的结构化快照，至少包含会话元数据、阶段元数据、玩家可见状态、敌人可见状态、牌区摘要、遗物、药水以及终局标记。对于卡牌、powers 或等效文本对象，bridge MUST 在保持现有基础字段兼容的前提下，稳定暴露分层描述信息，例如 `description_raw`、`description_rendered`、`description_vars` 与 glossary 锚点；当这些 richer 字段暂时缺失时，bridge MUST 退化到现有基础文本字段，而不是让快照失效。

#### Scenario: 在玩家回合中请求战斗快照
- **WHEN** agent 在一场进行中的战斗里、玩家可行动阶段请求当前决策快照
- **THEN** bridge 返回该最新决策窗口的单个结构化快照，并包含足以选择合法动作的可见状态
- **THEN** 若当前卡牌或 powers 已有渲染描述与 glossary 锚点，bridge MUST 一并暴露给上层策略
