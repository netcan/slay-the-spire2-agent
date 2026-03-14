## Why

当前 live autoplay 已经能从主菜单进局、打完整个玩家回合，并且 reward 入口也能单步触发，但自动链路仍常在 `reward -> map -> 下一房间/下一场战斗` 这一段断开：runner 不知道何时该继续处理奖励、何时该选图、何时该等待过渡结束并接回新的 `combat` 决策窗口。只要这段没有打通，就还不能稳定完成“连续跑多场战斗/完整 run 片段”的真实自动化验证。

现在推进这段最合适：reward 与 map 的 live 状态、`choose_reward`、`skip_reward`、`choose_map_node` 已经具备基础能力，且刚完成了菜单开局链路，正适合把 runner 抬升到“跨 reward / map / 房间过渡，直到下一场战斗或安全停止”为止的连续执行语义。

## What Changes

- 扩展 `llm-autoplay-runner`，使其在 battle autoplay 中不再把 `reward` 或 `map` 视为终点，而是能够继续执行奖励决策、地图路径选择，并在房间过渡后重新接入下一场战斗。
- 为 runner 增加跨 phase 自动化状态机：覆盖 `reward_choice`、`card_reward_selection`、`map`、房间加载/过渡等待，以及重新进入 `combat` 后的恢复逻辑。
- 引入 reward/map 级安全策略与停止条件，例如奖励自动选择模式、地图选路策略、最大非战斗步数、过渡超时与未知窗口熔断。
- 补充 trace、summary、调试脚本与 live 验证脚本，能明确记录本次自动化是在 reward 停止、map 停止、进入下一战成功，还是因异常窗口/超时而中断。
- 视需要补强 runtime bridge 的 phase/metadata 诊断，确保 runner 能稳定识别“奖励完成但尚未进入 map”“地图已选路但尚未进入下一房间”“已进入下一场战斗”这几类状态。

## Capabilities

### New Capabilities
- 无

### Modified Capabilities
- `llm-autoplay-runner`: 从“支持单场战斗内回合/奖励决策”扩展为“支持 reward -> map -> 下一房间/下一场战斗 的连续自动化闭环”。
- `in-game-runtime-bridge`: 补充 reward 完成、map 选路、房间过渡与再次进入 combat 时的稳定导出与诊断要求，便于上层 runner 做状态机推进。

## Impact

- 影响 Python 侧 `src/sts2_agent/` 的 orchestrator、policy、trace、CLI 与 live autoplay 调试入口。
- 可能影响 `mod/Sts2Mod.StateBridge/Providers/Sts2RuntimeReflectionReader.cs` 的 phase/window diagnostics 与 map/reward 相关导出质量。
- 会新增或更新测试、live 验证脚本与文档，重点覆盖“reward -> map -> next combat”真实链路。
