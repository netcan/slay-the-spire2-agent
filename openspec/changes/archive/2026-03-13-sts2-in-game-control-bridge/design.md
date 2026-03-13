## Context

当前仓库已经具备三块基础能力：
1. Python 侧 `sts2-agent` 原型，可消费 `DecisionSnapshot` / `LegalAction` 协议；
2. C# 侧 `Sts2Mod.StateBridge`，可在宿主 host 中暴露 `health` / `snapshot` / `actions`；
3. `runtime provider` 反射读取器，已经能从 STS2 程序集对象中推导 phase、玩家状态、敌人、奖励和地图节点。

但真正缺失的部分有两项：
- bridge 还没有作为真实 mod 运行在 STS2 进程内，当前 host 只能验证代码路径，不能跨进程读取游戏 live state；
- 外部 agent 还无法提交动作给游戏，缺少 `apply` 类接口、决策版本校验与实际动作分发链路。

这个变更需要把“状态读取”推进为“游戏内运行 + 动作执行”的完整闭环，同时保持失败可回退、默认安全、协议可诊断。由于 STS2 仍可能更新内部对象结构，设计必须把稳定对外契约与易变内部反射/patch 隔离开。

## Goals / Non-Goals

**Goals:**
- 让 `Sts2Mod.StateBridge` 能作为真实 STS2 mod 随游戏启动，并在游戏进程内提供 loopback bridge 服务。
- 让 bridge 在游戏主线程或受控调度点安全导出 live state，并向外保持现有 `health` / `snapshot` / `actions` 协议兼容。
- 新增动作提交接口，支持外部 agent 基于 `decision_id` / `action_id` 发起执行请求。
- 在首批核心窗口中完成真实动作映射：战斗出牌/结束回合、奖励选择/跳过、地图节点选择。
- 增加只读保护、决策版本校验、执行结果回执与错误语义，避免错误输入破坏运行中的对局。

**Non-Goals:**
- 本次不追求覆盖所有 STS2 交互分支，例如商店购买、事件分支、篝火、锻造、遗物界面等。
- 本次不实现远程网络接入、鉴权或多客户端控制，bridge 仍限定在本机 loopback 使用。
- 本次不实现完全无反射的稳定 SDK 适配层；若官方 mod API 缺失，允许继续使用反射与 Harmony 辅助。
- 本次不解决大模型策略本身，只解决“可读局 + 可控局”的桥接基础设施。

## Decisions

### 1. 采用“游戏内 bootstrap + 现有 provider/server 复用”而不是重写第二套 mod 服务
现有 `ModBootstrap`、`LocalBridgeServer`、`BridgeSessionState`、window extractor 与协议模型已经可工作，新的游戏内 mod 入口应尽量复用这些组件，只替换宿主启动方式与 runtime 调度路径。

这样可以减少协议分叉风险，保证 `fixture -> runtime host -> in-game mod` 三种运行形态尽量共用同一套导出逻辑。

备选方案：
- 另起一套专门给游戏内 mod 用的服务层：放弃，因为会快速造成双实现偏移。
- 完全移除 host：放弃，因为离线联调与 CI 验证仍需要 host 形态。

### 2. 动作执行采用“请求入队 + 游戏主线程消费”模型
外部 HTTP 请求线程不能直接对游戏对象做有副作用操作。`apply action` 请求应先解析并校验，然后写入受控队列；真正的动作执行由游戏主线程在 `_Process` 或明确的游戏回调里消费。

这样可以避免跨线程触碰 Godot / STS2 对象导致崩溃，也便于统一做 stale action 拦截与执行结果汇报。

备选方案：
- 在 HTTP 线程直接反射调用游戏对象：放弃，因为线程安全风险过高。
- 把所有请求都改为轮询文件或 pipe：放弃，因为会增加状态同步复杂度，首版收益不高。

### 3. `apply action` 协议必须携带决策上下文并支持幂等拒绝
动作请求至少应包含 `decision_id`，优先允许 `action_id` 直接命中当前 legal action，也允许结构化参数回退。bridge 必须在执行前校验：
- 当前 `decision_id` 是否匹配；
- `read_only` 是否关闭；
- 动作是否仍存在于当前 legal actions；
- 当前 phase 是否支持该动作。

