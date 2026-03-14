## ADDED Requirements

### Requirement: Mod 必须同时导出牌堆计数与 pile contents
系统 MUST 在 combat `snapshot.player` 中继续保留现有 `draw_pile`、`discard_pile`、`exhaust_pile` 等计数字段，同时新增对应的 pile contents 列表，用于描述这些牌堆中当前有哪些牌。计数与列表 MUST 指向同一时刻的窗口状态，不得彼此明显矛盾。

#### Scenario: 抽牌堆存在卡牌时导出列表与计数
- **WHEN** 玩家当前战斗中的抽牌堆非空
- **THEN** `snapshot.player.draw_pile` MUST 返回数量摘要
- **THEN** `snapshot.player.draw_pile_cards` MUST 返回与该 pile 对应的结构化卡牌列表

#### Scenario: pile 为空时仍返回稳定结构
- **WHEN** 弃牌堆或消耗堆当前为空
- **THEN** 对应计数字段 MUST 为 `0`
- **THEN** 对应 pile contents 字段 MUST 返回空数组，而不是缺失或返回非列表值
