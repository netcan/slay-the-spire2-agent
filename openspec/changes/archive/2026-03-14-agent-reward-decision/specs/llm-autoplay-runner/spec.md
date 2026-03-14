## ADDED Requirements

### Requirement: runner 必须支持 reward 决策模式并安全执行 reward 动作
runner MUST 支持在 `snapshot.phase = reward` 时对奖励窗口做出决策，并通过 bridge 提交 `choose_reward` / `skip_reward` 等 reward 动作。runner MUST 提供显式的 reward 决策模式开关，默认 MUST 为安全模式（不自动领取奖励），避免在未配置策略时对跑局结果产生不可控影响。

#### Scenario: 默认 reward 模式为 halt
- **WHEN** 调用方未显式启用 reward 决策，且 runner 观测到 `snapshot.phase = reward`
- **THEN** runner MUST 停止继续自动决策并返回明确的停止原因（例如 `reward_phase_reached`）
- **THEN** runner MUST NOT 对 bridge 发起真实 `/apply`

#### Scenario: reward_mode=skip 时仅自动跳过奖励
- **WHEN** 调用方将 reward 决策模式设为 `skip` 且当前 legal actions 中包含 `skip_reward`
- **THEN** runner MUST 直接提交 `skip_reward` 并等待窗口推进
- **THEN** runner MUST 在 trace 中记录该决策与 bridge 回执

#### Scenario: reward_mode=llm 时允许模型选择 reward 动作
- **WHEN** 调用方将 reward 决策模式设为 `llm` 且当前 legal actions 中存在 `choose_reward` 或 `skip_reward`
- **THEN** runner MUST 将 reward legal actions 提供给 LLM policy 生成决策
- **THEN** 若模型返回的 `action_id` 属于当前 legal set，runner MUST 才能提交到 bridge；否则 MUST 视为无效输出并按失败回退语义处理

## MODIFIED Requirements

### Requirement: runner 必须支持回合级停止条件、dry-run 与失败回退
runner MUST 支持 dry-run 模式、`max_steps` 限制、人工停止和模型失败中断。dry-run 模式下，runner MUST 完整执行读取与模型决策流程，但 MUST NOT 真的向 bridge 发送 `/apply`。在多步 autoplay 场景下，runner MUST 额外支持回合级停止条件，例如玩家回合结束、phase 切换、只剩 `end_turn`、模型 halt、bridge 拒绝或单回合动作数达到上限。

当 runner 启用了 reward 决策模式（非 `halt`）时，`reward` phase MUST NOT 被简单视为“停止条件”；runner MUST 继续在 reward 窗口完成一次或多次 reward 动作提交，直到 phase 推进到后续窗口或命中其他停止条件。

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

#### Scenario: reward_mode 启用时进入 reward 不应直接停止
- **WHEN** runner 已启用 reward 决策模式（非 `halt`），且观测到 `snapshot.phase = reward`
- **THEN** runner MUST 进入 reward 决策与提交流程，而不是把 `phase_changed` 作为立即停止原因
- **THEN** 若 reward legal actions 为空或无法安全选择，runner MUST 以明确 reason 停止，而不是无休止等待

#### Scenario: 模型连续失败后中断
- **WHEN** 模型请求失败、解析失败或返回非法动作，且已达到允许的重试上限
- **THEN** runner MUST 中断当前 autoplay
- **THEN** 结果 MUST 明确记录失败原因，而不是继续盲打

### Requirement: runner 必须提供面向多步回合执行的调试入口
系统 MUST 提供一个本地可执行的调试入口，用于连接 OpenAI 兼容接口和 live bridge 完成端到端联调。该入口 MUST 支持通过参数或环境变量设置 `base_url`、`model`、`api_key`、`bridge_base_url`、`dry_run` 与 `trace_dir`。对于多步 autoplay，该入口 MUST 支持配置单回合动作上限或等效安全边界。该入口 MUST 支持配置 reward 决策模式（例如 `reward_mode=halt|skip|llm`），以便在真实游戏中验证 reward 行为。

#### Scenario: 使用本地 chat completions 接口启动完整玩家回合 autoplay
- **WHEN** 调用方把 `base_url` 设为 `http://127.0.0.1:8080/v1`，并启用多步回合模式
- **THEN** 调试入口 MUST 能连接 live bridge 并连续执行多步决策
- **THEN** 调试入口 MUST 在当前玩家回合结束或命中安全停止条件后退出

#### Scenario: CLI 参数覆盖回合级默认配置
- **WHEN** 调用方在命令行显式传入回合级参数，如单回合动作上限或是否自动 `end_turn`
- **THEN** 调试入口 MUST 使用这些参数覆盖默认值
- **THEN** 实际运行配置 MUST 可在 trace 或启动日志中被确认

#### Scenario: CLI 参数显式启用 reward 决策模式
- **WHEN** 调用方在命令行显式传入 reward 决策参数（例如 `--reward-mode skip`）
- **THEN** 调试入口 MUST 按该模式处理 `snapshot.phase = reward` 的窗口
- **THEN** 调试入口 MUST 在 trace 中记录 reward 决策的输入与输出

