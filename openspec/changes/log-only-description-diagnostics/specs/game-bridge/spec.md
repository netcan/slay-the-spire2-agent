## ADDED Requirements

### Requirement: Bridge 面向 Agent 的说明对象必须保持精简
系统 MUST 将 cards、powers、card preview 与其他说明类对象的公共响应收敛为面向决策的精简 schema。对外协议中的说明文本 MUST 以 canonical `description` 为主；`description_quality`、`description_source`、`description_vars` 等仅用于解析排障的内部字段 MUST NOT 继续暴露给客户端或策略层。

#### Scenario: 战斗快照中的手牌与 powers 只导出精简说明字段
- **WHEN** agent 读取 `snapshot.player.hand[]`、`snapshot.player.powers[]` 或 `snapshot.enemies[].powers[]`
- **THEN** 若对象存在说明文本，bridge MUST 返回可直接消费的 `description`
- **THEN** 公共响应 MUST NOT 再包含 `description_quality`、`description_source` 或 `description_vars`

#### Scenario: legal action preview 不再泄漏内部说明诊断
- **WHEN** bridge 为 `play_card`、`choose_reward` 或等效动作导出 `card_preview`、`reward_preview` 等说明对象
- **THEN** preview 中的说明字段 MUST 与 snapshot 一样保持精简
- **THEN** 客户端 MUST 不需要理解说明解析来源、变量表或回退等级才能正常决策
