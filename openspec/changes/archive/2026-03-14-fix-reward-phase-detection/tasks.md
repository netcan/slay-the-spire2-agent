## 1. reward phase 判定修复

- [x] 1.1 梳理 `Sts2RuntimeReflectionReader` 中 `DetectPhase(...)`、`GetRewardScreen(...)`、`ExtractRewards(...)` 的现有反射路径，并记录当前 reward 误判样例所依赖的字段。
- [x] 1.2 重构 reward 检测逻辑，基于 reward screen、reward buttons、敌人是否清空等多信号综合判定 `phase`，避免 reward 已显示时回落到 `combat`。
- [x] 1.3 修正战斗结束过渡态的导出行为，确保“敌人已空但奖励 UI 尚在切换”时不会继续对外暴露误导性的 `player_turn` / `end_turn` 语义。

## 2. reward 窗口导出一致性

- [x] 2.1 对齐 reward `snapshot` 与 `actions` 的生成条件，确保进入奖励界面后返回 `snapshot.phase = reward`、`snapshot.rewards` 与 `choose_reward` / `skip_reward` 等合法动作。
- [x] 2.2 在 metadata 或 diagnostics 中补充 reward 判定来源与降级信息，便于区分 reward 成功识别、过渡态和识别失败。
- [x] 2.3 校验 `health` / `snapshot` / `actions` 在 reward 界面的对外一致性，避免出现 phase、window_kind 与 legal actions 互相矛盾。

## 3. 测试与真实联调

- [x] 3.1 增加或更新 mod 侧测试，覆盖 reward 正常识别、敌人已清空后的过渡态、reward 关闭后的后续判定等场景。
- [x] 3.2 在真实 STS2 reward 界面执行一次 live 冒烟，确认 bridge 导出 `phase = reward` 且 `actions` 为 reward 动作集。
- [x] 3.3 更新相关文档或联调记录，沉淀 reward 误判样例、修复后的验证结果与 artifacts 路径。
