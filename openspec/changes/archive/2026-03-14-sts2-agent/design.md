## 背景

当前仓库还处于 OpenSpec 初始化阶段，尚未落地实现代码，但目标系统天然分成两个运行环境：一侧是能够读取和驱动 Slay the Spire 2 内部状态的游戏 mod，另一侧是负责做决策的外部 agent 进程。现阶段最重要的不是直接追求训练平台或复杂策略，而是先定义一套稳定、可调试、与游戏内部实现解耦的 agent 接入层。

这套设计需要同时兼容多种策略实现，包括规则策略和 LLM 驱动策略，但不能过早绑定某一家模型提供方、某一种提示词格式，或者某一种 transport 实现。第一阶段应优先保证：状态可读、动作可校验、执行可追踪、失败可中断。

## Goals / Non-Goals

**Goals:**
- 定义本地 `game-bridge` 接口，覆盖结构化观察、合法动作枚举、动作提交和会话生命周期控制。
- 用版本化 JSON 契约解耦游戏侧 mod 与外部 agent 运行时。
- 建立可重复执行的 autoplay 循环，并记录足够的 trace 信息用于调试和评估。
- 在 bridge 或 policy 失步、超时、报错时安全停止，避免游戏进入不可控状态。

**Non-Goals:**
- 本次变更不构建高吞吐强化学习训练平台或并行模拟农场。
- 本次变更不采用截图识别或操作系统级鼠标键盘脚本控制游戏。
- 本次变更不尝试一次性解决完整的长程组牌策略问题。
- 本次变更不绑定具体 LLM 服务商、部署方式或提示词模板。

## Decisions

### 1. 系统拆分为 `game-bridge` 与 `autoplay-orchestrator`
`game-bridge` 负责状态提取、合法性校验和有副作用的动作执行；`autoplay-orchestrator` 负责策略调用、执行循环控制和 trace 持久化。

这样可以让 mod 保持贴近游戏环境、职责单一，同时让 agent 运行时在游戏进程之外快速迭代，更方便接入 Python、TypeScript 或其他实验工具链。

备选方案：
- 把策略逻辑全部嵌入 mod：放弃，因为模型接入、可观测性和迭代效率都较差。
- 完全用 UI 自动化驱动游戏：放弃，因为脆弱且无法获得精确结构化状态。

### 2. 首版采用本地 loopback JSON 协议
第一版 bridge 通过本地回环接口暴露 JSON 请求/响应协议。JSON 便于 C#、Python、TypeScript 之间联调，也方便手工调试和落盘 trace。只绑定本地 loopback 可以在简化实现的同时控制安全边界。

备选方案：
- Named pipes：后续可考虑，但首版不如 JSON 接口直观，跨语言调试成本也更高。
- 纯 WebSocket 流式接口：首版放弃，因为当前控制流更适合显式请求/响应。

### 3. 以“决策窗口”为中心规范 observation
每次 bridge 响应都必须显式标识当前决策窗口，至少包含 `session_id`、`state_version`、`phase`、`decision_id` 等稳定元数据。观察内容只输出策略决策真正需要的结构化字段，不直接暴露原始引擎对象。

这样可以避免策略层依赖易变的游戏内部对象结构，也便于识别过期动作和状态漂移。

备选方案：
- 直接导出原始内部对象：放弃，因为会把 agent 紧耦合到脆弱的 mod 内部实现。
- 截图作为主要观察：放弃，因为合法动作、隐藏计数和精确状态很难稳定恢复。

### 4. 动作采用 typed legal commands 表示
bridge 应针对当前决策窗口枚举完整合法动作集合，并为每个动作提供稳定的 `action_id`。动作本身应是类型化命令，例如 `play_card`、`end_turn`、`choose_reward`、`choose_map_node`、`use_potion`、`skip`。

`autoplay-orchestrator` 只能提交当前合法动作集合中出现过的动作；bridge 必须拒绝格式错误、非法或过期动作，而且不能修改状态。

备选方案：
- 自由文本命令：放弃，因为解析歧义太大，无法提供可靠安全边界。
- 原始屏幕坐标点击：放弃，因为过于依赖 UI，且难以做 legality 校验。

### 5. 每次决策都持久化 trace
`autoplay-orchestrator` 每尝试一次决策，都应写入一条 trace 记录。trace 至少包含观察元数据、合法动作集合、策略输出、最终选择动作、bridge 返回结果和时间戳。

这样从第一阶段开始就具备回放、调试、离线评估和错误追踪能力。

备选方案：
- 只记录最终胜负结果：放弃，因为无法定位策略错误或 bridge 失步问题。

### 6. 安全策略采用 fail closed
如果 bridge 拒绝动作、读取状态与提交动作之间决策窗口发生变化，或 policy 连续超时 / 返回无效结果，autoplay 必须立即停止，并把会话标记为 interrupted。

这会牺牲一部分“强行继续跑”的自治性，但能换来更强的可控性和操作者信任。

## Risks / Trade-offs

- [游戏更新导致 bridge 内部实现漂移] -> 保持外部契约小而稳定，并通过版本化隔离 mod 侧适配变化。
- [LLM 延迟过高导致流程卡顿] -> 增加可配置超时，并提供非 LLM 的基线策略用于联调和回归测试。
- [observation schema 过快膨胀] -> 先覆盖战斗和常见非战斗决策窗口，只在规格真正需要时扩展字段。
- [agent 与 bridge 的合法动作理解不一致] -> 让 bridge 成为唯一的 legality 真值源，并提供清晰错误码。
- [本地 API 被其他进程误用] -> 首版只监听 loopback，后续加入临时 session token 机制。

## Migration Plan

1. 在本仓库中先定义协议模型、bridge 接口和 trace 数据结构。
2. 在接入真实 STS2 mod 之前，先实现 mock bridge 和基于夹具的测试。
3. 在游戏侧实现 loopback bridge adapter，并按契约联调。
4. 先用战斗与非战斗 fixture 跑通 orchestrator，再连接到真实本地游戏会话。
5. 在基础链路稳定后，再补充 undo / restart 等增强能力。

回滚策略：关闭 autoplay 入口，回退到人工游玩；由于 bridge 仅为本地能力，不主动写入持久化数据时不会影响正常存档。

## Open Questions

- 第一阶段非战斗决策窗口要覆盖到什么程度：只做地图，还是同时支持奖励、商店、事件？
- 首版 bridge API 应该提供统一 `step` 接口，还是拆分为 `state`、`actions`、`act` 三类接口？
- trace 文件第一阶段是否需要轮转、压缩或归档策略？
