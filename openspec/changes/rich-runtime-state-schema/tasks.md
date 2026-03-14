## 1. Runtime contracts 与 schema 扩展

- [x] 1.1 扩展 mod contracts 与 Python models，为 `CardView`、`EnemyState`、`PlayerState`、`DecisionSnapshot` 追加 richer 字段，并保持现有基础字段兼容。
- [x] 1.2 设计并落地最小 `run_state` 结构，至少覆盖 `act/floor/current_room_type/map` 等规划上下文，并确定缺失字段的默认返回策略。
- [x] 1.3 为卡牌、敌人等对象预留 `canonical_*` 或等效稳定知识锚点字段，并明确与运行时实例 id 的区分语义。

## 2. Mod runtime reader 导出 richer state

- [x] 2.1 在 `Sts2RuntimeReflectionReader` 中补齐卡牌 richer 字段导出，优先覆盖 `description`、`upgraded`、`target_type`、`traits` 与可用关键词。
- [x] 2.2 在敌人与玩家状态导出中补齐结构化 `intent` 与 `powers[]`，对当前无法稳定解析的子字段采用可选值回退。
- [x] 2.3 将最小 `run_state` 上下文接入 combat/reward/map 快照导出，并保证 fixture provider 与 live runtime provider 行为一致。

## 3. Bridge / agent 消费路径适配

- [x] 3.1 更新 HTTP bridge、snapshot 解析与 trace 序列化，确保 richer fields 能在 Python 侧稳定读写。
- [x] 3.2 更新 `ChatCompletionsPolicy` 的 snapshot 摘要逻辑，把高价值 richer 字段纳入策略输入，同时控制 payload 膨胀。
- [x] 3.3 为 richer fields 缺失场景增加降级兼容逻辑，确保现有 autoplay 不因新字段为空而中断。

## 4. 验证与联调

- [x] 4.1 补充 mod 侧与 Python 侧单元测试，覆盖 richer card fields、结构化 intent、powers 与 `run_state` 的解析与兼容性。
- [x] 4.2 更新 fixture / smoke assets，增加至少一个 richer combat snapshot 样例，便于稳定回归。
- [x] 4.3 在真实 STS2 runtime 完成至少一次 richer state 导出联调，记录卡牌描述、敌方 intent/powers 与最小 `run_state` 的 artifacts，并确认 agent 输入已消费这些字段。
