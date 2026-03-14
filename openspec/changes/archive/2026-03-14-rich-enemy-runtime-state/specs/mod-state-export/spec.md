## ADDED Requirements

### Requirement: Mod 必须同时导出基础 enemy state 与 richer enemy fields
系统 MUST 在 combat `snapshot.enemies[]` 中继续保留现有基础敌人字段，同时新增可供 agent 直接消费的 richer enemy fields，用于表达当前招式说明、trait/tag 与机制关键词。基础字段与 enrich 字段 MUST 指向同一时刻的窗口状态，不得彼此明显矛盾。

#### Scenario: 敌人存在当前招式文本时导出 enrich fields
- **WHEN** 某个敌人的当前招式在 runtime 中可读取到显示文本或说明文本
- **THEN** 对应 `snapshot.enemies[]` 条目 MUST 返回基础敌人状态
- **THEN** 对应条目 MUST 额外返回 `move_name`、`move_description` 或等效 enrich fields

#### Scenario: enrich 字段暂时为空时仍返回稳定结构
- **WHEN** 某个敌人的 trait、keyword 或 move description 当前不可读
- **THEN** 该敌人的基础字段 MUST 仍然导出
- **THEN** enrich 字段 MUST 返回空数组、空值或缺失 optional 字段中的稳定形式，而不是返回不可序列化值
