## Why

当前仓库已经验证了“读取 live state -> 调本地大模型 -> 选择合法动作 -> bridge 执行一手牌”的最小闭环，但这还不足以支撑真实对局中的自动打牌体验。要让 agent 真正接近“能自己打一回合”，runner 需要在同一个玩家回合内连续做多次决策，而不是只打一手就退出。

## What Changes

- 把现有 `llm-autoplay-runner` 从“单步/固定步数循环”扩展为“以回合推进为边界的多步 autoplay”。
- 为连续决策增加回合级停止条件，包括玩家回合结束、phase 切换、terminal、模型 halt、bridge 拒绝或连续失败。
- 为多步模式增加更清晰的 trace 与运行摘要，区分“本回合执行了几手牌”“为什么停下”“是否成功结束玩家回合”。
- 提供面向真实战斗的多步调试入口，支持限制单回合最大动作数，避免失控连续出牌。

## Capabilities

### New Capabilities

### Modified Capabilities
- `llm-autoplay-runner`: 将现有单步 autoplay 约束扩展为支持同一玩家回合内的连续决策、回合级停止条件与多步 trace/summary。

## Impact

- Python 侧会修改 `AutoplayOrchestrator`、`live_autoplay` 与 `tools/run_llm_autoplay.py` 的执行语义。
- trace 和 `RunSummary` 需要补充回合级统计字段，便于复盘多步执行结果。
- 真实联调脚本和文档会增加“完整玩家回合 autoplay” 的使用方式与验证记录。
