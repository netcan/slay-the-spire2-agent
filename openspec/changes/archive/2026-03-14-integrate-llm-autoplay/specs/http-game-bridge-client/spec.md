## ADDED Requirements

### Requirement: Python 侧必须提供 `HttpGameBridge` 封装本地 bridge 接口
系统 MUST 提供一个 `http-game-bridge-client`，把现有本地 HTTP bridge 封装为 `GameBridge` 实现，并至少覆盖 `/health`、`/snapshot`、`/actions`、`/apply` 四个接口。该实现 MUST 可被 `AutoplayOrchestrator` 直接调用，而不需要脚本层手写 HTTP 请求。

#### Scenario: 附着到已运行的 live bridge
- **WHEN** 调用方创建 `HttpGameBridge` 并调用 `attach_or_start()`
- **THEN** client MUST 先检查 `/health`
- **THEN** 若 bridge 健康可达，client MUST 返回一个可供后续读取 snapshot/actions 的本地 session

#### Scenario: bridge 不可达时拒绝附着
- **WHEN** `attach_or_start()` 时本地 bridge 端口不可连接
- **THEN** client MUST 返回或抛出明确的 bridge 连接错误
- **THEN** 上层 MUST 能据此中断 autoplay，而不是进入空轮询

### Requirement: `HttpGameBridge` 必须把 HTTP 结果映射为现有模型与错误类型
client MUST 将 `/snapshot`、`/actions`、`/apply` 的 JSON 结果映射为仓库已有的 `DecisionSnapshot`、`LegalAction`、`ActionResult` 或等效错误类型。对于 `stale_decision`、`illegal_action`、`read_only` 等已知错误码，client MUST 尽量映射为现有 `BridgeError` 子类或保留结构化错误码。

#### Scenario: `/apply` 成功返回 accepted
- **WHEN** `/apply` 返回 `status = "accepted"`
- **THEN** client MUST 产出 `ActionResult`
- **THEN** 结果 MUST 保留 `accepted_action_id`、`message` 与关键 metadata

#### Scenario: `/apply` 返回 stale decision
- **WHEN** `/apply` 返回 `error_code = "stale_decision"` 或等效错误
- **THEN** client MUST 将其映射为 `StaleActionError` 或带同等语义的 bridge 错误
- **THEN** 上层 orchestrator MUST 能据此安全中断本轮决策

### Requirement: `HttpGameBridge` 必须保持只从当前 legal actions 提交动作
client MUST 只提交调用方显式给出的 `action_id` 与当前 `decision_id`，不得自行猜测或改写动作内容。若桥接端返回当前动作不在 legal set 中，client MUST 将该错误向上传递，而不是自动替换成其他动作。

#### Scenario: 提交当前 decision 的 action_id
- **WHEN** orchestrator 调用 `submit_action()` 并传入 `decision_id` 与 `action_id`
- **THEN** client MUST 原样构造 `/apply` 请求
- **THEN** 请求中 MUST 包含当前 decision 上下文

#### Scenario: bridge 拒绝非法动作
- **WHEN** `/apply` 返回 `illegal_action`、`invalid_action` 或等效错误
- **THEN** client MUST 将该失败返回给上层
- **THEN** client MUST NOT 在本地替换成其他 legal action 重试
