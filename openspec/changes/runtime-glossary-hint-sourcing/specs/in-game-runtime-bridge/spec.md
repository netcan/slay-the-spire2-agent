## ADDED Requirements

### Requirement: live runtime glossary hint 必须优先来自游戏真实文本来源
当 bridge 为 cards、powers、potions、enemy move 或等效 runtime 对象导出 glossary anchors 时，`hint` MUST 优先来自游戏 runtime 可读的真实词条来源，例如 `HoverTip.Description`、模型 `Description` / `SmartDescription`、`LocString` 或等效 localization 结果，而不是默认使用手写摘要文本。

#### Scenario: runtime 可读取术语 hover tip 时直接导出真实说明
- **WHEN** glossary term 对应的 runtime 对象可提供 `HoverTip` 或等效术语说明节点
- **THEN** 导出的 glossary `hint` MUST 使用该 runtime 节点解析出的文本
- **THEN** glossary `source` MUST 标识为 `runtime_hover_tip` 或等效真实来源，而不是 `fallback_builtin`

#### Scenario: 没有 hover tip 时回退到模型描述或 localization
- **WHEN** runtime 对象不存在可读 `HoverTip`，但存在 `Description`、`SmartDescription`、`LocString` 或等效 localization 入口
- **THEN** bridge MUST 继续尝试从这些来源解析 glossary `hint`
- **THEN** 只有在这些真实来源都不可用时，bridge 才可以进入 fallback

### Requirement: live runtime glossary fallback 必须显式告警且不得伪装成真实来源
当 glossary `hint` 无法从游戏 runtime 或 localization 获得时，bridge MAY 导出空 hint 或最小 fallback，但 MUST 在日志或 diagnostics 中显式告警，并且 MUST NOT 把 fallback 结果标记成 `description_text`、`runtime_hover_tip` 或其他看似真实的来源。

#### Scenario: glossary hint 解析失败时打印 warning 并保持 fail-safe
- **WHEN** 某个 glossary term 只能依赖最小 fallback 或最终拿不到 hint
- **THEN** bridge MUST 继续返回可序列化的 glossary anchor
- **THEN** bridge MUST 在日志中打印包含 glossary id、对象路径与 fallback 阶段的 warning

#### Scenario: fallback source 语义对外可区分
- **WHEN** glossary `hint` 来自 built-in fallback 或根本缺失
- **THEN** glossary `source` MUST 标识为 `fallback_builtin`、`missing_hint` 或等效可区分来源
- **THEN** 调用方 MUST 能据此区分“真实游戏说明”和“bridge 临时兜底”
