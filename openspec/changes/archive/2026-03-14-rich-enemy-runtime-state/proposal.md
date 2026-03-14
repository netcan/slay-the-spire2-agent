## Why

当前 bridge 虽然已经导出敌人的基础信息，例如 `name`、`hp`、`block`、`intent_damage`、`powers`，但对 agent 来说仍然不够“可推理”：很多怪物的真实威胁来自更细的行动文本、意图附带效果、固有机制、种类标签或可复用的规范化标识，而不是单一的伤害数字。现在需要继续把 enemy 侧信息做厚，先把 runtime 当前能稳定读到的敌人观测补齐，为后续更强的战斗决策、怪物机制百科与检索增强打基础。

## What Changes

- 扩展 combat `snapshot.enemies[]`，在保留现有基础字段的同时，补充更丰富的敌人观测信息，例如行动显示文本、行动描述、结构化效果、敌人 trait/tag/keyword 或等效稳定字段。
- 统一 enemy 侧文本解析链路，让敌人行动、固有效果、powers 与说明文本尽量复用现有 glossary / description 提取能力，避免只暴露原始 runtime 对象名或模糊 intent 摘要。
- 明确 live runtime 对敌人扩展字段的降级策略：单个敌人的某个扩展字段读取失败时，不得让整个 enemy 列表或 combat snapshot 失效，并在 metadata 中保留最小诊断信息。
- 调整 Python models、bridge、fixtures 与 LLM snapshot 摘要，让上层策略可以直接消费 richer enemy state，而不是只依赖 `intent_damage` 和 `powers` 自行猜测怪物机制。
- 为后续接入敌人机制百科预留可扩展锚点，例如更稳定的 `canonical_enemy_id`、行动规范化文本和结构化效果字段，但本次先聚焦 runtime 可直接导出的事实层状态。

## Capabilities

### New Capabilities
- 无

### Modified Capabilities
- `game-bridge`: 调整统一快照契约，要求 combat `enemies[]` 导出更丰富的敌人行动与机制观测字段。
- `in-game-runtime-bridge`: 调整 live runtime 敌人读取约束，要求稳定提取 richer enemy state，并在单字段失败时保持 fail-safe。
- `mod-state-export`: 调整 mod 状态导出语义，要求敌人除基础血量/意图外，还导出可供 agent 使用的结构化补充信息。

## Impact

- 受影响代码主要位于 `mod/Sts2Mod.StateBridge/Providers/Sts2RuntimeReflectionReader.cs`、enemy/runtime contracts、window extractors、fixture provider 与相关测试。
- Python 侧会影响 `src/sts2_agent/models.py`、`src/sts2_agent/bridge/`、fixtures、validation 与 `src/sts2_agent/policy/llm.py`。
- 需要补充 live 验证，确认真实战斗中至少一类常见敌人的 richer enemy fields 可读、可诊断，并与现有 `intent` / `powers` 语义一致。
