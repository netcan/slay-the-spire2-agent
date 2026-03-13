## Why

当前 runtime bridge 在真实战斗中已经能够导出 `snapshot` 与 `actions`，但重复手牌场景下仍可能把两张同名牌映射成同一个 `card_id` 或 `action_id`。这会让外部 agent 无法稳定地区分“左边那张打击”和“右边那张打击”，进一步导致真实 `apply play_card` 存在误操作风险。

## What Changes

- 调整 runtime 手牌身份生成策略，为每张 live 手牌生成稳定且可区分的实例级 `card_id`。
- 让 `play_card` legal action 的 `params.card_id` 与 `snapshot.player.hand[].card_id` 一一对应，并让 `action_id` 体现实例差异。
- 更新 `apply play_card` 的执行定位逻辑，基于实例级 `card_id` 而不是仅按名称或粗粒度描述匹配。
- 补充测试与真实游戏验证，覆盖重复 `打击`、`防御` 等高频重复手牌场景。

## Capabilities

### New Capabilities
- None.

### Modified Capabilities
- `in-game-runtime-bridge`: 要求 live 手牌导出稳定且可区分的实例级 `card_id`，并与 `play_card` actions 对齐。
- `action-apply-bridge`: 要求 `apply play_card` 使用实例级 `card_id` 精确定位具体手牌。

## Impact

- 主要影响 `mod/Sts2Mod.StateBridge/Providers/` 中的 runtime 读牌与身份生成逻辑。
- 会调整 `mod/Sts2Mod.StateBridge/Extraction/WindowExtractors.cs` 中 `play_card` action 的生成方式。
- 需要补充对应单元测试与真实游戏联调记录，确保重复手牌不再冲突。