若校验失败，返回明确拒绝结果而不是静默忽略。这样后续 agent 能区分“动作已过期”“不合法”“运行时异常”等不同失败类型。

备选方案：
- 只靠 `action_id` 不带 `decision_id`：放弃，因为跨状态版本时容易误执行。
- 只接受结构化参数不返回拒绝语义：放弃，因为调试与 agent 恢复成本过高。

### 4. 先覆盖核心窗口的真实执行映射，其他窗口继续保持只读
首版动作执行只保证以下窗口：
- `combat`：`play_card`、`end_turn`、部分 `use_potion`
- `reward`：`choose_reward`、`skip_reward`
- `map`：`choose_map_node`

这些窗口已经具备较稳定的状态提取结果，且直接决定自动打牌是否能闭环。其他窗口短期内继续通过 `health` / metadata 明确标记为未支持，而不是冒险提供不稳定执行。

备选方案：
- 一次性覆盖所有游戏 UI：放弃，因为风险高且调试面过大。
- 只做战斗动作不做奖励/地图：放弃，因为自动推进 run 仍会卡住。

### 5. 运行模式保持显式：`fixture`、`runtime-host`、`in-game-runtime`
当前仓库已有 `fixture` 与 `runtime` 两类 provider 语义。为了降低调试复杂度，建议在 health / metadata 中显式区分：
- `fixture`：完全假数据；
- `runtime-host`：宿主中装配了 runtime provider，但不在游戏进程内；
- `in-game-runtime`：真实 mod 已挂进 STS2 进程，可读取与执行 live action。

这样 Python 侧和联调脚本可以快速知道当前是否真的具备动作执行能力。

备选方案：
- 继续只保留 `runtime` 一个状态：放弃，因为无法快速区分“可反射但不可读局”和“真实已接入”。

## Risks / Trade-offs

- [游戏主线程调度点选错，导致执行时机不稳定] -> 把执行入口限制在固定 tick/回调，并对每次执行记录 phase 与决策版本。
- [STS2 更新后内部方法名或节点结构变化] -> 把反射与动作映射集中在独立适配层，并通过 `health` 暴露兼容诊断信息。
- [HTTP 请求积压或重复提交导致重复操作] -> 使用单消费者队列、请求状态机和幂等拒绝语义，避免同一 `decision_id` 下重复执行。
- [动作映射不完整导致误判 legal action] -> 先以当前导出的 legal actions 为真值源，只允许执行枚举结果中的动作。
- [mod 运行异常影响正常游玩] -> 默认保留 `read_only` 开关、执行异常捕获与可关闭 bridge 的 fail-safe 路径。

## Migration Plan

1. 增加真实游戏内 mod 入口与生命周期挂载点，确保 bridge 可随 STS2 进程启动。
2. 抽象动作执行队列和结果模型，在不打开写权限时先完成只读挂载验证。
3. 逐步接通 `combat`、`reward`、`map` 三类窗口的真实执行映射，并增加拒绝语义。
4. 更新联调脚本与文档，区分 host 模式与真实游戏内模式。
5. 让 Python 侧 bridge adapter 可选调用 `apply action`，完成首个端到端闭环验证。

回滚策略：
- 保留 `fixture` / host 模式，真实游戏内入口若不稳定，可在构建或启动参数层面关闭；
- `apply action` 默认维持只读关闭，出现问题时可退回只读观察模式，不影响人工游玩。

## Open Questions

- STS2 当前稳定可用的 mod loader 初始化点具体是哪一套，是否需要在仓库中额外引入模板或 loader 适配层？
- 战斗动作是否优先走现有 UI 控件模拟点击，还是直接调用底层运行时方法？两者的稳定性需要实际对比。
- `apply action` 是否要在首版就返回同步执行结果，还是先返回 `accepted` + 异步最终状态更稳妥？
- 是否要在首版加入简单的本地 session token，防止同机其他进程误调用写接口？
