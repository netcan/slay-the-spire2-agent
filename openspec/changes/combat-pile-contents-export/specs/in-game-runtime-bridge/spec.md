## ADDED Requirements

### Requirement: live runtime bridge 必须尽可能稳定读取 combat piles
当 `snapshot.phase="combat"` 时，bridge MUST 尽可能从 live runtime 读取当前玩家的抽牌堆、弃牌堆、消耗堆内容，并将其导出到统一快照。若某个 pile 暂时不可读、节点缺失或只部分可解析，bridge MUST 对该 pile 独立降级，而不是让整个 combat snapshot 失败。

#### Scenario: live runtime 成功读取三类 pile
- **WHEN** 当前玩家的 `DrawPile`、`DiscardPile`、`ExhaustPile` 在 runtime 中都可访问
- **THEN** `snapshot.player` MUST 导出三类 pile 的结构化卡牌列表
- **THEN** 这些 pile contents MUST 与同一帧里的 pile 计数字段保持一致或等效一致

#### Scenario: 单个 pile 读取失败时保持 fail-safe
- **WHEN** 某一个 pile 的 runtime collection 暂时不可读或其中个别卡牌无法完整解析
- **THEN** bridge MUST 仍然返回可序列化的 combat snapshot
- **THEN** 失败的 pile MUST 独立降级为可接受的空列表或最小 fallback 列表
- **THEN** metadata MUST 提供该 pile 的 source、fallback 或等效 diagnostics
