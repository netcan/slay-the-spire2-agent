## Context

当前仓库已经具备两类基础能力：一类是 in-game runtime bridge，可在真实 STS2 进程中导出 `snapshot`、`actions` 与 `health`；另一类是调试脚本，可完成构建、安装、启动游戏与只读联调。与此同时，`action-apply-bridge` 已经定义了 `POST /apply` 的受控执行语义，最近也补齐了重复手牌场景下稳定的 `card_id` / `action_id`。

缺口在于：还没有一条针对真实游戏进程、可重复执行、可留痕的写入验证流程。开发者目前只能手动观察游戏画面或临时发请求，无法快速判断“bridge 是否真的能在当前版本下自动打出一张牌，并且状态确实推进”。这个缺口会直接阻塞后续 agent 接入，因为没有稳定的联调基线。

## Goals / Non-Goals

**Goals:**
- 提供一条真实游戏内的 `POST /apply` 自动验证流程，能从 live `snapshot` / `actions` 里挑选一个安全动作并发起请求。
- 在写入前做显式安全检查，只有当 `STS2_BRIDGE_ENABLE_WRITES=true` 且当前状态可执行时才真正发起动作。
- 在动作提交后等待 bridge 状态推进，并输出可复盘的验证 artifacts，至少包含执行前后快照、被选中的动作、HTTP 回执与验证结论。
- 尽量复用现有 `tools/debug_sts2_mod.py`、HTTP bridge 客户端逻辑与当前目录结构，避免新增一套割裂的调试链路。

**Non-Goals:**
- 不在本次变更中扩展新的游戏动作类型；验证对象只要求覆盖已经支持的 live `apply` 动作。
- 不实现完整自动爬塔或多步策略循环；本次只关注单步或单回合级别的真实写入验证。
- 不改变 bridge 默认的只读策略，也不在未显式开启写入时偷偷降级执行。

## Decisions

1. 采用“独立验证脚本 + 复用现有 debug 启动器”的方案
   - 优先在 `tools/` 下新增或扩展验证脚本，而不是把验证逻辑塞进 mod 内部。
   - 这样可以直接在宿主机侧完成启动、探活、选动作、发请求、落盘 artifacts，更适合后续 CI 外的人工联调。
   - 备选方案是把验证逻辑写进 `debug_sts2_mod.py` 的单个命令中，但会让启动器职责过重，不利于后续扩展更多验证模式。

2. 验证流程采用“两阶段模式”：discovery + apply
   - `discovery` 阶段只读取 `/health`、`/snapshot`、`/actions`，筛选候选动作并打印原因，不做写入。
   - `apply` 阶段要求显式开启写入，并基于 `decision_id`、`action_id`、`params` 提交真实请求。
   - 这样即使使用者先在战斗中停留到特定局面，也能先看脚本会选什么，再决定是否真正出牌。

3. 候选动作优先级采用“低风险优先”策略
   - 首选 `combat` 中具备 `card_id` 的 `play_card`，并优先选择无需额外目标参数、能直接执行的动作。
   - 若当前窗口没有可安全执行的 `play_card`，可以按明确规则回退到 `end_turn` 或其他已支持、低歧义动作，但必须把回退原因写入结果。
   - 不使用模糊 label 匹配，所有选择都基于结构化字段，保证与 agent 后续调用路径一致。

4. 成功判定采用“HTTP 接受 + live 状态推进”双条件
   - 仅收到 `accepted` 还不算验证成功；脚本还必须轮询新的 `snapshot` / `actions`，确认 `decision_id` 变化、目标动作消失、手牌/能量/phase 发生预期推进中的至少一种。
   - 如果桥接层接受请求但状态长期不变，结果应标记为 `inconclusive` 或等效状态，而不是误报成功。
   - 这样能避免把入队成功误判成真实自动打牌成功。

5. 验证结果统一落到 UTF-8 无 BOM 的结构化 artifacts
   - 每次执行生成单独目录，例如 `tmp/live-apply-validation/<timestamp>/`。
   - 目录中至少包含 `before_snapshot.json`、`before_actions.json`、`apply_request.json`、`apply_response.json`、`after_snapshot.json`、`result.json`。
   - 这样既方便人工复盘，也方便后续把同一套结果喂给 agent 回归测试。

## Risks / Trade-offs

- [真实局面差异大，脚本可能选不到安全动作] -> 先把 discovery 输出做完整，明确说明为什么没有候选动作，并允许用户手动指定 `action_id` 或动作类型过滤。
- [游戏状态推进存在动画或队列延迟] -> 验证时增加轮询窗口与超时配置，不把一次瞬时未变化直接判为失败。
- [写入验证具备真实副作用] -> 默认 discovery，不传 `--apply` 或不开启 `STS2_BRIDGE_ENABLE_WRITES` 时绝不发写请求；文档中显式要求只在可接受的测试局面使用。
- [本地环境差异影响复现] -> 结果 artifacts 中记录端口、时间戳、provider mode、phase、被选动作与关键环境变量，方便定位是否为环境问题。
