## Context

当前 bridge 在战斗态已经能稳定导出手牌 `hand`，并额外给出 `draw_pile`、`discard_pile`、`exhaust_pile` 的计数，但这些字段只回答“有多少张”，不能回答“有哪些牌”。对自动打牌来说，这会直接影响当前回合价值判断：例如是否值得过牌、是否需要保留检索组件、弃牌堆是否已有关键 attack/skill、消耗堆是否已经失去 win condition 组件。

这个变更会跨 C# contracts、runtime 反射读取、fixture provider、Python models/bridge/policy 与验证脚本。它不只是加几个字段，还要明确 pile contents 与 hand 的对象语义是否复用、live runtime 拿不到完整 pile 时如何降级，以及如何避免过大 payload 破坏现有联调体验。

## Goals / Non-Goals

**Goals:**
- 在 combat `snapshot.player` 中导出抽牌堆、弃牌堆、消耗堆的结构化卡牌列表。
- 让 pile contents 与手牌复用同一基础卡牌 schema，避免客户端针对每个 pile 实现不同解析逻辑。
- 保持现有 `draw_pile`、`discard_pile`、`exhaust_pile` 计数字段不变，便于旧逻辑继续工作。
- 在 live runtime 读取失败时保持 fail-safe，并提供最小可诊断信息，而不是让整个 snapshot 失败。

**Non-Goals:**
- 本次不引入“未知顶牌概率”“未来抽牌顺序预测”或更高层的长期规划语义。
- 本次不处理 deck、master deck、奖励池、商店池等 run 外或战斗外牌区。
- 本次不要求对 pile contents 增加额外隐私/隐藏信息推断；只导出 runtime 当前可读到的内容。

## Decisions

### 1. pile contents 作为 player 下的并列列表字段导出

在 `player` 下新增 `draw_pile_cards`、`discard_pile_cards`、`exhaust_pile_cards` 三个列表字段，而不是把现有计数字段改成对象结构。这样可以保持旧客户端仍能继续使用 `draw_pile=12` 这类摘要字段，新客户端则可以直接读取更丰富的牌堆列表。

备选方案是把 `draw_pile` 从 `int` 改成 `{count, cards}`。这会扩大 breaking surface，也会让现有逻辑全部改写，因此不选。

### 2. pile cards 复用 hand card 基础 schema，但明确不是 legal hand

牌堆里的卡牌对象复用现有 `CardView`/`RuntimeCard` 的基础字段，例如 `card_id`、`name`、`canonical_card_id`、`description`、`cost`、`upgraded`、`traits`、`keywords`。但它们不应被误解为“当前可打出的手牌”：不要求 `playable=true` 有真实语义，也不会与 `play_card` legal actions 建立一一对应。

这能最大化复用现有 description、glossary 与文本解析链路，同时避免为 pile 专门再造一套 card schema。

### 3. live runtime 优先全量导出，失败时允许按 pile 独立降级

`Sts2RuntimeReflectionReader` 在读取 `DrawPile`、`DiscardPile`、`ExhaustPile` 时，优先从 runtime collection 直接提取 cards 列表；若某个 pile 暂时为空、节点缺失或个别卡解析失败，不应影响其他 piles 和 hand 的导出。降级策略是：
- pile collection 缺失：返回空列表，并在 metadata 中记录对应 pile 的 source / fallback；
- 单张卡解析失败：用最小 fallback card 对象保留索引与基础名字，避免数量对不上；
- 只有在整个 player combat state 不可读时，才回落到现有更粗粒度失败路径。

### 4. LLM 摘要默认传 pile contents，但保持可控规模

Python / policy 层把 pile contents 加入 snapshot 摘要，便于模型利用当前 draw/discard/exhaust 信息做决策。但为控制 prompt 大小，优先传基础字段和必要说明，不默认附带与手牌同等级的冗余 metadata。若后续 prompt 变大，再单独做截断或摘要策略。

## Risks / Trade-offs

- [live runtime 某些 pile 读取路径不稳定] → 每个 pile 独立降级，避免单点失败拖垮整个 combat snapshot。
- [payload 体积明显上升] → 先保持基础字段集，必要时在 policy 层做裁剪，而不是提前削弱 bridge 能力。
- [pile card 的 `card_id` 被误用成可执行动作参数] → 在 schema 和测试里明确 pile cards 只代表观察态卡实例，不绑定 legal actions。
- [同一张牌在 hand / discard / exhaust 之间缺少稳定关系] → 首版优先保证每个 pile 内部可读和实例可区分；跨 pile 生命周期追踪留待后续独立能力。
