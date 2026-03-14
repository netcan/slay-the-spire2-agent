## Why

当前 bridge 已经能够在真实 STS2 战斗中稳定导出 live `snapshot` 与 `actions`，但真实 `POST /apply` 仍会在 `in-game-runtime` 模式下卡在 `action_timeout`。这意味着 agent 已经具备“看牌”和“选牌”能力，却还不能稳定完成真正的自动出牌闭环，因此必须优先修复游戏线程动作消费链路。

## What Changes

- 修复 `in-game-runtime` 模式下外部动作请求入队后未被游戏线程及时消费的问题，确保 `play_card`、`end_turn` 等首批动作能在真实对局中完成闭环。
- 为 in-game 动作队列增加更细的受控执行诊断，明确区分“未入队”“未被 tick 消费”“执行阶段异常”“状态未推进”等不同失败路径。
- 补充真实联调与回归验证，覆盖至少一个 `play_card` 或 `end_turn` 在战斗窗口中被成功执行并推进状态的场景。

## Capabilities

### New Capabilities
- None.

### Modified Capabilities
- `action-apply-bridge`: 强化 in-game 动作队列消费、超时诊断与真实执行成功判定，确保 live `POST /apply` 不再停留在无信息的 `action_timeout`。

## Impact

- 主要影响 `mod/Sts2Mod.StateBridge/Providers/InGameRuntimeCoordinator.cs`、`mod/Sts2Mod.StateBridge/InGame/Sts2InGameModEntryPoint.cs` 以及动作执行相关反射桥接逻辑。
- 可能补充 bridge logger、动作响应 metadata 与真实联调脚本，以便记录队列生命周期与执行阶段诊断。
- 会更新 OpenSpec 规范与验证文档，形成一条真正可复现的 live 写入回归路径。
