# in-game-runtime-bridge Specification

## Purpose
定义 STS2 bridge 作为真实游戏内 mod 运行时的状态导出约束，确保 live `health`、`snapshot`、`actions` 在受控线程上下文中稳定可用。

## Requirements
### Requirement: 游戏内 mod 必须暴露 live runtime bridge
系统 MUST 能以真实 STS2 mod 的形式运行在游戏进程内，并通过 loopback bridge 对外暴露 live `health`、`snapshot`、`actions` 能力。该 bridge SHALL 复用统一的决策窗口模型，并在游戏内无活动 run、运行时未就绪或版本不兼容时返回可诊断状态，而不是崩溃或阻塞游戏。

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

#### Scenario: runtime 读取失败时保持 fail-safe
- **WHEN** 反射读取、游戏节点发现或窗口识别过程中发生异常
- **THEN** bridge MUST 返回结构化错误或降级状态
- **THEN** mod MUST NOT 使游戏进程崩溃
- **THEN** 后续请求 MUST 仍可继续探测健康状态

### Requirement: bridge 必须在受控线程上下文导出 live state
系统 MUST 在游戏主线程或等效的受控调度点读取、刷新和导出游戏状态，不得在任意 HTTP 请求线程直接对 STS2 或 Godot 对象做不安全访问。若当前请求无法立即读取 live state，bridge MUST 返回明确失败语义或最近一次可接受的快照策略说明。

#### Scenario: HTTP 请求与游戏主线程并发发生
- **WHEN** 外部进程在游戏运行过程中并发请求 `snapshot` 或 `actions`
- **THEN** bridge MUST 通过受控调度或快照缓存来读取状态
- **THEN** bridge MUST NOT 直接在 HTTP 线程上执行不安全的游戏对象访问

#### Scenario: 状态版本在 live 更新后推进
- **WHEN** 当前决策窗口内容发生变化，例如手牌、敌人、奖励或可选地图点变化
- **THEN** 导出的 `state_version` MUST 对应推进
- **THEN** 新的 `decision_id` MUST 反映最新的 live 决策上下文

### Requirement: 手牌卡牌必须导出稳定且可区分的运行时 card_id
系统 MUST 为 `snapshot.player.hand` 中的每一张手牌导出稳定的 `card_id`，并确保同名、同费用、同升级状态的重复手牌在同一决策窗口内仍然可以被区分。该 `card_id` MUST 与当前 live 牌实例保持一致，并 SHALL 被对应的 `play_card` legal action 通过 `params.card_id` 直接引用。

#### Scenario: 重复手牌仍然拥有不同 card_id
- **WHEN** 玩家当前手牌中同时存在两张或更多张表面属性相同的卡牌
- **THEN** `/snapshot.player.hand` 中每张手牌的 `card_id` MUST 彼此不同
- **THEN** 卡牌的 `name` MAY 相同，但 bridge MUST 仍能稳定区分这些实例

#### Scenario: legal actions 与 hand 中的 card_id 一一对应
- **WHEN** bridge 生成当前窗口的 `play_card` legal actions
- **THEN** 每个 `play_card` action 的 `params.card_id` MUST 对应到 `snapshot.player.hand` 中的一张具体手牌
- **THEN** `action_id` MUST 反映该 `card_id` 所代表的实例级差异

### Requirement: live snapshot 与 actions 必须导出可读的用户向文本
系统 MUST 对 in-game runtime bridge 中的主要文本字段执行统一解析，至少覆盖 relics、potions、rewards、map nodes、cards、enemies 与 action labels。当字段背后存在 `LocString` 或类似本地化容器时，bridge MUST 优先输出面向玩家的本地化文本，而不得将类名、原始对象名或无意义 `ToString()` 结果直接作为对外值。

#### Scenario: relics 与 potions 使用本地化文本导出
- **WHEN** 玩家状态中包含 relics 或 potions，且对应对象可以解析到本地化显示文本
- **THEN** `/snapshot.player.relics` 与 `/snapshot.player.potions` MUST 返回可读的本地化名称
- **THEN** 返回值 MUST NOT 是 `MegaCrit.Sts2.Core.Localization.LocString` 或类似类名字符串

#### Scenario: reward 与 action label 优先使用用户向文本
- **WHEN** reward 按钮或 legal action 背后同时存在本地化文本与开发者内部标识
- **THEN** `/snapshot.rewards` 与 `/actions[].label` MUST 优先输出用户实际看到的可读文本
- **THEN** 若需要稳定行为参数，bridge MUST 通过 `params` 或 metadata 保留结构化标识，而不是仅依赖 label

### Requirement: 文本解析失败时必须提供可诊断的降级信息
系统 MUST 在文本解析失败或只能使用 fallback 时保持 fail-safe，并在 metadata 或等效 diagnostics 结构中暴露足够的调试信息。bridge MUST NOT 因为个别文本字段解析失败而使整个 `snapshot` 或 `actions` 构建失败。

#### Scenario: 无法解析本地化文本时仍返回稳定快照
- **WHEN** 某个 runtime 对象的文本字段无法通过本地化或预定字段解析
- **THEN** bridge MUST 仍然返回可序列化的 `snapshot` 或 `actions` 响应
- **THEN** bridge MUST 在 metadata 中指出对应字段使用了 fallback 或存在 unresolved 情况

#### Scenario: diagnostics 不得污染面向 Agent 的主字段
- **WHEN** bridge 需要暴露文本解析来源或失败原因
- **THEN** diagnostics MUST 优先放在 metadata 或等效辅助字段中
- **THEN** `name`、`label`、`relics`、`rewards` 等面向 Agent 的主字段 MUST 保持简洁、稳定和可读
