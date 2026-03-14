## ADDED Requirements

### Requirement: Bridge 快照必须暴露卡牌描述的解析质量
系统 MUST 在桥接快照中稳定暴露卡牌描述的质量语义，使上层调用方能够区分“已经得到真实动态数值”的卡牌与“仍处于模板回退”的卡牌。若 bridge 暂时无法拿到真实值，快照 MUST 保持兼容可读，但 MUST 同时提供足够的质量或来源信息，避免上层误判。

#### Scenario: 快照中的卡牌描述已完成真实数值解析
- **WHEN** bridge 已从 live runtime 拿到当前卡牌实例的真实动态数值
- **THEN** 快照中的 `description_rendered` MUST 为不含模板占位符的最终文本
- **THEN** `description_vars` MUST 能反映对应动态字段的实际值

#### Scenario: 快照中的卡牌描述仍处于模板回退
- **WHEN** bridge 只能拿到模板文本，或 `description_rendered` 仍包含模板占位符
- **THEN** 快照 MUST 继续返回兼容字段，避免中断 autoplay
- **THEN** 快照 MUST 同时暴露足以识别回退状态的质量或来源信息

### Requirement: Bridge 不得把模板文本伪装成高质量策略输入
系统 MUST 确保上层通过 snapshot 或 action metadata 读取到的卡牌描述不会被错误标记为“已渲染完成”。如果某张卡牌的 `card_preview`、`snapshot.player.hand` 或等效结构仍停留在模板占位符层，bridge MUST 在对应输出上保持一致的回退语义。

#### Scenario: action metadata 中的 card_preview 与 snapshot 质量保持一致
- **WHEN** 同一张 live 手牌同时出现在 `snapshot.player.hand` 与 `actions[].metadata.card_preview`
- **THEN** 两处关于 `description_rendered`、`description_vars` 和回退状态的语义 MUST 保持一致
- **THEN** bridge MUST NOT 在一个位置标记为已解析、另一个位置却仍是模板回退且无解释
