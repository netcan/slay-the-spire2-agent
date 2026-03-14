## MODIFIED Requirements

### Requirement: 游戏内 mod 必须暴露 live runtime bridge
系统 MUST 能以真实 STS2 mod 的形式运行在游戏进程内，并通过 loopback bridge 对外暴露 live `health`、`snapshot`、`actions` 能力。该 bridge SHALL 复用统一的决策窗口模型，并在游戏内无活动 run、运行时未就绪或版本不兼容时返回可诊断状态，而不是崩溃或阻塞游戏。对于战斗结束后的奖励窗口，bridge MUST 稳定识别并导出 `phase = reward`，不得在 reward 已显示时错误回落到 `combat` 或继续伪造玩家战斗动作。

#### Scenario: 游戏内 bridge 成功附着到活动 run
- **WHEN** STS2 已启动、mod 已加载且玩家进入一局活动 run
- **THEN** `health` MUST 返回可识别的 in-game runtime 模式
- **THEN** `snapshot` MUST 返回当前决策窗口的 live 状态
- **THEN** `actions` MUST 返回与该窗口对应的 legal actions

#### Scenario: 游戏已启动但当前没有活动 run
- **WHEN** STS2 已启动且 mod 已加载，但玩家仍在主菜单或尚未进入 run
- **THEN** `health` MUST 返回 bridge 已加载但 run 未就绪的状态说明
- **THEN** `snapshot` MUST NOT 伪造战斗或地图数据
- **THEN** bridge MUST 保持可继续服务，直到 run 就绪

#### Scenario: reward 界面显示时导出 reward phase
- **WHEN** 玩家已经结束战斗并进入奖励界面，且 runtime 可观察到 reward screen、reward buttons 或等效 reward 信号
- **THEN** `snapshot.phase` MUST 返回 `reward`
- **THEN** `snapshot.rewards` MUST 返回当前可见奖励文本
- **THEN** `actions` MUST 返回 `choose_reward`、`skip_reward` 或等效 reward 合法动作，而不是 `end_turn`

#### Scenario: 战斗结束过渡态不得伪装为玩家战斗回合
- **WHEN** 当前战斗敌人已经全部清空，但 reward UI 仍在挂载或切换中
- **THEN** bridge MUST 优先进入 reward 识别或保守降级路径
- **THEN** bridge MUST NOT 持续导出 `window_kind = player_turn` 与可重复提交的 `end_turn` 作为主要对外语义

#### Scenario: runtime 读取失败时保持 fail-safe
- **WHEN** 反射读取、游戏节点发现或窗口识别过程中发生异常
- **THEN** bridge MUST 返回结构化错误或降级状态
- **THEN** mod MUST NOT 使游戏进程崩溃
- **THEN** 后续请求 MUST 仍可继续探测健康状态
