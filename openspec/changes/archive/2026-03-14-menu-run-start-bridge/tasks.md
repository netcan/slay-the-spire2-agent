## 1. 协议与模型扩展

- [x] 1.1 在 C# 合约模型中引入 `DecisionPhase.Menu = "menu"`（或等效常量），并确保 `/snapshot.phase` 能输出该值（无活动 run 且菜单可操作时）。
- [x] 1.2 扩展 legal actions 模型与序列化：新增 `continue_run`、`start_new_run`、`select_character`、`confirm_start_run`（可选 `set_seed`）等 action type 的生成与解析约束（保持向后兼容）。
- [x] 1.3 在 Python 侧 `src/sts2_agent/` 兼容 `phase="menu"`，并为未知 menu actions 保持 fail-safe（不崩溃、保留 diagnostics）。

## 2. in-game runtime 菜单识别与导出

- [x] 2.1 在 `Sts2RuntimeReflectionReader` 增加菜单窗口探测：识别主菜单、开局配置/角色选择流程，并输出 `metadata.window_kind`（例如 `main_menu`、`new_run_setup`）与探测 diagnostics。
- [x] 2.2 实现 `actions` 生成：当 Continue 可用时导出 `continue_run`；当 New Run/Start 可用时导出 `start_new_run`；进入角色选择后导出 `select_character{character_id}` 与 `confirm_start_run`。
- [x] 2.3 实现安全抑制：对不确定状态或可能误触危险按钮（Exit/Abandon 等）默认不导出动作，并在 metadata 解释抑制原因。

## 3. apply 执行映射

- [x] 3.1 在 `POST /apply` 中新增菜单动作的校验与入队逻辑，复用 `decision_id`、legal set 与只读开关检查。
- [x] 3.2 在主线程消费阶段实现菜单动作真实点击：执行时重新解析目标控件并点击/激活；失败返回 `stale_action`/`runtime_incompatible`/`not_clickable` 等结构化原因与 handler。
- [x] 3.3 增加 C# 侧回归测试：覆盖 `menu` phase 导出、动作生成、以及在控件缺失/不可点击/窗口变化时的拒绝与诊断。

## 4. 自动化测试与联调

- [x] 4.1 扩展 fixture provider：提供至少 1 个 menu fixture（含 Continue 与 New Run 两条路径各 1 个），用于验证 `snapshot.phase="menu"` 与动作集合。
- [x] 4.2 新增 `tools/validate_menu_run_start.py`（或等效脚本）：轮询等待 menu actions 出现，执行 `continue_run` 或 `start_new_run`，验证最终进入 run 内 phase（map/combat/reward）且 `decision_id/state_version` 推进。
- [x] 4.3 更新 `docs/` 或 README：记录 `menu` phase 语义、动作边界、常见失败码与排障方式（metadata diagnostics 示例）。
