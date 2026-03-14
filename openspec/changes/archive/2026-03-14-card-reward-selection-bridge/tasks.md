## 1. 运行时识别与诊断

- [x] 1.1 在 in-game runtime `snapshot.metadata` 中补充 overlay 顶部 screen 的类型名（例如 `overlay_top_type`），便于现场识别“选牌二级界面”的真实类型。
- [x] 1.2 在 `AnalyzeRewardPhase(...)` 中加入“卡牌奖励选择界面”探测逻辑，并在 metadata 中输出 `reward_subphase` 与 `detection_source`。
- [x] 1.3 确保在无存活敌人且处于卡牌奖励选择界面时，`DetectPhase(...)` 返回 `reward` 而不是回落到 `combat`。

## 2. Reward 窗口导出与 legal actions

- [x] 2.1 实现卡牌奖励选择界面的可选卡牌提取：产出与展示顺序一致的 rewards labels（优先本地化文本，fallback 为 `card_<index>`）。
- [x] 2.2 在卡牌奖励选择界面构造 `choose_reward` legal actions：`params.reward_index` 与 `snapshot.rewards` 索引一一对应，并补充最少必要的 diagnostics（例如文本解析来源）。
- [x] 2.3 仅在可跳过时导出 `skip_reward` legal action；不可跳过时在 metadata 中标记 `reward_skip_available=false` 与原因字段。
- [x] 2.4 为该窗口设置可区分的 `metadata.window_kind`（例如 `reward_card_selection`），避免与 `combat_transition` 混淆。

## 3. apply 映射扩展（choose_reward / skip_reward）

- [x] 3.1 扩展 `ExecuteChooseReward(...)`：当当前窗口为卡牌奖励选择界面时，按 `reward_index` 精确选择对应卡牌；界面变化或索引越界时返回 `stale_action`。
- [x] 3.2 扩展 `ExecuteSkipReward(...)`：当当前窗口为卡牌奖励选择界面且跳过可用时，触发真实跳过/关闭钩子；钩子不可用时返回 `runtime_incompatible`。
- [x] 3.3 保持协议兼容：对外 action type 仍为 `choose_reward` / `skip_reward`，不引入新 type。

## 4. 测试与联调回归

- [x] 4.1 在 `mod/Sts2Mod.StateBridge.Host` 或 fixture provider 中加入可复现的“卡牌奖励选择界面”快照/动作样例，用于回归导出字段与 action 列表。
- [x] 4.2 为新增的 reward 子窗口导出逻辑补充最小单测/集成测试，覆盖：phase 判定、rewards 非空、choose_reward 索引一致、skip_reward 条件导出。
- [x] 4.3 增加 live 冒烟步骤与 artifacts：进入 reward，选择“加牌”，确认二级选牌界面仍导出为 `phase=reward` 且可执行 `choose_reward|skip_reward`，并记录日志/trace 文件路径。（artifacts: `tmp/reward-card-selection-validation/20260314-125328`）
- [x] 4.4 更新 `docs/prototype-validation.md`，写明该场景的验证命令、期望输出与常见失败诊断字段。
