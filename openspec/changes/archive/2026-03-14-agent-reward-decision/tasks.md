## 1. Reward 决策模式与配置

- [x] 1.1 在 `OrchestratorConfig` / `LiveAutoplayConfig` 中新增 `reward_mode`（`halt|skip|llm`），并提供合理默认值（默认 `halt`）。
- [x] 1.2 在 `tools/run_llm_autoplay.py` 增加 `--reward-mode` 参数与环境变量读取（如 `STS2_REWARD_MODE`），并把配置透传到 orchestrator。

## 2. Orchestrator 支持 reward 执行

- [x] 2.1 扩展 orchestrator 的“可执行窗口”判定：在 `phase=reward` 且 `reward_mode != halt` 时进入 reward handler，而不是当作非玩家回合等待。
- [x] 2.2 实现 `reward_mode=skip` 的执行路径：优先选择 legal actions 中的 `skip_reward` 并提交；缺少 `skip_reward` 时以明确 reason 停止。
- [x] 2.3 实现 `reward_mode=llm` 的执行路径：将 reward legal actions 交给 LLM policy 选择 action_id，复用现有动作合法性校验与失败回退语义。
- [x] 2.4 调整 battle completion/phase stop 逻辑：在 battle mode 下，进入 reward 且启用 reward_mode 时不应直接判定 `battle_completed`，而应继续执行 reward 决策直到窗口推进。

## 3. Policy 提示词与 trace 质量

- [x] 3.1 更新 `ChatCompletionsPolicy` 的 system prompt/输入摘要，使其在 `phase=reward` 时能理解 `choose_reward` / `skip_reward` 的语义，并在不确定时返回 `halt=true`。
- [x] 3.2 确保 reward 决策同样写入 jsonl trace（observation、legal actions、policy output、bridge result），并在 `RunSummary.reason` 中区分 `reward_phase_reached`、`reward_skipped`、`reward_chosen` 等关键停止原因（或等效语义）。

## 4. 测试与联调回归

- [x] 4.1 增加 orchestrator 单元测试：`reward_mode=halt` 时遇到 reward 必须停止且不写入；`reward_mode=skip` 必须提交 `skip_reward`；`reward_mode=llm` 必须只接受 legal action_id。
- [x] 4.2 在真实 live reward 界面做一次冒烟：使用 `--reward-mode skip`（或 `llm`）推进奖励窗口，并保存 trace/artifacts 路径到文档。
