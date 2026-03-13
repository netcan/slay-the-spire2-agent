## ADDED Requirements

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
