## Context

当前 live bridge 的 phase 判定主要依赖 `GetRewardScreen(runNode)` 与 `rewardScreen.IsComplete`。在真实联调中已经出现：游戏画面已进入 reward，但 runtime 仍导出 `phase = combat`、`window_kind = player_turn`、`legal_actions = [end_turn]`，同时 `enemies = []`。这说明 reward 检测存在窗口对象不稳定、生命周期边界过窄或过渡态遗漏的问题。

这个问题直接影响两层能力：一是 mod 自身导出的 `snapshot/actions` 不可信；二是上层 battle autoplay 会把 reward 误判成 combat 尾帧，只能依赖兜底 heuristics 提前退出，无法安全衔接 reward 自动决策。

约束上，本次 change 仍不扩展新的 HTTP 协议；修复点应集中在 runtime 反射读取、phase 判定与 reward context 导出，尽量保持现有 `DecisionPhase`、`RuntimeWindowContext` 与 extractor 结构不变。

## Goals / Non-Goals

**Goals:**
- 让 in-game runtime 在 reward 界面出现时稳定导出 `phase = reward`，而不是错误回落到 `combat`。
- 覆盖战斗结束到 reward 展示之间的过渡态，避免“无敌人 + 只剩 end_turn”长期伪装成 combat 玩家回合。
- 保持 reward `snapshot.rewards` 与 `actions` 的导出一致性，使 `choose_reward` / `skip_reward` 能与 phase 判定对齐。
- 增加可诊断 metadata 或测试样例，方便区分 reward 识别成功、过渡态等待与最终降级路径。

**Non-Goals:**
- 本次不实现 reward 的 LLM 自动选择策略，只修正 mod 导出的状态识别质量。
- 不修改 battle runner 的整体产品边界；runner 是否继续托管 reward 另开 change 再做。
- 不引入新的外部依赖或独立缓存服务。

## Decisions

### 1. 将 reward 判定从“单一 UI 对象存在”提升为“多信号综合判定”

`DetectPhase(...)` 不再只依赖 `_connectedRewardsScreen != null && !IsComplete`。改为按优先级综合以下信号：

- reward screen 存在且可提取 reward buttons / reward entries
- run node 或 screen tracker 中存在 reward 可见性/连接状态
- 当前战斗敌人已全部清空，且 reward 相关对象已可见
- 仅在以上信号都不满足时才回落到 `combat`

这样可以降低单个字段命名、生命周期抖动或 `IsComplete` 语义变化带来的误判。

备选方案：只把 `IsComplete` 条件去掉，发现 reward screen 非空就判 reward。实现简单，但容易把 reward 清理后残留对象也误判成 reward，鲁棒性不足。

### 2. 明确引入“combat 结束过渡态”的兜底处理

真实运行时中，敌人已清空但 reward 未完全挂载的瞬间，bridge 可能短时间既不满足 reward screen 完整条件，也不应继续导出玩家可操作 combat 窗口。这里优先做两层处理：

- phase 判定优先检查“是否仍有存活敌人”
- 当敌人已空且 reward 信号正在出现时，导出 reward；至少不得继续生成误导性的 `play_card` / `end_turn` 玩家动作集合

备选方案：新增独立 `combat_transition` phase。语义更精确，但会扩展现有协议和上层处理复杂度，不符合本次最小修复目标。

### 3. reward snapshot 与 actions 必须共用同一判定来源

修复不能只改 `health` 或 `snapshot.phase`；`BuildRewardWindow(...)`、`ExtractRewards(...)` 与 `choose_reward` / `skip_reward` legal action 生成必须和 phase 使用同一组 reward 判定输入。这样才能避免 `phase = reward` 但 `actions = []`，或 `phase = combat` 却出现 reward actions 的不一致。

备选方案：只在 orchestrator 侧通过 `enemies = []` 兜底。虽然能减轻 autoplay 问题，但无法修正 bridge 契约本身，也不能支撑后续 reward 自动决策。

### 4. 用可复现测试覆盖误判样例，而不是仅靠 live 人工回归

为避免后续游戏版本或反射字段变化再次打破 reward 检测，需要增加单元/集成测试，至少覆盖：

- reward screen 正常显示时判定为 reward
- 敌人已清空且 reward buttons 可读时，不得回落为 combat
- reward screen 完成或关闭后，再按地图/终局/战斗逻辑继续判定

live 手工冒烟仍保留，但只作为补充验证，不作为唯一保障。

## Risks / Trade-offs

- [奖励窗口内部字段名在版本更新后变化] → 优先使用多信号组合，并把 diagnostics 打到 metadata，降低单点字段依赖。
- [敌人已空但奖励对象尚未完全挂载，存在短暂空窗] → 通过过渡态兜底避免继续导出误导性 combat actions，并在必要时保守降级。
- [扩大 reward 判定会把部分残留 UI 误识别成 reward] → 要求 reward phase 至少满足“对象存在 + 可读 reward 内容/按钮”等联合条件，而不是只看对象非空。
- [修复 phase 后会影响上层 battle runner 退出时机] → 这是预期收益；runner 已按 `phase != combat` 视为 battle 完成，可直接受益。

## Migration Plan

1. 先梳理 `DetectPhase(...)`、`GetRewardScreen(...)`、`ExtractRewards(...)` 的现有反射路径与 live diagnostics。
2. 实现 reward 多信号判定，并同步调整 reward `snapshot/actions` 生成条件。
3. 增加测试覆盖误判样例与 reward 正常路径。
4. 在真实 STS2 reward 界面做一次 live 校验，确认 `health`、`snapshot.phase`、`actions` 一致导出 reward。

回滚方式：若新判定在某些版本上误报 reward，可先回退到旧逻辑，同时保留新增 diagnostics，便于下一轮更精确修复。

## Open Questions

- 当前游戏版本里，reward 可见性的最稳反射信号究竟是 `_connectedRewardsScreen`、`_rewardButtons`，还是其他 screen tracker 字段。
- 敌人全灭但 reward 未挂载的极短时间窗口，bridge 是应立即切 reward，还是短暂返回不可操作状态更安全。
