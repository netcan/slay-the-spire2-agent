## Why

当前 bridge 已经能导出卡牌 `description`，但很多文本仍然是模板或富文本标记，例如 `获得{Block:diff()}点[gold]格挡[/gold]。`。这类文本对大模型和规则策略都不够友好：模型既拿不到当前回合的真实数值，也分不清 `[gold]...[/gold]` 是样式标签还是术语本身，更无法稳定理解“格挡”“易伤”“力量”等关键状态词条的语义。

现在推进卡牌描述渲染与词条语义锚点，能把 runtime facts 从“半结构化字符串”提升到“可直接消费的描述 + 变量 + 词条锚点”，让 agent 不再只靠卡名和模糊模板猜效果，并为后续扩展长期知识层、怪物机制百科和更强策略推理打好基础。

## What Changes

- 为卡牌、powers 等对象补充分层文本字段，区分 `description_raw`、`description_rendered` 与结构化 `description_vars`。
- 为 bridge 协议增加词条语义锚点，如稳定 `keyword_ids` / `glossary_keys`，并允许上层按锚点补充中文解释。
- 在 mod runtime reader 中优先补齐当前战斗可稳定获取的数值渲染结果，例如当前卡牌伤害、格挡、力量加成后的实时文本。
- 在 Python 侧更新 models、HTTP decode 与 LLM snapshot 摘要，优先向策略层提供渲染后描述与高价值词条解释，而不是原始模板噪声。
- 增加 fixture、单测与至少一次 live validation，验证卡牌描述渲染、变量提取与词条语义锚点已经进入 agent 输入。

## Capabilities

### New Capabilities
- `card-description-rendering-glossary`: 定义卡牌/状态描述的原始模板、渲染文本、变量槽位与词条语义锚点约定。

### Modified Capabilities
- `mod-state-export`: 扩展 mod 导出的卡牌与状态文本要求，使其必须支持渲染后描述、变量提取与词条锚点。
- `game-bridge`: 扩展 bridge 快照契约，使其能稳定暴露渲染描述与词条语义相关字段。
- `llm-autoplay-runner`: 调整 runner 对 snapshot 的消费要求，使策略层优先接收渲染后描述与高价值词条提示。

## Impact

- 受影响代码主要包括 `mod/Sts2Mod.StateBridge/Providers/Sts2RuntimeReflectionReader.cs`、`mod/Sts2Mod.StateBridge/Contracts/*.cs`、`src/sts2_agent/models.py`、`src/sts2_agent/bridge/http.py`、`src/sts2_agent/policy/llm.py`。
- 需要新增或扩展描述渲染 helper、词条锚点抽取逻辑、fixture 数据与 live validation artifacts。
- 会引入协议字段追加，但应保持向后兼容：旧 `description` 仍可保留，新字段采用追加式、可选字段策略。
