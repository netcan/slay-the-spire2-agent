## 1. 游戏内 mod 挂载

- [x] 1.1 确认可用的 STS2 mod loader 入口，并补齐游戏内 bootstrap 初始化代码
- [x] 1.2 让 `Sts2Mod.StateBridge` 能随游戏进程启动、停止，并区分 `fixture`、`runtime-host`、`in-game-runtime` 模式
- [x] 1.3 在游戏内主线程或受控调度点接通 live state 导出，保证 `health`、`snapshot`、`actions` 可在真实 run 中工作

## 2. 动作提交协议与执行队列

- [x] 2.1 定义 `apply action` 请求/响应模型，包含 `decision_id`、动作标识、执行结果与拒绝原因
- [x] 2.2 在 loopback bridge 中新增动作提交 endpoint，并接入 `read_only`、stale decision、illegal action 校验
- [x] 2.3 实现受控动作执行队列，由游戏主线程消费并回写请求状态

## 3. 核心窗口真实动作映射

- [x] 3.1 为 `combat` 窗口接通 `play_card`、`end_turn` 的真实执行映射
- [x] 3.2 为 `reward` 窗口接通 `choose_reward`、`skip_reward` 的真实执行映射
- [x] 3.3 为 `map` 窗口接通 `choose_map_node` 的真实执行映射
- [x] 3.4 为未支持窗口和执行异常补齐明确的失败语义与诊断日志

## 4. 联调与验证

- [x] 4.1 增加真实游戏内模式的安装、启动、联调文档，说明与 host 模式的区别
- [x] 4.2 编写最小验证脚本或手工流程，检查 live `snapshot` / `actions` / `apply action` 闭环
- [x] 4.3 在 Python `sts2-agent` 侧记录真实 bridge 的接入约束与后续适配点
