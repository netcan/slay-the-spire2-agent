## Why

`card-description-rendering-glossary` 已经把分层字段、glossary 和 Python/LLM 链路打通，但在 2026-03-14 的真实 STS2 runtime 联调中，手牌 `description_rendered` 仍经常保留 `{Damage:diff()}`、`{Block:diff()}` 一类模板占位符，`description_vars.value` 也仍为 `null`。这意味着 agent 虽然拿到了更完整的 schema，却仍然拿不到当前回合真正可用的卡牌数值，自动出牌质量会继续受限。

现在继续推进这个修复是合适的，因为问题已经从“协议缺字段”收敛为“runtime 真实取值失败”，范围明确、可复盘 artifacts 已齐备，而且会直接影响后续大模型自动打牌的决策质量。

## What Changes

- 针对 live runtime 的手牌描述读取补齐“最终数值解析”链路，优先拿到真实战斗中的当前伤害、格挡、抽牌等数值。
- 为 `Sts2RuntimeReflectionReader` 增加更强的 runtime 反射探测与多路径回退，区分“拿到了模板文本”“拿到了渲染文本”“拿到了变量值”三种状态。
- 改进 `description_rendered` 的语义：当无法拿到真正渲染值时，不再把仍含模板占位符的文本误标成高质量 rendered。
- 改进 `description_vars` 的提取来源，优先对齐真实 live card instance 上的当前值，而不是只依赖通用字段猜测。
- 在 Python 摘要与联调 artifacts 中增加质量标记，帮助快速识别哪些卡牌已经拿到真实值、哪些仍在模板回退。

## Capabilities

### New Capabilities
- 无

### Modified Capabilities
- `mod-state-export`: 调整卡牌描述导出要求，真实 runtime 下需要尽量提供当前回合可用的数值化描述，而不是仅返回模板占位符。
- `game-bridge`: 调整 bridge 快照契约，对 `description_rendered` 与 `description_vars` 的质量语义增加约束，避免把模板文本伪装成已渲染文本。
- `llm-autoplay-runner`: 调整策略输入要求，让 runner 能识别描述质量，并优先消费真正完成数值解析的卡牌信息。
- `in-game-runtime-bridge`: 调整 live runtime bridge 的行为要求，明确手牌描述在真实战斗里应优先绑定当前卡牌实例的动态数值。

## Impact

- 主要影响 `mod/Sts2Mod.StateBridge/Providers/Sts2RuntimeReflectionReader.cs` 及相关 runtime extractor/helper。
- 需要补充 live validation 与诊断 artifacts，重点覆盖手牌 `description_rendered`、`description_vars` 和质量标记。
- Python 侧会小幅调整 `src/sts2_agent/bridge/http.py`、`src/sts2_agent/policy/llm.py` 与调试脚本，但不会引入新的外部依赖。
