## Why

当前真实游戏里已经出现“玩家肉眼看到 reward 界面，但 bridge 仍导出 `phase = combat`、`window_kind = player_turn`、`legal_actions = [end_turn]`”的误判。这会让上层 autoplay runner 把战斗结束后的奖励窗口继续当作战斗尾帧处理，既影响状态可信度，也阻塞后续 reward 自动决策接入。

## What Changes

- 修正 in-game runtime 对 reward 窗口的 phase 识别逻辑，不再在 reward 已显示时错误回落到 `combat`。
- 为 reward 检测补充更稳健的兜底信号，覆盖 `_connectedRewardsScreen` 不稳定、`IsComplete` 提前变化、敌人已清空但窗口仍切换中的边界场景。
- 调整 reward 窗口的 `snapshot` / `actions` 导出，确保进入奖励界面后返回 `phase = reward`、可读 reward 列表以及 `choose_reward` / `skip_reward` 等合法动作。
- 增加诊断元数据与回归测试，能区分“真实 reward”“combat 尾帧”“识别失败降级”三类状态。

## Capabilities

### New Capabilities
- 无

### Modified Capabilities
- `in-game-runtime-bridge`: 修改 live runtime 对 reward 窗口、战斗结束过渡态与 phase 导出的判定要求，确保 reward 不再被误识别为 combat。

## Impact

- 受影响代码主要在 `mod/Sts2Mod.StateBridge/Providers/Sts2RuntimeReflectionReader.cs` 及相关 runtime 提取/测试代码。
- 受影响接口为 live `health`、`snapshot`、`actions` 的 phase 与 reward 相关导出行为。
- 会直接影响 `src/sts2_agent/orchestrator.py`、`tools/run_llm_autoplay.py` 等上层调用方对 battle 完成与 reward 进入的判断质量。
