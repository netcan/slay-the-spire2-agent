## 1. Runner 跨 phase 状态机

- [x] 1.1 在 `src/sts2_agent/` 中扩展 autoplay 状态机，显式覆盖 `reward_choice`、`card_reward_selection`、`map`、`transition_wait` 与 `combat_resume` 等阶段。
- [x] 1.2 调整主循环停止条件：允许在 `reward` 和 `map` 后继续运行，直到进入下一场 `combat`、命中不支持窗口或触发安全停止。
- [x] 1.3 为 `reward` 与 `map` 阶段接入合法动作选择流程，复用当前 legal actions 校验，并支持 `choose_reward`、`skip_reward`、`choose_map_node`。

## 2. 安全策略与运行配置

- [x] 2.1 为 reward/map 决策增加可配置模式（如 `llm`、`safe-default`、`skip-only` 或等效选项），并在 CLI/配置层暴露。
- [x] 2.2 实现 `transition_timeout`、`max_non_combat_steps`、未知窗口熔断等安全边界，区分正常等待与异常卡死。
- [x] 2.3 为 map 选路补充一个保守默认策略，确保在不依赖 LLM 的情况下也能跑通 `reward -> map -> next combat`。

## 3. Bridge 导出与诊断补强

- [x] 3.1 检查并补强 mod 侧 reward 完成后到 map 出现前的 metadata/diagnostics，确保 runner 能识别过渡态。
- [x] 3.2 检查并补强 map 窗口的 `snapshot.map_nodes` 与 `choose_map_node` 导出稳定性，覆盖节点暂不可达或文本 fallback 场景。
- [x] 3.3 验证地图选路后进入下一房间/下一战斗时 `phase`、`decision_id`、`state_version` 的推进语义，并在必要时补充实现或测试。

## 4. Trace、验证与文档

- [x] 4.1 扩展 trace 与运行摘要，记录 reward/map/transition/combat_resume 的步骤类型、动作统计与 `next_combat_entered` 等结果字段。
- [x] 4.2 新增或更新自动化测试与 live 验证脚本，覆盖 `reward -> map -> next combat` 至少一条真实链路。
- [x] 4.3 更新 README 或 `docs/`，说明新配置项、停止原因、常见卡点诊断方式与推荐联调命令。
