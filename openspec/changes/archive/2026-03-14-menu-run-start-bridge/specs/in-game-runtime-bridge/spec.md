## ADDED Requirements

### Requirement: 无活动 run 时必须导出 menu phase 与可执行开局动作
当 STS2 已启动且 mod 已加载，但当前没有活动 run（例如处于主菜单、开局配置、角色选择等流程）时，bridge MUST 仍然能够导出结构化快照与 legal actions，以支持自动化进入 run。此时 `snapshot` MUST 使用 `phase="menu"`（或等效稳定值）表示当前处于菜单/开局流程；并且 MUST NOT 伪造 `combat`、`map`、`reward` 的 run 内数据。

#### Scenario: 主菜单存在 Continue 时导出 continue_run
- **WHEN** 当前处于主菜单且 Continue/继续 按钮可用
- **THEN** `snapshot.phase` MUST 等于 `menu`
- **THEN** `actions` MUST 包含 `type="continue_run"` 的 legal action
- **THEN** 该 action 的 `label` MUST 为玩家可读的按钮文本或等效可读文本

#### Scenario: 主菜单无存档时导出 start_new_run
- **WHEN** 当前处于主菜单且 Continue/继续 不可用，但 New Run/开始 等入口可用
- **THEN** `snapshot.phase` MUST 等于 `menu`
- **THEN** `actions` MUST 包含 `type="start_new_run"` 的 legal action
- **THEN** bridge MUST 在 `metadata` 中提供 `menu_detection_source` 或等效 diagnostics，便于定位识别路径

#### Scenario: 进入新 run 配置后导出角色选择与确认动作
- **WHEN** 玩家已进入新 run 配置流程，且界面存在角色列表与“开始/确认”按钮
- **THEN** `actions` MUST 包含一个或多个 `type="select_character"` 的 legal actions（每个角色一个）
- **THEN** `actions` MUST 在可用时包含 `type="confirm_start_run"` 的 legal action
- **THEN** `select_character` MUST 通过 `params.character_id` 或等效稳定参数区分不同角色

#### Scenario: 不确定菜单状态时必须 fail-safe
- **WHEN** bridge 无法稳定识别当前菜单流程（例如被遮挡弹窗、版本不兼容、字段探测失败）
- **THEN** bridge MUST 返回可序列化的 `snapshot`，其 `phase` MUST 为 `menu` 或 `unknown` 的稳定值
- **THEN** bridge MUST NOT 导出可能误触危险路径的动作（例如 Exit/Abandon/删除存档 等）
- **THEN** bridge MUST 在 `metadata` 中返回可诊断信息（例如探测失败原因或候选控件摘要）

