# 原型 bridge 校验记录

当前最小验证脚本为 `tools/validate_mod_bridge.py`，默认启动 `Sts2Mod.StateBridge.Host` 的 `fixture` 模式，并执行一轮读写闭环检查。

## 已覆盖的窗口

- `combat`
- `reward`
- `map`
- `terminal`

## 已验证的契约行为

- `GET /health` 能返回健康状态与兼容性元数据
- `GET /snapshot` 能为四类窗口返回稳定的 `decision_id` / `state_version`
- `GET /actions` 能返回当前 legal actions 集合
- `POST /apply` 能接受合法动作并推进到下一窗口
- 旧 `decision_id` 会被拒绝为 `stale_decision`
- `terminal` 窗口没有 legal actions

## 执行方式

```bash
dotnet build mod/Sts2Mod.StateBridge.sln
python tools/validate_mod_bridge.py
```

## 当前边界

- 该脚本仍基于 `fixture`，用于快速回归协议与写接口行为。
- 真实 `in-game-runtime` 仍建议按 `docs/sts2-mod-local-development.md` 中的手工流程联调。
- 若后续补充 `HttpGameBridge`，可直接复用本脚本中的请求顺序扩展为端到端 agent 验证。
- `tools/validate_mod_bridge.py` 现在会覆盖 reward 链路中的二级窗口：从奖励列表 `choose_reward` 进入 `reward_card_selection`，再执行一次 `choose_reward` 进入 `map`。

## 真实 live apply 联调记录

### 2026-03-14

- 执行命令：`python tools/validate_live_apply.py --launch --game-dir "F:\SteamLibrary\steamapps\common\Slay the Spire 2" --wait-seconds 60`
- 实际 `/health`：
  - `provider_mode = "in-game-runtime"`
  - `read_only = true`
  - `status = "game runtime attached; waiting for an active run."`
- 结果：bridge 已成功挂到真实游戏进程，但因为当前停留在主菜单、没有活动 run，`/snapshot` 返回 `500 Internal Server Error`，因此本次只记录到失败诊断，未进入真实 `POST /apply` 冒烟阶段。
- 结果 artifacts：`tmp/live-apply-validation/20260314-010134/result.json`
- 后续操作：进入一局实际 run，并在需要写入验证时以 `read_only=false` 重新执行 `python tools/validate_live_apply.py --apply --allow-write ...`

### 2026-03-14（战斗窗口二次联调）

- 实际 `/health`：
  - `provider_mode = "in-game-runtime"`
  - `read_only = false`
  - `status = "live runtime attached; phase=combat; game_version=v0.98.3 (cb602cef)"`
- 在真实战斗窗口中，`/snapshot` 与 `/actions` 已可稳定读取；两张 `防御`、两张 `打击` 仍保持不同 `card_id` / `action_id`。
- 对 `play_card` 与 `end_turn` 的真实 `POST /apply` 均返回：
  - `status = "failed"`
  - `error_code = "action_timeout"`
- 对应 artifacts：
  - `tmp/live-apply-validation/20260314-073334`
  - `tmp/live-apply-validation/20260314-073406`
- 结论：问题已缩小到 in-game action queue 的消费链路，而不是动作选择、只读保护或 stale decision 校验。

### 2026-03-14（战斗窗口成功冒烟）

- 执行命令：`python tools/validate_live_apply.py --enable-writes --apply --allow-write --wait-seconds 5`
- 实际 `/health`：
  - `provider_mode = "in-game-runtime"`
  - `read_only = false`
  - `status = "live runtime attached; phase=combat; game_version=v0.98.3 (cb602cef)"`
- 自动候选动作：
  - `action_id = "act-4187d7f9"`
  - `type = "play_card"`
  - `label = "Play 防御"`
- `POST /apply` 返回：
  - `http_status = 200`
  - `status = "accepted"`
  - `message = "Played card 'card-c35cc4f2'."`
  - `queue_stage = "completed"`
  - `elapsed_ms = 39`
- 状态推进证据：
  - `decision_id_changed`
  - `state_version_changed`
  - `action_no_longer_legal`
  - `player_energy_changed`
- 对应 artifacts：`tmp/live-apply-validation/20260314-081224`
- 结论：真实战斗窗口中的 in-game action queue 已能在游戏线程 dequeue + execute，`action_timeout` 问题已完成回归验证。

### 2026-03-14（本地 LLM 自动打牌冒烟）

- 执行命令：`python tools/run_llm_autoplay.py --base-url "http://127.0.0.1:8080/v1" --model "Qwen3.5-9B-Q5_K_M.gguf" --bridge-base-url "http://127.0.0.1:17654" --trace-dir "tmp/llm-autoplay/20260314-live2" --max-steps 1`
- 本地模型接口探测：
  - `GET /v1/models` 可正常返回模型列表
  - 实际使用模型：`Qwen3.5-9B-Q5_K_M.gguf`
- 模型输出：
  - `action_id = "act-ebcfb923"`
  - `reason` 指向优先打出 `痛击`
  - `halt = false`
- 自动提交结果：
  - `bridge_result.status = "accepted"`
  - `bridge_result.message = "Played card 'card-292f28c0'."`
  - `target_id = "1"`
  - `queue_stage = "completed"`
- 状态推进结果：
  - 执行前：`decision_id = "dec-26615189"`、`state_version = 9`、敌人 `hp = 46`、玩家 `energy = 2`、手牌含 `痛击`
  - 执行后：`decision_id = "dec-7da0488f"`、`state_version = 12`、敌人 `hp = 38`、玩家 `energy = 0`、`痛击` 已离开手牌
