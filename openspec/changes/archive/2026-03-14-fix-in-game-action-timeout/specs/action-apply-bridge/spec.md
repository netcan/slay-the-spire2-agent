## MODIFIED Requirements

### Requirement: 动作执行必须通过受控队列与结果回执闭环
系统 MUST 将外部动作请求写入受控执行队列，并由游戏主线程或受控调度点消费。bridge MUST 为每个动作请求维护结果状态，至少区分 `accepted`、`rejected`、`failed` 三类结论，便于 agent 做恢复与重试。在 `in-game-runtime` 模式下，bridge MUST 记录动作请求的关键执行阶段，并在队列未被消费、执行阶段抛错或状态长时间未推进时返回可诊断的失败信息，而不是仅暴露无上下文的超时。

#### Scenario: 请求先入队再由游戏线程消费
- **WHEN** bridge 收到一个格式正确且通过校验的动作请求
- **THEN** bridge MUST 先将请求加入受控执行队列
- **THEN** 实际的游戏状态变更 MUST 在受控执行阶段发生

#### Scenario: 游戏线程已消费动作请求
- **WHEN** 某个 in-game 动作请求已被 `Tick()` 或等效受控调度点取出并开始处理
- **THEN** bridge MUST 记录该请求已经进入 `dequeued`、`executing` 或等效中间阶段
- **THEN** 若后续仍失败，返回结果 MUST 能区分“已消费但执行失败”和“从未被消费”

#### Scenario: 队列未被及时消费导致超时
- **WHEN** 动作请求入队后在超时窗口内始终没有被游戏线程消费
- **THEN** bridge MUST 将该请求标记为 `failed` 或等效失败状态
- **THEN** 返回结果 MUST 明确指出失败发生在队列消费前阶段，并包含最少必要的诊断 metadata

#### Scenario: 执行阶段发生运行时异常
- **WHEN** 动作请求已通过校验并已进入实际执行阶段，但执行中抛出异常
- **THEN** bridge MUST 将该请求标记为 `failed` 或等效失败状态
- **THEN** 返回结果 MUST 包含可诊断的错误信息
- **THEN** mod MUST NOT 因该异常导致整个 bridge 不可用
