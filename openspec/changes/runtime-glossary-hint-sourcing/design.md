## Context

当前项目已经能在卡牌、powers、enemy move 等文本中识别 glossary anchors，但 glossary `hint` 主要来自 `KnownGlossarySpecs` 手写表。这个方案在“快速给 agent 一点术语解释”上有效，却已经暴露出两个结构性问题：其一，手写说明会偏离游戏真实文案；其二，当前 `source=description_text` 只能表达“术语是在文本里命中的”，不能表达 `hint` 本身的真实来源。

前期探索已经确认 STS2 runtime 自身具备更可靠的候选来源：`PowerModel`、`PotionModel`、`CardModel` 暴露了 `Description`、`SmartDescription`、`HoverTips`、`HoverTip` 等字段；`HoverTip` 本身有 `Title` 与 `Description`；`LocString` 支持 `GetFormattedText()`、`GetRawText()` 和按 key 取值。与此同时，游戏资源中也存在像 `VULNERABLE_POWER.description` / `smartDescription` 这样的真实词条文本，说明 glossary hint 有机会直接对齐游戏原始语义，而不是继续依赖手写摘要。

## Goals / Non-Goals

**Goals:**
- 建立 glossary hint 的统一解析链路，优先从 runtime hover tip、模型描述或 localization 资源获取真实文本。
- 尽量移除当前手写 hint 表，仅保留稳定 glossary id、术语别名与必要的最小 fallback 元数据。
- 让导出的 `source` 或 diagnostics 能准确表达 hint 来源，而不是混淆“术语命中位置”和“hint 文本来源”。
- 在拿不到真实 hint 时保持 fail-safe：允许导出空 hint 或最小 fallback，但必须打印日志告警，方便定位缺口。
- 为常见术语补充可验证样例，确保 live runtime 中至少一部分类别已经走到真实来源。

**Non-Goals:**
- 本次不构建完整离线百科系统，也不在 payload 中引入长篇术语说明。
- 本次不要求一次性覆盖所有 glossary term；无法解析的条目可以先走受控 fallback。
- 本次不重构现有 action 协议或 LLM 决策主流程，重点只在 glossary hint sourcing。

## Decisions

### 1. 将 glossary 解析拆成“术语识别”和“hint sourcing”两个阶段
- 决策：保留 glossary id 识别能力，但将 `hint` 文本获取从当前手写表中拆离，改为单独的 runtime/localization sourcing pipeline。
- 原因：`glossary_id` 的稳定性和 `hint` 的真实性是两个问题。术语识别可以继续依赖 canonical id、关键词和文本锚点；但 `hint` 应优先来自游戏真实数据。
- 备选：继续用一张 `KnownGlossarySpecs` 同时承担识别与解释。未采用，因为这会继续把“识别成功”和“解释可信”绑死在同一份手写表上。

### 2. hint 获取优先级采用 runtime first
- 决策：优先级按 `HoverTip.Description` -> 模型 `SmartDescription` / `Description` -> localization key 解析 -> 最小 fallback。
- 原因：`HoverTip` 最接近玩家鼠标悬浮时看到的真实词条；`SmartDescription` / `Description` 通常是模型级别的官方说明；再往下才是需要自己拼的 localization key；最后才允许 fallback。
- 备选：优先读取资源文件中的 localization 文本。未采用为首选，因为 runtime 上下文已能给出更贴近当前语言和对象实例的结果。

### 3. 手写表尽量去掉，只保留最小 fallback 识别元数据
- 决策：尝试移除手写 `hint` 文案，保留 glossary id、显示名或匹配 alias 等最小识别信息；若个别术语在当前阶段完全找不到 runtime 来源，可用 `fallback_builtin` 暂时兜底，但必须显式标注并打印告警。
- 原因：用户已经明确要求“手写表尝试去掉”；长期看，这也能减少文案漂移。
- 备选：把手写文案改得更像游戏原文。未采用，因为根本问题不是“写得像不像”，而是它仍然不是 runtime 真源。

### 4. `source` 与日志分开表达“命中位置”和“hint 来源”
- 决策：glossary anchor 对外 `source` 字段优先表达 hint 的来源；若仍需保留术语命中位置，则放入 metadata/diagnostics。fallback 时记录 warning 日志，包含 glossary id、对象路径、失败阶段和所用 fallback。
- 原因：当前 `source=description_text` 容易让人误读。新的语义应该让调用方一眼看出“这个 hint 是从游戏 hover tip 来的，还是只是 fallback”。
- 备选：继续复用单个 `source` 字段，不额外记录 diagnostics。未采用，因为这正是现在语义混淆的来源。

## Risks / Trade-offs

- [部分术语不是独立 power，也没有现成 HoverTip] -> 允许短期导出空 hint，并记录 warning，后续再补 localization key 路径。
- [runtime 反射链路更复杂，字段名可能随版本漂移] -> 优先复用现有 `RuntimeTextResolver` / `LocString` 解析，并将新路径收敛到少量 helper 内。
- [移除手写表后，短期内可用 hint 数量可能下降] -> 通过 live validation 先覆盖高价值术语，并用日志统计剩余 fallback 缺口。
- [旧调用方依赖当前 `hint` 文案] -> 字段结构保持兼容，但文案与 `source` 语义会变化；需要在 change 中明确这是“可信度提升”而非“文本稳定不变”。

## Migration Plan

1. 先梳理 glossary anchor 的对象来源，区分 power、potion、card、enemy move 等不同类型的 hint sourcing 入口。
2. 在 runtime reader 中增加 runtime/localization hint 解析 helper，并将现有 glossary 组装逻辑切换到新 helper。
3. 收缩手写 glossary 表，只保留识别 alias 与必要 fallback；fallback 命中时统一打 warning 日志。
4. 更新 fixture、单测和 live validation，确认 `vulnerable` 等常见词条已优先使用游戏真实说明。
5. 若某些术语暂时只能 fallback，在 diagnostics/日志中记录缺口，后续再做增量补全。

## Open Questions

- `block`、`draw`、`exhaust` 这类规则关键字是否存在统一可读的 runtime/localization key，还是需要从多个对象类型分别映射？
- glossary anchor 的 `display_text` 是否也应该尽量走 runtime/localization，而不是继续依赖当前最小映射表？
- 对外是否需要新增更细的 `hint_source` 字段，还是复用现有 `source` 并把“命中位置”完全下沉到 diagnostics 即可？
