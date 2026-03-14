## ADDED Requirements

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
