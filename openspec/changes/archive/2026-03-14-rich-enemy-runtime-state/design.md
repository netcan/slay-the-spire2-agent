## Context

当前 `snapshot.enemies[]` 已经能导出基础战斗信息，例如生命、格挡、基础 `intent` 摘要、部分意图数值和 powers；但对于自动决策来说，这些信息仍然偏“压缩态”。很多敌人的危险并不只体现在 `intent_damage=11`，还体现在当前招式是否附带 debuff、是否带有持续被动、怪物属于哪一类机制、当前行动文本里出现了哪些关键词。现有 schema 也缺少一组稳定字段，让后续策略层把 runtime 事实层和敌人机制百科关联起来。

这个变更会跨 C# contracts、runtime 反射读取、fixture/provider、Python models/bridge/policy 与 live validation。它既涉及数据模型扩展，也涉及文本解析复用、fail-safe 边界，以及 prompt 规模控制。

## Goals / Non-Goals

**Goals:**
- 在 combat `snapshot.enemies[]` 中补充 richer enemy runtime fields，例如 `move_name`、`move_description`、`move_glossary`、`traits`、`keywords` 或等效稳定字段。
- 让敌人行动文本与 glossary 尽量复用现有 card/power 文本解析链路，避免新增一套平行但不一致的解析实现。
- 保持现有基础字段（`intent`、`intent_damage`、`powers`、`canonical_enemy_id` 等）继续可用，新增字段作为向后兼容增强。
- 在 live runtime 某个敌人的某个扩展字段不可读时保持 fail-safe，并在 metadata 中提供最小可诊断信息。
- 为后续敌人机制百科或检索增强预留稳定锚点，但不把百科知识硬编码进本次 bridge schema。

**Non-Goals:**
- 本次不直接接入外部敌人数据库、RAG 或百科服务。
- 本次不尝试推断“下一回合概率行动”“完整行为树”或跨回合隐藏状态。
- 本次不强制每个敌人都拿到完整 move id；runtime 读不到时允许降级到文本与关键词层。

## Decisions

### 1. 采用“基础字段保留 + enemy enrich fields 并列新增”

在 `enemies[]` 现有字段之外新增 richer fields，而不是重构 `intent` 为全新嵌套对象。这样旧客户端仍然可以继续读取 `intent` / `intent_damage`，新客户端则可以消费更完整的敌人信息。

备选方案是把现有敌人结构改成 `{intent_summary, move, passive, tags}` 的嵌套模型。这样会扩大 breaking surface，也会增加 Python/LLM 侧迁移成本，因此不选。

### 2. 优先导出五类补充字段：move_name / move_description / move_glossary / traits / keywords

首版聚焦 agent 最直接可用的字段：
- `move_name`: 当前行动显示名称或等效文本；
- `move_description`: 行动说明文本，尽量是玩家可读文本；
- `move_glossary`: 从行动文本中提取出的 glossary anchors；
- `traits`: 怪物自身的稳定 trait/tag/type；
- `keywords`: 从行动文本、traits、powers 中归一得到的机制关键词。

这组字段比直接上复杂 `planned_effects[]` 更容易稳定落地，同时已足够提升 LLM/规则策略可读性。若后续需要更精细的结构化效果，再在此之上追加而不是一次做过重。

### 3. 复用现有文本解析链路，但区分 enemy move 与 power/card 来源

`move_description` 与 `move_glossary` 复用现有 `RuntimeTextResolver`、description/glossary 归一能力，避免卡牌、powers、enemy move 三套规则不一致。实现上允许 enemy move 使用独立 source 标识，例如 `enemy_move_description`、`enemy_trait`、`enemy_power`，便于日志和 diagnostics 定位。

备选方案是为 enemy move 单独做一套 parser。这样短期可行，但会重复 glossary、fallback、LocString 解析逻辑，长期维护成本更高，因此不选。

### 4. diagnostics 以每个 enemy 独立降级，而不是按整个 enemy 列表失败

live runtime 中最容易不稳定的是某个敌人的某个扩展字段，例如招式对象路径、文本字段或 trait 容器。降级策略是：
- 单个字段读取失败：保留该敌人的基础字段，补充空字符串、空数组或缺失 optional 字段；
- 单个敌人 enrich 失败：仍保留该敌人的基础观测和现有 powers；
- metadata 记录 `enemy_export` 或等效 diagnostics，包含 enemy 索引、字段来源、fallback 原因；
- 仅在整个 enemy collection 都不可读时，才回落到现有更粗粒度的 enemy 导出。

### 5. LLM 摘要只带基础 enrich fields，不默认透传完整 diagnostics

Python / policy 层把 richer enemy fields 纳入摘要，但默认只传 `move_name`、`move_description`、`traits`、`keywords`、必要 glossary；不把 metadata diagnostics 原样传给模型，以控制 prompt 大小并避免干扰决策。调试细节继续留在日志和 validation artifacts。

## Risks / Trade-offs

- [不同怪物 runtime 结构差异大，move 字段不稳定] → 先把可稳定读取的文本和 tags 提上来，field-by-field 降级，不要求所有敌人同等完整。
- [prompt 体积增加] → policy 层只传精简版 enemy enrich fields，必要时后续再引入截断或按重要度摘要。
- [关键词提取过度依赖文本，可能出现噪声] → 关键词来源保守化，优先 traits/powers/明确 glossary，避免把任意文本 token 都当机制关键词。
- [schema 继续膨胀] → 本次限制在 agent 已经能直接消费的字段，不提前加入百科知识体或复杂预测结构。
