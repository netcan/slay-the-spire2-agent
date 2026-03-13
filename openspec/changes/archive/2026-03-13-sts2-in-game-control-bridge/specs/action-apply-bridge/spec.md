## ADDED Requirements

### Requirement: bridge 必须支持外部动作提交与决策校验
系统 MUST 提供 `apply action` 能力，允许外部 agent 基于当前 `decision_id` 提交动作请求。bridge MUST 在执行前校验 `read_only` 开关、`decision_id`、当前 phase 与 legal action 集合，并对拒绝原因返回明确的结构化结果。

#### Scenario: 合法动作被成功接受
- **WHEN** 外部 agent 提交的动作请求命中当前 `decision_id` 且对应合法动作
- **THEN** bridge MUST 返回该请求已被接受执行的结果
- **THEN** 结果 MUST 包含用于诊断的动作标识或请求标识

#### Scenario: 过期 decision 的动作被拒绝
- **WHEN** 外部 agent 提交的 `decision_id` 已不是当前 live 决策上下文
- **THEN** bridge MUST 拒绝执行该动作
- **THEN** 返回结果 MUST 明确标记为 stale decision 或等效错误原因

#### Scenario: 只读模式下拒绝写动作
- **WHEN** bridge 处于 `read_only=true` 模式且收到动作提交请求
- **THEN** bridge MUST 拒绝执行该动作
- **THEN** 返回结果 MUST 明确说明写操作被禁用

### Requirement: 核心窗口必须具备首批真实动作执行映射
系统 MUST 为 `combat`、`reward`、`map` 三类核心窗口提供首批真实执行映射，至少覆盖 `play_card`、`end_turn`、`choose_reward`、`skip_reward`、`choose_map_node`。bridge MUST 只执行当前 legal actions 中存在的动作，不得猜测未枚举动作。

#### Scenario: 战斗回合执行打牌动作
- **WHEN** 当前 phase 为 `combat` 且 legal actions 中存在某个 `play_card`
- **THEN** bridge MUST 能把该动作映射到游戏内真实出牌流程
- **THEN** 执行后新的 `snapshot` MUST 反映更新后的 live 状态或新的决策上下文

#### Scenario: 奖励窗口执行选牌或跳过
- **WHEN** 当前 phase 为 `reward` 且 legal actions 中存在 `choose_reward` 或 `skip_reward`
- **THEN** bridge MUST 能触发对应奖励选择或跳过逻辑
- **THEN** 执行结果 MUST 导向新的窗口状态、地图状态或下一决策

#### Scenario: 地图窗口执行路线选择
- **WHEN** 当前 phase 为 `map` 且 legal actions 中存在某个 `choose_map_node`
- **THEN** bridge MUST 只允许选择当前可达节点
- **THEN** 执行后 MUST 进入与该节点对应的后续 run 状态

### Requirement: 动作执行必须通过受控队列与结果回执闭环
系统 MUST 将外部动作请求写入受控执行队列，并由游戏主线程或受控调度点消费。bridge MUST 为每个动作请求维护结果状态，至少区分 `accepted`、`rejected`、`failed` 三类结论，便于 agent 做恢复与重试。

#### Scenario: 请求先入队再由游戏线程消费
- **WHEN** bridge 收到一个格式正确且通过校验的动作请求
- **THEN** bridge MUST 先将请求加入受控执行队列
- **THEN** 实际的游戏状态变更 MUST 在受控执行阶段发生

#### Scenario: 执行阶段发生运行时异常
- **WHEN** 动作请求已通过校验但在实际执行中抛出异常
- **THEN** bridge MUST 将该请求标记为 `failed` 或等效失败状态
- **THEN** 返回结果 MUST 包含可诊断的错误信息
- **THEN** mod MUST NOT 因该异常导致整个 bridge 不可用
