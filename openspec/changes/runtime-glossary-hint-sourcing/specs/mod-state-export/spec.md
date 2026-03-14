## ADDED Requirements

### Requirement: Mod 导出的 glossary hint 必须反映真实来源语义
系统在导出 glossary anchors 时，`display_text` 与 `hint` SHOULD 尽量来自游戏 runtime / localization 的真实文本；若使用 fallback，导出结构 MUST 准确表达其来源语义，不得把手写说明伪装成游戏原始词条说明。

#### Scenario: Power glossary 说明来自真实 power 文本
- **WHEN** 某个 power 或等效模型存在可解析的标题与说明文本
- **THEN** 对应 glossary anchor MUST 优先复用该模型或其 hover tip 的真实文本
- **THEN** `hint` 内容 MUST 与当前语言环境下的游戏词条说明保持一致或等效

#### Scenario: fallback glossary 不再冒充游戏原文
- **WHEN** 某个 glossary term 当前只能依赖 bridge 内置 fallback
- **THEN** 该 glossary anchor MAY 返回空 `hint` 或最小 fallback `hint`
- **THEN** 其 `source` MUST 明确标识 fallback 语义，且 metadata 或日志 MUST 可用于定位缺口
