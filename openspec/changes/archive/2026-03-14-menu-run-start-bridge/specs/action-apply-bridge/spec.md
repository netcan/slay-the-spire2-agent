## ADDED Requirements

### Requirement: bridge 必须支持菜单开局动作的受控执行映射
系统 MUST 支持在 `menu` phase 下提交并执行开局相关动作，以便自动化从主菜单进入活动 run。bridge MUST 仅在对应动作出现在当前 legal actions 中时才允许执行，并 MUST 复用现有 `decision_id` 校验、只读开关与受控队列消费机制。执行结果 MUST 返回可诊断回执，区分 `accepted`、`rejected`、`failed`，并在失败时返回结构化错误码（例如 `stale_action`、`runtime_incompatible`、`not_clickable`）。

#### Scenario: 执行 continue_run 进入存档 run
- **WHEN** 当前 phase 为 `menu` 且 legal actions 中存在 `continue_run`
- **THEN** bridge MUST 能将该动作映射到游戏内 Continue/继续 按钮的真实点击/激活流程
- **THEN** 执行后新的 `snapshot.phase` MUST 最终推进到 `map`、`combat`、`reward` 或等效 run 内 phase

#### Scenario: 执行 start_new_run 进入新 run 配置流程
- **WHEN** 当前 phase 为 `menu` 且 legal actions 中存在 `start_new_run`
- **THEN** bridge MUST 能将该动作映射到 New Run/开始 等入口的真实点击/激活流程
- **THEN** 执行后 `snapshot.phase` MUST 仍为 `menu`（处于开局流程）或推进到 run 内 phase，并且 `decision_id/state_version` MUST 发生推进

#### Scenario: 角色选择与确认必须遵循 legal actions 参数
- **WHEN** 当前 phase 为 `menu` 且 legal actions 中存在某个 `select_character`，其 `params.character_id` 为 `<id>`
- **THEN** bridge MUST 仅选择与 `<id>` 对应的角色，不得改写为其他角色
- **THEN** 若当前界面已变化或角色列表不一致，bridge MUST 拒绝执行并返回 `stale_action` 或等效错误原因

#### Scenario: 目标控件不可点击时返回可诊断失败
- **WHEN** 某个菜单动作已通过提交校验，但执行阶段发现目标按钮不可点击、被遮挡或无法解析
- **THEN** bridge MUST 将该请求标记为 `failed` 或 `rejected`
- **THEN** 返回结果 MUST 指明失败原因与阶段（例如 `not_clickable`、`runtime_incompatible`，并包含 `runtime_handler`/diagnostics）

