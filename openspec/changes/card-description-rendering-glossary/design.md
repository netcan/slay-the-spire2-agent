## Context

当前项目已经能在 live runtime 中导出 richer combat state，但卡牌与 powers 的文本仍停留在“半结构化模板”阶段。例如 `获得{Block:diff()}点[gold]格挡[/gold]。` 同时混杂了变量槽位、富文本样式和术语本体。对 agent 而言，这会带来三个问题：一是读不到当前战斗下的真实数值；二是无法区分样式标签与业务语义；三是没有稳定词条锚点，难以与后续 glossary / encyclopedia / 长期知识层对接。

这个变更会同时影响 mod contracts、runtime reader、Python models、HTTP bridge 与 LLM 摘要逻辑，属于跨层 schema 演进。现有 rich runtime state 已经提供 `canonical_*` 锚点和 `keywords` 基础槽位，因此这次可以继续沿着“事实层 + 知识锚点”方向推进，而不是直接把整套百科内容硬编码进 live payload。

## Goals / Non-Goals

**Goals:**
- 为卡牌与 powers 文本建立分层导出结构，至少区分 `description_raw`、`description_rendered` 与 `description_vars`。
- 将当前战斗下可稳定得到的动态数值渲染进导出文本，例如伤害、格挡、力量修正后的当前值。
- 为“格挡”“力量”“易伤”等关键状态词条提供稳定 glossary 锚点，而不是只保留本地化字符串。
- 在 Python 侧让策略层优先消费渲染后文本与高价值词条提示，并保持旧字段兼容。
- 通过 fixture 与 live validation 明确区分“文本已经渲染成功”和“仍回退为模板文本”的场景。

**Non-Goals:**
- 本次不构建完整百科数据库，不在 live payload 中塞入长篇解释文本。
- 本次不要求一次性补齐所有卡牌、所有 powers、所有怪物术语的完整语义库。
- 本次不改变现有动作协议，只扩展状态与策略输入。

## Decisions

### 1. 描述字段分层，而不是继续复用单个 `description`
- 决策：为卡牌、powers 等对象追加 `description_raw`、`description_rendered`、`description_vars`，旧 `description` 保留并逐步对齐为 `description_rendered` 的兼容别名。
- 原因：原始模板、运行时渲染文本、结构化变量的稳定性和用途不同，混成一个字段会让上层难以判断质量。
- 备选：继续只导出 `description`。未采用，因为这正是当前信息模糊的根源。

### 2. 词条语义使用稳定锚点，不直接内嵌长解释
- 决策：导出 `keyword_ids` / `glossary_keys` / `canonical_*` 一类稳定锚点；LLM 摘要层只挑少量高价值词条给简短提示。
- 原因：完整解释文本更适合在 Python 知识层或离线词典中维护，live payload 只需提供稳定对接点。
- 备选：在 mod 里直接内嵌完整中文术语解释。未采用，因为难维护，也会快速膨胀 payload。

### 3. 优先导出“当前战斗渲染结果”，其次才是模板推断
- 决策：runtime reader 优先读取 UI 或 runtime 已计算好的渲染文本与实际数值；若拿不到，再退回模板 + 变量槽位。
- 原因：agent 做当前回合决策时，最重要的是当前真实效果，而不是卡面模板。
- 备选：只做模板字符串替换。未采用，因为很多效果并不是简单字符串替换能稳定表达。

### 4. Python 侧摘要优先发渲染文本，并对词条做轻量归一化
- 决策：`ChatCompletionsPolicy` 在 snapshot 摘要中优先发送 `description_rendered`，必要时附带简短 glossary 提示；原始模板只在诊断或缺省时作为后备。
- 原因：模型上下文有限，渲染后描述最直接，模板文本噪声更大。
- 备选：把 raw/rendered/template/glossary 全量都发给模型。未采用，因为会显著增加噪声和 token。

## Risks / Trade-offs

- [Runtime 里拿不到稳定渲染文本] → 先允许回退到 `description_raw + description_vars`，并在 metadata/diagnostics 中标记回退来源。
- [术语解释维护成本变高] → live payload 只保留稳定锚点，解释词典留给 Python 知识层按需维护。
- [字段过多导致 payload 膨胀] → trace 可保留完整信息，LLM 摘要只挑 `description_rendered`、关键变量和少量 glossary 提示。
- [不同本地化文本导致锚点不稳定] → glossary 以 canonical id / keyword id 为主，不依赖中文显示名做匹配。

## Migration Plan

1. 扩展 contracts 与 Python models，新增文本分层字段和 glossary 锚点字段。
2. 在 runtime reader 中优先接入卡牌与 power 的渲染文本、变量提取与 glossary key 推断。
3. 更新 HTTP decode、fixtures 与 LLM 摘要逻辑，优先消费 `description_rendered`。
4. 补充单测与至少一次 live validation，确认渲染描述、变量与词条锚点已经进入 agent 输入。
5. 后续若要做完整 glossary / encyclopedia，只在 Python 侧追加知识映射，不再重构 runtime facts。

## Open Questions

- STS2 runtime 是否能直接暴露“最终渲染后的卡牌文本”，还是需要组合多个字段自己格式化？
- `description_vars` 的字段名是否应该采用通用键（如 `damage/block/draw`），还是保留 runtime 内部键名并额外归一化？
- glossary 锚点应该挂在 `keywords` 上扩展，还是独立增加 `glossary_keys` 更清晰？
- enemy intent 的文本渲染与 glossary 提示是否要复用同一套机制，还是后续单独拆 change？
