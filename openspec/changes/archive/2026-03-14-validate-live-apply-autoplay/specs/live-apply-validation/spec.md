## ADDED Requirements

### Requirement: 系统必须提供真实游戏内的受控 apply 验证流程
系统 MUST 提供一条面向真实 STS2 进程的 `live-apply-validation` 流程，能够读取当前 `health`、`snapshot` 与 `actions`，选择一个当前合法且可执行的动作，并在满足安全前提时发起 `POST /apply` 请求。该流程 MUST 与现有 bridge HTTP 协议保持一致，不得依赖仅在 fixture 模式存在的假数据路径。

#### Scenario: 在 live 战斗窗口发现可执行动作
- **WHEN** bridge 已连接到真实游戏进程，当前 `snapshot.phase` 为 `combat`，且 `/actions` 中存在一个结构化可执行动作
- **THEN** 验证流程 MUST 能读取该动作的 `action_id`、`action_type` 与 `params`
- **THEN** 验证流程 MUST 生成一份待执行候选动作说明

#### Scenario: 当前没有安全候选动作时不强行出牌
- **WHEN** 当前 live 窗口中不存在满足验证策略的候选动作
- **THEN** 验证流程 MUST 返回明确的未执行结果
- **THEN** 结果 MUST 说明当前 phase、候选筛选原因或缺失条件

### Requirement: 写入验证必须受显式安全开关约束
系统 MUST 默认以只读 discovery 方式运行 live 验证。只有当调用者显式进入 apply 模式，且 bridge 写入能力已通过 `STS2_BRIDGE_ENABLE_WRITES=true` 或等效机制开启时，验证流程才可提交真实 `POST /apply` 请求。若安全前提不满足，流程 MUST 拒绝执行写入并返回明确原因。

#### Scenario: 未开启写入能力时拒绝真实 apply
- **WHEN** 调用者请求执行真实写入，但当前环境未显式开启 bridge 写入能力
- **THEN** 验证流程 MUST 拒绝发送 `POST /apply`
- **THEN** 结果 MUST 明确标记为安全拒绝，而不是伪造成功

#### Scenario: discovery 模式只读取不写入
- **WHEN** 调用者以默认 discovery 模式运行验证流程
- **THEN** 流程 MUST 只调用只读端点，如 `/health`、`/snapshot`、`/actions`
- **THEN** 流程 MUST NOT 修改 live 游戏状态

### Requirement: 成功验证必须同时确认请求回执与状态推进
系统 MUST 将 live `POST /apply` 验证定义为“请求被接受且状态发生可观察推进”的双条件校验。验证流程 MUST 在收到 `accepted` 或等效成功回执后，继续轮询新的 `snapshot` 或 `actions`，确认 `decision_id`、phase、手牌、能量或 legal actions 至少有一项发生与该动作一致的变化；否则 MUST 返回 `inconclusive`、`failed` 或等效非成功结论。

#### Scenario: 真实出牌后进入新的决策上下文
- **WHEN** 验证流程对当前 live `decision_id` 提交一个合法动作，且 bridge 返回请求已接受
- **THEN** 流程 MUST 继续等待并读取新的 live 状态
- **THEN** 若新的 `decision_id` 已变化或原动作已不再合法，流程 MUST 将本次验证标记为成功

#### Scenario: 请求被接受但状态长时间不变
- **WHEN** bridge 返回请求已接受，但在验证超时窗口内 live `snapshot` 与 `actions` 没有出现可观察推进
- **THEN** 验证流程 MUST NOT 将结果标记为成功
- **THEN** 结果 MUST 明确区分为 `inconclusive`、超时或等效诊断状态

### Requirement: 验证流程必须输出可复盘的结构化 artifacts
系统 MUST 为每次 live 验证生成独立的结构化 artifacts，至少记录执行前状态、候选动作、实际请求、回执、执行后状态与最终结论。artifacts MUST 使用 UTF-8 无 BOM 编码，便于中文诊断信息与后续自动化消费。

#### Scenario: 单次验证生成完整结果目录
- **WHEN** 调用者完成一次 discovery 或 apply 验证流程
- **THEN** 系统 MUST 生成一个按时间或运行标识隔离的结果目录
- **THEN** 目录 MUST 至少包含验证前后快照、动作请求/响应以及汇总结论文件

#### Scenario: 验证失败时仍保留诊断材料
- **WHEN** live 验证因环境、网络、协议校验或状态超时而失败
- **THEN** 系统 MUST 仍输出已收集到的诊断 artifacts
- **THEN** 结果文件 MUST 包含失败阶段、失败原因与关键上下文
