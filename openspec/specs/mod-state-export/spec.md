# mod-state-export Specification

## Purpose
TBD - created by archiving change sts2-mod-state-bridge. Update Purpose after archive.
## Requirements
### Requirement: Mod 必须导出统一的决策窗口状态快照
系统 MUST 在 Slay the Spire 2 运行过程中识别当前决策窗口，并导出统一结构的状态快照，至少覆盖 `combat`、`reward`、`map`、`terminal` 四类窗口，并包含 `session_id`、`decision_id`、`state_version`、`phase` 等元数据。

#### Scenario: 玩家处于战斗回合时请求状态
- **WHEN** 外部调用方在玩家可行动的战斗回合请求当前快照
- **THEN** mod 返回一份 `combat` 类型的结构化状态快照，包含玩家、敌人、牌区和窗口元数据

### Requirement: Mod 必须导出与窗口对应的合法动作集合
系统 MUST 针对当前决策窗口导出完整合法动作集合，并为每个动作提供稳定的 `action_id`、动作 `type`、参数信息与目标约束。

#### Scenario: 奖励选牌窗口导出合法动作
- **WHEN** 外部调用方在奖励选牌界面请求合法动作
- **THEN** mod 返回该窗口下所有可选卡牌动作以及 `skip` 等合法选择动作

### Requirement: Mod 必须在状态变化时推进快照版本
系统 MUST 在决策窗口变化、回合推进或界面切换后推进 `state_version`，并生成新的 `decision_id`，以便外部系统识别状态是否已经失效。

#### Scenario: 战斗结束进入奖励界面
- **WHEN** 游戏从战斗窗口切换到奖励窗口
- **THEN** mod 返回新的 `state_version` 和新的 `decision_id`，并将 `phase` 更新为 `reward`

### Requirement: Mod 必须暴露兼容性元数据
系统 MUST 在状态快照中包含协议版本、mod 版本以及可用于诊断的兼容性元数据，以帮助外部 agent 检查当前 bridge 是否可用。

#### Scenario: 外部系统检查 bridge 兼容性
- **WHEN** 外部调用方读取当前状态快照
- **THEN** 响应中包含当前协议版本、mod 版本以及必要的环境兼容信息

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

