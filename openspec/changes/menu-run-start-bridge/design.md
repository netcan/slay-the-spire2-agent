## Context

当前 bridge 设计偏向“进入活动 run 之后”的窗口（combat/map/reward）。当游戏停留在主菜单或开局流程时，in-game runtime 端通常只会返回 “run 未就绪”，导致自动化无法完成从启动到对局的闭环。

为了支持自动化测试与批量调试，需要把“从 menu 进入 run”的流程建模为可读状态与可执行动作，并复用现有的 `snapshot/actions/apply` 协议与受控执行队列机制。与此同时，菜单界面天然存在多选项入口，必须避免误点（例如把 Settings/Exit 当成继续），保证动作语义稳定、可诊断、可回退。

约束：
- 协议与实现保持最小增量，不引入图像识别等新依赖。
- 仍要求所有写动作在游戏主线程受控执行。
- 文档使用简体中文，UTF-8 无 BOM。

## Goals / Non-Goals

**Goals:**
- 在 `snapshot.phase` 引入明确的 `menu`（或等效）语义，使“无活动 run 但可操作”有稳定建模。
- 在 `actions` 中导出菜单开局相关 legal actions：优先覆盖 `continue_run` 与 `start_new_run`，并在需要时导出 `select_character`、`confirm_start_run`（以及可选 `set_seed`）。
- 在 `POST /apply` 中实现这些动作的受控执行映射，确保具备 `decision_id` 校验、stale 防护与结构化失败诊断。
- 为 fixture 与 live 提供可复现验证路径：能从主菜单自动进入 run，并确认 `decision_id/state_version` 推进到 map/combat 等后续 phase。

**Non-Goals:**
- 不在本变更中覆盖所有菜单分支与设置项（Settings/Credits/Exit 等），也不保证能穿透所有教程/弹窗。
- 不要求一次性支持“全流程新 run 配置”的全部维度（难度、模式、mod 选项等）；优先支持最常见路径。
- 不在本变更中引入外部进程管理（启动游戏 exe）。该能力可由 `tools/` 脚本在 mod bridge ready 后再调用菜单动作实现。

## Decisions

### 1) 新增 `phase="menu"`，避免在无 run 时伪装为 map/combat/reward

相比使用 `unknown` 或复用 `terminal`，显式 `menu` 能让上层 orchestrator 更容易写出确定性逻辑（例如：优先尝试 continue，否则 start new run）。同时可以保持 `player=null`、`enemies=[]`、`map_nodes=[]` 的“非 run”快照形态，不违反“不得伪造 run 数据”的约束。

备选方案：继续保持 `phase="unknown"`，仅在 metadata 暴露 menu 信息。缺点是上层需要依赖非契约字段，协议语义不清晰。

### 2) 动作集合按“安全优先”分级导出

- `continue_run`: 只有当 Continue 按钮明确可用时才导出。
- `start_new_run`: 只有当 New Run/Start 按钮明确存在且不会覆盖已有存档（或已明确进入新 run 配置流程）时才导出。
- `select_character`/`confirm_start_run`: 仅在已经进入“新 run 配置/角色选择”流程，并且控件可点击时导出。

并在 `metadata` 增加 diagnostics（按钮文本、目标控件类型、探测来源、被抑制原因），便于 live 兼容问题定位。

### 3) apply 侧采用“提交校验 + 执行时重新解析控件”的 stale 防护

菜单 UI 变化快（按钮禁用、弹窗遮挡、流程跳转），因此 apply 执行阶段不直接持久化对象引用；而是在主线程消费时重新探测目标控件并执行点击。若探测失败或控件不可点击，返回 `stale_action`/`runtime_incompatible`/`not_clickable` 等结构化原因。

备选方案：在 action params 中携带反射路径/对象 id。实现复杂，且跨帧更易失效。

## Risks / Trade-offs

- [新增 phase 需要上层适配] → 在 Python orchestrator 中把 `menu` 作为“可操作但非战斗”的 phase 分支处理，并保持未知 phase 的 fail-safe。
- [误点危险按钮（Exit/Abandon）] → 仅匹配白名单按钮类型与关键词（Continue/New Run/Start/Confirm），并要求控件可点击且不在多选菜单混淆态；对不确定情况宁可不导出动作。
- [版本差异导致反射字段变更] → 使用多信号探测（节点类型名、字段名、可读文本、按钮集合遍历），并在 metadata 输出 diagnostics 方便快速修复。

