## 1. 回合级 orchestrator 语义

- [x] 1.1 扩展 `AutoplayOrchestrator` 配置，增加单回合动作上限、回合结束后停止、只剩 `end_turn` 时自动结束回合等参数。
- [x] 1.2 调整 orchestrator 主循环，使其在同一 `combat` 玩家回合内连续读取最新 `snapshot` / `actions` 并持续决策，而不是默认打一手就退出。
- [x] 1.3 扩展 `RunSummary`、trace 或等效结果结构，补充 `turn_completed`、本回合动作数、停止原因等回合级信息。

## 2. 多步回合停止条件与安全边界

- [x] 2.1 实现回合级停止条件，覆盖 phase 切换、terminal、模型 halt、bridge 拒绝、只剩 `end_turn`、单回合动作上限等路径。
- [x] 2.2 为“只剩 `end_turn`”场景增加可配置的自动结束回合逻辑，并在 trace / summary 中明确标记。
- [x] 2.3 确保单步 smoke 现有用法仍可工作，不因多步回合模式破坏当前 `max_steps=1` 调试流程。

## 3. CLI 与测试

- [x] 3.1 更新 `src/sts2_agent/live_autoplay.py` 与 `tools/run_llm_autoplay.py`，暴露多步回合参数并在输出中体现回合级摘要。
- [x] 3.2 增加单元测试，覆盖同回合连续两步执行、只剩 `end_turn` 自动结束、phase 变化停止、单回合动作数上限等路径。
- [x] 3.3 补充 CLI / 集成级测试，确认参数覆盖、多步 trace 落盘与回合级 summary 字段可用。

## 4. 真实联调与文档

- [x] 4.1 更新 `README.md` 或 `docs/`，说明如何运行“完整玩家回合 autoplay”以及相关安全参数。
- [x] 4.2 在真实 STS2 战斗窗口完成至少一次从玩家回合开始到该回合结束的多步 autoplay 冒烟，并记录模型输出、动作序列与最终停止原因。
