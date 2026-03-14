## ADDED Requirements

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
