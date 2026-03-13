## Context

当前 in-game runtime bridge 已经可以在真实 STS2 运行时输出 `health`、`snapshot` 和 `actions`，但字段质量还不稳定。现在的 `ConvertToText` 主要依赖 `ToString()` 与少量反射字段，导致 relics、potions、rewards 等场景仍可能输出类名、空值或无法区分的占位文本。

这类问题对人工调试和 Agent 消费都不友好：人无法快速判断状态是否准确，模型也无法稳定使用这些文本字段进行推理或 trace 对比。因此需要在不破坏现有 bridge 协议的前提下，为 runtime 导出建立更稳定的文本解析链路和 diagnostics 约束。

## Goals / Non-Goals

**Goals:**
- 为 relics、potions、rewards、map nodes、action labels 等文字字段提供一致的文本解析策略。
- 优先导出面向玩家的本地化文本，其次才是稳定的开发者可读 fallback。
- 在文本无法解析时保留可诊断 metadata，避免无声退化。
- 维持现有 `/snapshot`、`/actions` 主结构不变，优先做字段质量提升。

**Non-Goals:**
- 不在本变更中重设 contracts 的整体数据模型。
- 不在本变更中新增或扩展 `apply` 的动作执行范围。
- 不承诺一次性解决所有 UI 节点或所有本地化类型的反射兼容问题。

## Decisions

1. 建立集中的文本解析链路
   - 在 `Sts2RuntimeReflectionReader` 内部把现有 `ConvertToText` 扩展为多级 fallback pipeline：
     1) 直接 `string`
     2) 针对 `LocString` 或类似类型的本地化文本解包
     3) 常见字段（如 `Name`、`Title`、`Description`、`Label`、`Text`）
     4) 常见嵌套对象（如 `Relic`、`Potion`、`Reward`）
     5) 可接受的 `ToString()` fallback
   - 原因：将文本解析逻辑集中后，可以在各窗口和各类状态间保持一致。
   - 备选方案：在每个 `Describe*` 方法里各自定制反射。未采用，因为会很快出现重复 fallback 和行为不一致。

2. 区分“对外文本”与“诊断信息”
   - `/snapshot` 和 `/actions` 的主字段仍优先输出简洁可读文本，不在 `name`、`label`、`relics` 等主字段内混入大量调试结构。
   - 诊断信息放到 metadata 中，例如 `text_resolution`、`unresolved_fields`、`fallback_source` 等辅助键。
   - 原因：Agent 消费层需要稳定文本，调试层需要可视化诊断，两者应该分离。
   - 备选方案：把原始反射值直接拼进主字段。未采用，因为会降低协议稳定性。

3. 优先保持后向兼容
   - 不改变 `PlayerState.Relics`、`RuntimeActionDefinition.Label` 等已有字段的结构和类型，而是通过更好的值填充与 metadata 补充完成升级。
   - 原因：当前仓库已有 fixture、tests 和 agent 侧契约，小步前进比大量结构更改更稳。

4. 为核心决策元素补充稳定标识
   - 除了可读文本，对 reward 和 map node 保持可用于行为对齐的稳定参数，例如 `reward_index`、`node`、`coord`。
   - 原因：单纯依赖文本输出仍可能受本地化或 UI 变更影响。

## Risks / Trade-offs

- [反射链路过于激进导致误拿字段] → 将 fallback 顺序固定，并对核心对象类型加入目标化单测试和 runtime 日志验证。
- [增加 metadata 后 payload 变大] → 只暴露少量诊断键，优先记录失败分类而不是整个原对象。
- [不同版本 STS2 的本地化对象结构可能不同] → 将 `LocString` 解析设计成“多通道探测 + 安全 fallback”，并在失败时返回 diagnostics。
- [改善文本后 fingerprint 频繁变化] → 对 state fingerprint 仅使用面向决策的稳定文本与结构化值，不把瞬时 diagnostics 直接混入主 fingerprint。

## Migration Plan

- 先在 runtime 读取器内完成文本解析链路改造。
- 再补充 fixture 和单测试，确保 relics、rewards、map nodes 等输出稳定。
- 最后在真实游戏运行时重新验证 `/snapshot` 和 `/actions`。
- 本变更不涉及外部接口路径变更，无单独回滚步骤；若解析策略导致退化，可回退到旧的 fallback 链路。

## Open Questions

- `LocString` 在不同实体（relic、reward、intent 等）上是否存在需要分类处理的特殊属性。
- 是否需要在 contracts 中正式添加 `display_name` / `raw_name` 双轨字段，还是先通过 metadata 承载诊断信息即可。
