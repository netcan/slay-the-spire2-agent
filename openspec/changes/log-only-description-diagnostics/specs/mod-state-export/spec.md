## ADDED Requirements

### Requirement: Mod 必须以精简协议导出说明类对象
系统 MUST 在统一状态快照与合法动作元数据中把说明类对象导出为精简公共协议。对于 cards、powers、card preview 与后续复用同类说明结构的对象，mod MUST 以 `description` 作为唯一必需的用户向说明文本字段；仅用于 description 解析排障的 `description_quality`、`description_source`、`description_vars` 等内部结构 MUST NOT 进入公共导出 schema。

#### Scenario: snapshot 中的卡牌说明只保留 canonical description
- **WHEN** 外部调用方读取 `snapshot.player.hand[]` 中的卡牌对象
- **THEN** 若该卡牌存在说明文本，快照 MUST 返回最终可读的 `description`
- **THEN** 快照 MUST NOT 要求调用方继续读取内部 diagnostics 才能理解该卡牌描述

#### Scenario: action metadata 与 snapshot 共享同一精简说明协议
- **WHEN** bridge 为动作 metadata 导出 `card_preview` 或其他说明对象
- **THEN** metadata 中的说明结构 MUST 与 snapshot 保持一致的精简字段语义
- **THEN** 不同导出位置 MUST NOT 出现一处只给 `description`、另一处继续暴露内部解析 diagnostics 的分叉协议