- 对应 trace：`tmp/llm-autoplay/20260314-live2/sess-0d1e57b5.jsonl`
- 额外诊断：首次尝试 `tmp/llm-autoplay/20260314-live1/sess-0d1e57b5.jsonl` 因未补 `target_id` 返回 `play_rejected`；随后在 orchestrator 提交阶段自动补齐唯一目标后，真实自动出牌成功。

### 2026-03-14（回合结束修复与完整玩家回合多步 autoplay）

- `end_turn` live 修复验证：
  - 直接对 `action_id = "act-3fcd451b"` 调用 `/apply`
  - 返回：
    - `status = "accepted"`
    - `message = "Ended the current turn."`
    - `runtime_handler = "PlayerCmd.EndTurn"`
  - 之后 polling 观察到：
    - `decision_id`: `dec-915007da -> dec-ff6a0e91`
    - `state_version`: `7 -> 9`
    - 随后继续推进到下一玩家回合：`round_number = 2`、`current_side = "Player"`
  - 对应 artifacts：`tmp/end-turn-validate/20260314-091705`
- 完整玩家回合多步 autoplay：
  - 执行命令：`python tools/run_llm_autoplay.py --bridge-base-url "http://127.0.0.1:17654" --base-url "http://127.0.0.1:8080/v1" --model "Qwen3.5-9B-Q5_K_M.gguf" --trace-dir "tmp/llm-autoplay/20260314-091747-post-endturn-fix" --max-steps 12 --max-actions-per-turn 12`
  - `RunSummary`：
    - `completed = true`
    - `interrupted = false`
    - `decisions = 4`
    - `actions_this_turn = 4`
    - `ended_by = "auto_end_turn"`
  - 真实动作序列：
    - 第 1 手：`打击`
    - 第 2 手：`打击`
    - 第 3 手：`打击`
    - 第 4 手：自动 `end_turn`
  - 结果：
    - 敌人血量：`46 -> 28`
    - 回合结束后已进入下一玩家回合：`round_number = 3`
  - 对应 trace：`tmp/llm-autoplay/20260314-091747-post-endturn-fix/sess-384c418e.jsonl`
- 联调结论：
  - `PlayerCmd.EndTurn(...)` 已修复此前 `end_turn` accepted 但状态不推进的问题
  - 多步 runner 在真实战斗中已能跨多次 `stale_action` 竞争态恢复，并完成“玩家回合内连续出牌 -> 自动结束回合”的闭环

### 2026-03-14（reward 窗口识别修复 live 冒烟）

- 背景：曾出现“画面已进入奖励，但 bridge 仍导出 `phase=combat`、`window_kind=player_turn`、`actions=[end_turn]`”的误判。修复后引入 `combat_transition` 过渡态，并在 reward 已可见时稳定切换为 `phase=reward`。
- 关键修复点：单人模式下 STS2 的 `ScreenStateTracker` 不会维护 `_connectedRewardsScreen`，因此改为从 `NOverlayStack.Peek()` 读取当前 overlay 的 `NRewardsScreen` 作为 reward screen 来源。
- live snapshot 结果（bridge 端）：
  - `phase = reward`
  - `rewards = ["17金币", "将一张牌添加到你的牌组。"]`
  - `actions = [choose_reward x2, skip_reward]`
  - `metadata.phase_detection.reward_screen_source = "overlay_stack"`
- 对应 artifacts：`tmp/reward-phase-detection/20260314-105321`

### 2026-03-14（agent reward 决策：skip 模式 live 冒烟）

- 执行命令：`python tools/run_llm_autoplay.py --bridge-base-url "http://127.0.0.1:17654" --reward-mode skip --trace-dir "tmp/llm-autoplay/20260314-reward-skip-live" --max-steps 6`
- 结果摘要：
  - `RunSummary.ended_by = "reward_skipped"`
  - `total_actions = 1`
  - 提交动作：`skip_reward`
- 对应 trace：`tmp/llm-autoplay/20260314-reward-skip-live/sess-9a95e0d2.jsonl`

### 2026-03-14（reward 二级选牌界面识别与导出约定）

- 目标：当奖励链路进入“选牌二级界面”时，bridge 仍必须导出为 `phase=reward`，并提供可执行的 `choose_reward|skip_reward`。
- 期望 snapshot/action 特征（bridge 端）：
  - `snapshot.phase = "reward"`
  - `snapshot.metadata.window_kind = "reward_card_selection"`
  - `snapshot.metadata.reward_subphase = "card_reward_selection"`
  - `snapshot.metadata.detection_source` 指向 overlay 探测来源（例如 `overlay_stack.card_reward_selection`）
  - `snapshot.metadata.overlay_top_type` 暴露 overlay 顶部 screen 的运行时类型名
  - `actions` 含 `choose_reward`（每个选项一条，带 `params.reward_index`）以及在可跳过时含 `skip_reward`
- fixture 回归（无需启动游戏）：
  - `python tools/validate_mod_bridge.py`
- live 冒烟（需要把游戏停在选牌二级界面）：
  - 只读 capture：`python tools/validate_reward_card_selection.py`
  - 如需真实选择一张牌（写入）：先以 `read_only=false` 且 `STS2_BRIDGE_ENABLE_WRITES=true` 启动游戏，再运行 `python tools/validate_reward_card_selection.py --apply --allow-write`
