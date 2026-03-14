## MODIFIED Requirements

### Requirement: runner 必须用当前 legal actions 驱动模型连续决策
系统 MUST 提供一个 `llm-autoplay-runner`，在每一步从 bridge 读取当前 `snapshot` 与 `legal actions`，再调用 LLM policy 生成动作决策。runner MUST 只允许模型从当前 legal set 中选择动作，并在提交前完成本地校验。在 `combat` 玩家回合内，runner MUST 能连续执行多步决策，而不是默认只打一手就退出。

#### Scenario: 模型选择当前合法动作
- **WHEN** runner 拿到当前 decision 的 `legal actions`
- **THEN** runner MUST 将这些动作传给 LLM policy
- **THEN** 若模型返回的 `action_id` 属于当前 legal set，runner MUST 才能提交到 bridge

#### Scenario: 模型返回不存在的 action_id
- **WHEN** 模型返回的 `action_id` 不属于当前 legal set
- **THEN** runner MUST 将该结果视为无效模型输出
- **THEN** runner MUST NOT 直接调用 `/apply`

#### Scenario: 同一玩家回合内连续执行多步
- **WHEN** 当前 phase 为 `combat`，且玩家回合仍可继续提交 legal actions
- **THEN** runner MUST 在每次动作执行后重新读取最新 `snapshot` 与 `legal actions`
- **THEN** runner MUST 基于最新 live state 继续请求模型决策，直到命中停止条件

### Requirement: runner 必须支持回合级停止条件、dry-run 与失败回退
runner MUST 支持 dry-run 模式、`max_steps` 限制、人工停止和模型失败中断。dry-run 模式下，runner MUST 完整执行读取与模型决策流程，但 MUST NOT 真的向 bridge 发送 `/apply`。在多步 autoplay 场景下，runner MUST 额外支持回合级停止条件，例如玩家回合结束、phase 切换、只剩 `end_turn`、模型 halt、bridge 拒绝或单回合动作数达到上限。

#### Scenario: dry-run 模式只记录不执行
- **WHEN** 调用方以 dry-run 模式启动 runner
- **THEN** runner MUST 获取 snapshot、actions 并调用模型
- **THEN** runner MUST 只记录计划动作，而不向 bridge 提交真实写请求

#### Scenario: 达到最大步数后停止
- **WHEN** 自动打牌步数达到 `max_steps`
- **THEN** runner MUST 停止继续请求模型
- **THEN** 结果 MUST 标记为因 `max_steps_exceeded` 或等效原因中断

#### Scenario: 只剩 end_turn 时结束本回合
- **WHEN** 当前玩家回合的 legal actions 只剩 `end_turn`
- **THEN** runner MUST 能按配置自动结束当前回合，或明确以回合完成状态停止
- **THEN** 运行结果 MUST 能区分这是“正常结束本回合”而不是异常中断

#### Scenario: 模型连续失败后中断
- **WHEN** 模型请求失败、解析失败或返回非法动作，且已达到允许的重试上限
- **THEN** runner MUST 中断当前 autoplay
- **THEN** 结果 MUST 明确记录失败原因，而不是继续盲打

### Requirement: runner 必须为多步执行落盘可复盘 trace 与回合摘要
runner MUST 为每一步保存结构化 trace，至少包含当前 snapshot、legal actions、模型输出、bridge 回执与时间戳。若模型请求已发出，trace SHOULD 包含请求摘要、原始响应文本或等效诊断字段，便于回放与排障。对于多步 autoplay，运行结果 MUST 能总结本回合执行了多少步、为何停止、是否正常完成该回合。

#### Scenario: 正常执行多步动作
- **WHEN** runner 在同一玩家回合内完成多轮“读取状态 -> 调模型 -> 提交动作”
- **THEN** 每一步 trace MUST 记录 observation、legal actions、policy_output 与 bridge_result
- **THEN** trace MUST 能区分这些记录属于同一次回合级 autoplay

#### Scenario: 本回合正常结束后输出摘要
- **WHEN** runner 因玩家回合结束、phase 切换或自动 `end_turn` 成功而停止
- **THEN** `RunSummary` 或等效结果 MUST 记录本回合执行动作数与停止原因
- **THEN** 调用方 MUST 能区分“回合完成”与“异常中断”

#### Scenario: 模型侧失败也有 trace
- **WHEN** runner 在模型请求或响应解析阶段失败
- **THEN** trace MUST 记录失败时的 snapshot、legal actions 与错误信息
- **THEN** 后续分析 MUST 能区分是模型失败还是 bridge 失败

### Requirement: runner 必须提供面向多步回合执行的调试入口
系统 MUST 提供一个本地可执行的调试入口，用于连接 OpenAI 兼容接口和 live bridge 完成端到端联调。该入口 MUST 支持通过参数或环境变量设置 `base_url`、`model`、`api_key`、`bridge_base_url`、`dry_run` 与 `trace_dir`。对于多步 autoplay，该入口 MUST 支持配置单回合动作上限或等效安全边界。

#### Scenario: 使用本地 chat completions 接口启动完整玩家回合 autoplay
- **WHEN** 调用方把 `base_url` 设为 `http://127.0.0.1:8080/v1`，并启用多步回合模式
- **THEN** 调试入口 MUST 能连接 live bridge 并连续执行多步决策
- **THEN** 调试入口 MUST 在当前玩家回合结束或命中安全停止条件后退出

#### Scenario: CLI 参数覆盖回合级默认配置
- **WHEN** 调用方在命令行显式传入回合级参数，如单回合动作上限或是否自动 `end_turn`
- **THEN** 调试入口 MUST 使用这些参数覆盖默认值
- **THEN** 实际运行配置 MUST 可在 trace 或启动日志中被确认
