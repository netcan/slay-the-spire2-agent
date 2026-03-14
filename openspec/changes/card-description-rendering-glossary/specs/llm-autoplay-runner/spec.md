## MODIFIED Requirements

### Requirement: runner 必须用当前 legal actions 驱动模型连续决策
系统 MUST 提供一个 `llm-autoplay-runner`，在每一步从 bridge 读取当前 `snapshot` 与 `legal actions`，再调用 LLM policy 生成动作决策。runner MUST 只允许模型从当前 legal set 中选择动作，并在提交前完成本地校验。在 `combat` 中，runner MUST 能跨多个玩家回合连续执行，而不是在单个玩家回合结束后默认退出。当 snapshot 已提供描述渲染文本、变量槽位或 glossary 锚点时，runner MUST 优先将这些高价值字段提供给策略层，而不是只把原始模板文本交给模型。

#### Scenario: 模型选择当前合法动作
- **WHEN** runner 拿到当前 decision 的 `legal actions`
- **THEN** runner MUST 将这些动作传给 LLM policy
- **THEN** 若模型返回的 `action_id` 属于当前 legal set，runner MUST 才能提交到 bridge

#### Scenario: 渲染描述与词条锚点可用时进入策略输入
- **WHEN** bridge 已在当前 snapshot 中导出 `description_rendered`、`description_vars` 或 glossary 锚点
- **THEN** runner MUST 优先把这些字段纳入策略输入摘要
- **THEN** 策略层 MUST 不再只能依赖卡名与模板文本猜测效果
