## 1. 队列消费链路诊断

- [x] 1.1 梳理 `InGameRuntimeCoordinator`、`Sts2InGameModEntryPoint` 与相关 patch 的动作入队、消费、执行时序。
- [x] 1.2 为 pending action 增加阶段状态与关键时间点记录，并把最小诊断写入 `ActionResponse.metadata` 或日志。
- [x] 1.3 确认 `NGame._Process` 或等效 tick patch 能稳定触发队列消费，并修复“请求入队后未被游戏线程消费”的路径。

## 2. 动作执行可靠性修复

- [x] 2.1 修复真实 `play_card` / `end_turn` 在 `in-game-runtime` 模式下的 `action_timeout` 问题。
- [x] 2.2 在执行阶段异常、队列超时、状态未推进等场景下返回更细的失败原因，而不是单一笼统超时。
- [x] 2.3 为关键 coordinator 或 apply 路径补充测试，覆盖队列消费和失败诊断逻辑。

## 3. 真实联调回归

- [x] 3.1 更新 live 调试文档或验证记录，说明新的队列阶段诊断和典型排障方式。
- [x] 3.2 在真实 STS2 战斗窗口完成至少一次成功的 `POST /apply` 冒烟验证，并记录前后状态推进结果。
