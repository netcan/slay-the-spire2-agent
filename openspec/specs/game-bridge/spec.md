# game-bridge Specification

## Purpose
TBD - created by archiving change sts2-agent. Update Purpose after archive.
## Requirements
### Requirement: Bridge 暴露当前决策快照
系统 MUST 暴露当前 Slay the Spire 2 决策窗口的结构化快照，至少包含会话元数据、阶段元数据、玩家可见状态、敌人可见状态、牌区摘要、遗物、药水以及终局标记。

#### Scenario: 在玩家回合中请求战斗快照
- **WHEN** agent 在一场进行中的战斗里、玩家可行动阶段请求当前决策快照
- **THEN** bridge 返回该最新决策窗口的单个结构化快照，并包含足以选择合法动作的可见状态

### Requirement: Bridge 枚举当前决策窗口的合法动作
系统 MUST 返回当前决策窗口的完整合法动作集合，且每个动作 MUST 包含稳定的 `action_id`、动作 `type`、所需参数以及执行该动作所需的目标约束。

#### Scenario: 请求当前战斗中的合法动作
- **WHEN** agent 在战斗中、玩家仍可行动时请求合法动作列表
- **THEN** bridge 返回该决策窗口下全部当前可用的出牌、选目标、使用药水和结束回合动作

### Requirement: Bridge 在不改变状态的前提下拒绝过期或非法动作
系统 MUST 基于最新决策窗口校验提交动作，并 MUST 在动作格式错误、动作非法或动作已过期时拒绝该提交，同时不修改游戏状态。

#### Scenario: 状态变化后提交了旧决策窗口的动作
- **WHEN** agent 提交的动作绑定于旧的决策窗口，而 bridge 已经推进到新的 `state_version`
- **THEN** bridge 以确定性的错误响应拒绝该动作，并保持当前游戏状态不变

### Requirement: Bridge 支持本地对局生命周期控制
系统 MUST 提供本地生命周期操作，用于启动或附着到会话、在支持时重置或重开一局，以及在不让游戏进入不确定状态的前提下干净地停止 agent 控制。

#### Scenario: 操作者为新的 agent 尝试重开一局
- **WHEN** 操作者对当前会话发起受支持的 reset 或 restart 命令
- **THEN** bridge 初始化一个新的可控会话，并返回新的会话标识供后续 agent 调用

### Requirement: Bridge 必须在 combat 快照中导出主要牌堆内容
系统 MUST 在 `snapshot.phase="combat"` 时，除当前手牌外，还导出当前玩家抽牌堆、弃牌堆、消耗堆的结构化卡牌内容。对外字段名可以是 `draw_pile_cards`、`discard_pile_cards`、`exhaust_pile_cards` 或等效稳定名称，但三类 pile MUST 可被调用方稳定区分。

#### Scenario: 战斗快照同时包含 hand 与 pile contents
- **WHEN** agent 在玩家可行动的战斗回合请求当前快照
- **THEN** `snapshot.player` MUST 同时包含 `hand` 与 draw/discard/exhaust 的 pile contents
- **THEN** 调用方 MUST 不必只依赖 `draw_pile`、`discard_pile`、`exhaust_pile` 计数字段猜测具体牌组成

#### Scenario: pile cards 与 hand cards 保持一致的基础语义
- **WHEN** bridge 导出 `draw_pile_cards`、`discard_pile_cards` 或 `exhaust_pile_cards`
- **THEN** 每个 pile card MUST 复用与 `hand[]` 一致的基础卡牌字段语义，例如 `name`、`canonical_card_id`、`description` 或等效字段
- **THEN** 这些 pile cards MUST 作为观察态信息导出，而不是自动被视为当前可执行动作

### Requirement: Bridge 必须在 combat 快照中导出 richer enemy runtime state
系统 MUST 在 `snapshot.phase="combat"` 时，为 `snapshot.enemies[]` 导出比基础血量与 intent 更丰富的敌人观测信息。对外字段可以是 `move_name`、`move_description`、`move_glossary`、`traits`、`keywords` 或等效稳定名称，但调用方 MUST 能稳定区分“当前招式信息”和“敌人自身 trait / keyword 信息”。

#### Scenario: 战斗快照包含敌人的招式文本与机制标签
- **WHEN** agent 在玩家可行动的战斗回合请求当前快照
- **THEN** `snapshot.enemies[]` MUST 在基础 `intent`、`intent_damage`、`powers` 之外，额外导出当前敌人的行动文本或等效 richer fields
- **THEN** 调用方 MUST 不必只依赖 `intent_damage` 与敌人名称猜测当前怪物机制

#### Scenario: richer enemy fields 与现有基础字段保持兼容
- **WHEN** bridge 导出 richer enemy state
- **THEN** 现有 `enemy_id`、`name`、`hp`、`block`、`intent`、`powers` 等基础字段 MUST 继续保留
- **THEN** 新增字段 MUST 作为向后兼容增强，而不是要求旧调用方迁移到全新 enemy 嵌套对象

