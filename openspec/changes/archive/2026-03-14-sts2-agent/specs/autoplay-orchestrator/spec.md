## ADDED Requirements

### Requirement: Orchestrator 逐步执行策略驱动的自动打牌
系统 MUST 运行一个 autoplay 循环：读取 bridge 提供的最新快照和合法动作，调用已配置 policy，提交一个被选择的动作，并持续重复直到会话结束或满足人工停止条件。

#### Scenario: policy 驱动一回合实时战斗
- **WHEN** autoplay 已为一个可控会话启动，且 bridge 报告当前存在合法决策窗口
- **THEN** orchestrator 只为该窗口选择并提交一个合法动作，然后继续处理下一个决策窗口

### Requirement: Orchestrator 支持可插拔 policy 实现
系统 MUST 提供统一的 policy 接口，该接口接收当前 observation 与合法动作集合，并返回一个被选择的合法动作，或者返回显式 halt 结果。

#### Scenario: 不同 policy 复用同一套 bridge 契约
- **WHEN** 启发式 policy 与 LLM policy 都被配置到 orchestrator 中
- **THEN** 两者都可以消费同一份标准化 bridge 输入，并在无需 bridge 特定分支代码的前提下返回动作结果

### Requirement: Orchestrator 为每次尝试决策持久化 trace
系统 MUST 为每次 autoplay 决策尝试持久化一条 trace 记录，其中包含时间戳、决策标识、标准化 observation 元数据、合法动作集合、选中动作或 halt 结果，以及 bridge 返回值。

#### Scenario: 成功提交动作后写入 trace
- **WHEN** orchestrator 提交一个合法动作并被 bridge 接受
- **THEN** 系统写入一条可用于回放、调试或评估的 trace 记录

### Requirement: Orchestrator 在 bridge 或 policy 失败时安全停止
系统 MUST 在 bridge 拒绝动作、决策窗口失步、或 policy 在配置限制内未返回有效结果时停止 autoplay，并将会话标记为 interrupted。

#### Scenario: autoplay 过程中 bridge 拒绝了过期动作
- **WHEN** bridge 因提交动作已不再匹配当前活动决策窗口而返回拒绝结果
- **THEN** orchestrator 停止继续发出后续动作，并在 trace 输出中记录此次中断原因
