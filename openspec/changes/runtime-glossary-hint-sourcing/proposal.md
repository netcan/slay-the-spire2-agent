## Why

当前 bridge 导出的 glossary `hint` 主要来自 `KnownGlossarySpecs` 手写表，而不是游戏 runtime 或本地化资源本身。这会带来两个问题：一是文案可能与游戏真实词条说明不一致，例如 `易伤` 当前只导出“受到的攻击伤害提高”，但游戏实际说明包含“从攻击中受到的伤害增加 50%”；二是 `source` 容易误导调用方，把“命中了 description_text”误解成“hint 本身来自游戏描述”。

现在推进 glossary hint 的真实来源治理，能让 agent、调试工具和后续知识层消费到更可信的术语解释，同时减少手写表长期漂移和维护成本。

## What Changes

- 调整 glossary hint 解析优先级：优先使用 runtime `HoverTip`、`PowerModel` / `PotionModel` / `CardModel` 的本地化描述，或等效 localization 资源，而不是直接使用手写 hint。
- 尝试移除或显著收缩当前 `KnownGlossarySpecs` 中的手写说明文本，只保留必要的稳定 id / 识别别名 / 最小 fallback 元数据。
- 当 glossary hint 无法从游戏 runtime 或 localization 获得时，允许导出无 hint 或最小 fallback，并在日志中打印告警，避免静默伪造“看似可信但并非游戏原文”的说明。
- 明确 glossary `source` / diagnostics 语义，区分 `runtime_hover_tip`、`model_description`、`loc_string`、`fallback_builtin` 或等效来源。
- 补充 fixture、单元测试与至少一次 live validation，验证常见术语（如 `vulnerable`、`weak`、`block`）优先使用游戏真实词条说明。

## Capabilities

### New Capabilities
- 无

### Modified Capabilities
- `in-game-runtime-bridge`: 调整 live runtime glossary hint 的来源优先级、fallback 语义与日志告警要求。
- `mod-state-export`: 调整导出 glossary anchor 时 `hint` 与 `source` 的契约，避免把手写说明伪装成游戏原文。

## Impact

- 受影响代码主要包括 `mod/Sts2Mod.StateBridge/Providers/Sts2RuntimeReflectionReader.cs`、`mod/Sts2Mod.StateBridge/Providers/RuntimeTextResolver.cs`、fixture provider、相关 tests 与 live validation 脚本。
- glossary 导出字段结构大体可保持兼容，但 `hint` 的具体内容与 `source` 的取值会更严格、更贴近真实来源。
- 需要增加 runtime/localization 探测逻辑，以及 fallback 告警日志与 diagnostics 断言。
