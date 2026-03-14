## Context

当前 `in-game-runtime` 模式已经能在真实 STS2 对局中稳定导出 `snapshot`、`actions` 和 `health`，说明 runtime 附着、窗口识别与状态导出链路基本成立。但真实 `POST /apply` 在 live 战斗中仍然返回 `action_timeout`，并且同一局面下 `play_card` 与 `end_turn` 都会超时，说明问题不在单个动作映射，而在“HTTP 请求 -> pending queue -> 游戏线程消费 -> 真实执行”这条受控执行链路。

现有 `InGameRuntimeCoordinator.ApplyAction()` 会把请求入队后同步等待 2 秒，但只要 `Tick()` 没有及时消费队列，或者消费阶段没有把执行进度回写给等待方，外部就只能看到一个笼统的 `action_timeout`。这会阻塞后续 agent 接入，因为调用方无法区分是 patch 没触发、队列没消费、执行反射失败，还是状态推进太慢。

## Goals / Non-Goals

**Goals:**
- 修复 in-game 动作队列在真实游戏线程中的消费链路，让 `play_card`、`end_turn` 至少能完成一条真实闭环。
- 为动作执行过程增加阶段化诊断，明确标记 `enqueued`、`dequeued`、`executing`、`completed`、`failed`、`timed_out` 等关键节点。
- 让超时结果携带足够上下文，便于区分“队列未消费”和“执行阶段异常”。
- 保持现有 `POST /apply` 协议形状基本不变，优先通过 metadata、日志和内部状态修复问题，而不是大改对外接口。

**Non-Goals:**
- 不在本次变更中新增新的窗口类型或复杂动作类别。
- 不实现多步自动策略循环；本次只聚焦单步动作从请求到执行完成的可靠性。
- 不把所有内部调试信息都暴露给外部主字段，避免污染 agent 面向使用的核心协议。

## Decisions

1. 将动作处理拆成“入队成功”和“游戏线程已消费”两个明确阶段
   - 当前最大问题是外部只能看到最终超时，看不到卡在哪一层。
   - 修复方案应在 `PendingAction` 上记录阶段状态与时间戳，并在 `Tick()` 消费时立刻更新为 `dequeued`。
   - 这样即使最终超时，也能判断是 patch/tick 没跑到，还是跑到了但执行逻辑没完成。

2. 优先修复游戏线程消费链路，而不是单纯拉长超时时间
   - 仅扩大 `Wait(TimeSpan.FromSeconds(2))` 会掩盖问题，无法证明 `NGame._Process` patch 和 `ProcessPendingActions()` 真正可靠。
   - 更合理的做法是为 `OnGameTick()`、队列出入队和执行入口加受控日志，并在真实对局中验证动作至少被消费一次。
   - 如果确认只是动画或主线程节奏导致的短暂慢响应，再基于观测数据微调 timeout。

3. 失败诊断通过 `ActionResponse.metadata` 与 bridge logger 双通道输出
   - `ActionResponse.metadata` 记录可供脚本和 agent 消费的结构化字段，例如 `queue_stage`、`tick_seen`、`execution_started_at`。
   - bridge logger 记录更细的内部路径，便于本地调试 patch 是否触发、具体哪个执行分支抛错。
   - 这样既能保留外部可测试契约，又不要求 agent 解析完整日志。

4. 真实验证同时覆盖“动作成功”与“状态推进”
   - 本次修复不能只看 `POST /apply` 返回 `accepted`，还必须再次读取 live `snapshot` / `actions` 确认 `decision_id`、`state_version` 或动作集合发生推进。
   - 这与现有 `validate_live_apply.py` 的双条件判断保持一致，可直接复用该脚本做回归。

## Risks / Trade-offs

- [增加日志和 metadata 可能带来额外噪声] -> 仅输出关键阶段字段，详细日志保持在 debug 级别或本地文件中。
- [问题根因可能在 STS2/Godot 主循环时机而非队列实现] -> 先通过阶段化诊断确认卡点，再决定是否调整 patch 点或执行时机。
- [修复后不同动作类型行为不一致] -> 先用 `end_turn` 和无目标 `play_card` 做最小闭环验证，再扩展到需要目标的动作。
- [过度依赖真实环境联调导致回归成本高] -> 保留脚本化 live 验证路径，并尽可能补充 coordinator 级单元测试或集成测试。
