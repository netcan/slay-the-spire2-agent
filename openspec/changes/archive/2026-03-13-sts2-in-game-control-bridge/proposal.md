## Why

当前仓库虽然已经能通过 `runtime provider` 反射读取 STS2 运行时对象，但这条链路仍停留在“代码已准备好、尚未真正运行在游戏进程内”的阶段。要让 agent 或大模型不仅能看局面，还能真正自动打牌，下一步必须把 bridge 作为真实游戏 mod 挂进 STS2 进程，并补上从外部动作请求到游戏内执行的闭环。

现在推进这件事的时机已经成熟：状态提取、协议字段、窗口抽象和 runtime 反射探测已经具备基础，实现真实接入可以把当前原型从“可联调”升级为“可控局”。

## What Changes

- 新增游戏内 mod 启动与生命周期管理能力，让 bridge 能随 STS2 进程启动、停止，并在游戏主线程安全地导出状态。
- 新增动作提交接口，使外部 agent 可以提交 `action_id` 或结构化动作参数，由 mod 在当前决策窗口内校验并执行。
- 为战斗、奖励、地图三个核心窗口实现首批真实动作执行映射，覆盖打牌、结束回合、选奖励、选地图节点等关键流程。
- 增加动作执行结果、拒绝原因、决策版本校验与只读保护，避免 stale action、非法动作或版本漂移导致错误执行。
- 补充游戏内安装方式、调试流程、失败回退与安全约束文档，确保 mod 出错时不破坏正常游玩。

## Capabilities

### New Capabilities
- `in-game-runtime-bridge`: 让 bridge 作为真实 mod 运行在 STS2 进程内，持续暴露 live state、legal actions 和健康状态。
- `action-apply-bridge`: 接收外部动作请求，校验决策上下文，并把合法动作映射为游戏内真实执行。

### Modified Capabilities

None.

## Impact

- 会影响 `mod/Sts2Mod.StateBridge/` 的初始化入口、运行线程模型、HTTP 接口与 runtime 反射封装。
- 可能新增游戏内 mod loader 适配、Harmony patch 或 STS2 生命周期钩子代码。
- 本地 loopback API 将从只读查询扩展为读写混合协议，需要增加动作请求/响应模型与失败语义。
- 会影响联调脚本、开发文档以及后续 Python `sts2-agent` 对真实 bridge 的接入方式。
- 为后续大模型自动打牌、回放、trace 对齐和长期稳定运行提供真正的执行闭环。
