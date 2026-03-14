## Why

当前实测中，当 agent 在奖励列表（`NRewardsScreen`）选择了“将一张牌添加到你的牌组。”之后，游戏会进入选牌二级界面（卡牌网格选择）。但 bridge 无法识别该界面，仍降级导出为 `phase=combat` 的 `combat_transition` 且 `actions=[]`，导致 reward 流程无法走完，agent 也无法继续决策。

## What Changes

- 扩展 in-game runtime bridge 的窗口识别：在奖励链路进入“选牌二级界面”时，仍导出为 `phase=reward` 的可执行窗口，而不是降级为 `combat_transition`。
- 在该窗口导出可选卡牌列表到 `snapshot.rewards`（保持协议兼容），并生成可执行的 `choose_reward` / `skip_reward` legal actions，使现有 agent runner 可以直接复用 reward 决策逻辑完成选择或跳过。
- 补充 diagnostics，标记 reward 子类型（例如 `reward_choice` vs `card_reward_selection`）与判定来源，便于联调与回归。

## Capabilities

### New Capabilities

- 无

### Modified Capabilities

- `in-game-runtime-bridge`: 追加对“选牌二级界面”的识别与导出要求，保证 reward 链路可连续决策。
- `action-apply-bridge`: 扩展 `choose_reward` / `skip_reward` 的执行映射范围，覆盖卡牌奖励选择界面。

## Impact

- 主要影响 C# mod：`mod/Sts2Mod.StateBridge/Providers/Sts2RuntimeReflectionReader.cs` 的 phase/window 识别与 reward 提取、动作执行映射。
- 可能需要调整 reward 相关的 extractor/metadata（window_kind），并补充 mod 侧测试与 live 冒烟 artifacts。
- Python 侧 runner 原则上无需改协议，只要继续看到 `phase=reward` 且存在 `choose_reward|skip_reward` 即可继续自动决策。

