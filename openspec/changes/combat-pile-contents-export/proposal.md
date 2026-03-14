## Why

当前 bridge 在战斗态只稳定导出手牌和牌堆计数，例如 `draw_pile`、`discard_pile`、`exhaust_pile` 的数量，但看不到这些牌堆里具体有哪些牌。这会让 agent 只能做短视决策：知道“还剩几张牌”，却不知道下一抽可能是什么、弃牌堆里有哪些可回收资源、消耗堆里损失了哪些关键牌。

现在需要把当前回合可见的抽牌堆、弃牌堆、消耗堆内容也纳入统一状态快照，先覆盖 combat 决策场景，为后续更强的长期规划、检索增强与规则 agent 打基础。

## What Changes

- 扩展战斗态 `snapshot.player`，在保留现有牌堆计数字段的同时，补充 `draw_pile_cards`、`discard_pile_cards`、`exhaust_pile_cards` 或等效稳定字段，导出当前牌堆中的卡牌列表。
- 统一这些牌堆卡牌对象与手牌复用同一基础 schema，例如 `card_id`、`name`、`canonical_card_id`、`description`、`cost`、`upgraded`、`traits`、`keywords` 等。
- 要求 live runtime bridge 在能稳定读取 pile contents 时优先导出真实内容；读取失败时保持 fail-safe，不得因为某个牌堆解析失败而让整个 combat snapshot 失效。
- 调整 Python bridge、fixtures、validation 与 LLM 输入摘要，使上层可以直接消费这些 pile contents，而不是只看到数量。

## Capabilities

### New Capabilities

- 无

### Modified Capabilities

- `game-bridge`: 调整统一快照契约，要求 combat 状态除手牌外还可导出抽牌堆、弃牌堆、消耗堆的结构化卡牌内容。
- `in-game-runtime-bridge`: 调整 live runtime 导出约束，要求 in-game mod 在战斗中尽可能稳定读取 pile contents，并在失败时给出可诊断降级。
- `mod-state-export`: 调整 mod 状态导出语义，要求 player state 同时导出牌堆计数与 pile contents。

## Impact

- 受影响代码主要位于 `mod/Sts2Mod.StateBridge/Providers/Sts2RuntimeReflectionReader.cs`、runtime/contracts、window extractors 与 fixture provider。
- Python 侧会影响 `src/sts2_agent/models.py`、`src/sts2_agent/bridge/`、fixtures、validation 与后续给 LLM 的 snapshot 摘要。
- 需要补充 live 验证，确认 draw/discard/exhaust 三个牌堆在真实战斗中能稳定导出，并与现有计数字段保持一致。
