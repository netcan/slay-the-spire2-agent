## MODIFIED Requirements

### Requirement: Mod 必须导出统一的决策窗口状态快照
系统 MUST 在 Slay the Spire 2 运行过程中识别当前决策窗口，并导出统一结构的状态快照，至少覆盖 `combat`、`reward`、`map`、`terminal` 四类窗口，并包含 `session_id`、`decision_id`、`state_version`、`phase` 等元数据。对于卡牌、powers 等带文本说明的对象，mod MUST 进一步支持导出分层描述字段，至少包括原始模板文本、当前渲染文本与结构化变量槽位；对于关键术语，mod MUST 允许导出稳定 glossary 锚点，以便上层补充解释。

#### Scenario: 玩家处于战斗回合时请求状态
- **WHEN** 外部调用方在玩家可行动的战斗回合请求当前快照
- **THEN** mod 返回一份 `combat` 类型的结构化状态快照，包含玩家、敌人、牌区和窗口元数据
- **THEN** 若当前手牌或 powers 含有动态描述文本，响应 MUST 尽量返回已渲染文本，并在可用时附带变量槽位与 glossary 锚点
