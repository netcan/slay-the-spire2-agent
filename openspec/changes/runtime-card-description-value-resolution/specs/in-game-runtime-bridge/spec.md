## ADDED Requirements

### Requirement: live runtime bridge 必须将手牌描述绑定到当前卡牌实例的动态值
系统 MUST 在真实 STS2 进程内，优先基于当前手牌实例的 live runtime 状态解析卡牌描述所需的动态值，而不是长期依赖模板文本或静态定义。对于同名重复手牌，bridge MUST 以实例级语义读取数值，确保导出的描述与该 `card_id` 所代表的当前卡牌一致。

#### Scenario: 同名重复手牌也按实例级动态值读取
- **WHEN** 玩家手牌中同时存在多张同名卡牌，且其中部分实例因战斗效果产生不同的当前数值
- **THEN** bridge MUST 按实例读取每张卡牌的动态值
- **THEN** 每张卡牌导出的描述与变量值 MUST 与其自身 `card_id` 对应，而不是混用模板或共享静态值

#### Scenario: live runtime 找不到动态值时保持 fail-safe
- **WHEN** runtime bridge 无法从当前卡牌实例上稳定读取某个动态值
- **THEN** bridge MUST 回退到模板导出路径
- **THEN** bridge MUST 在 diagnostics 或等效辅助字段中指出该值未成功解析
- **THEN** bridge MUST NOT 因单张卡牌的动态值读取失败而使整个 live snapshot 构建失败
