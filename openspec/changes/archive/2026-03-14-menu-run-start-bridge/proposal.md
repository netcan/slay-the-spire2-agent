## Why

当前 bridge 的自动对局与回归测试仍需要人工从主菜单进入一局 run（Continue 或 New Run），这会让端到端自动化无法在干净环境下“一键启动并复现”，也阻碍 CI/本地批量调试的稳定性与可重复性。

## What Changes

- 在 in-game runtime 中增加“主菜单/开局流程（menu）”窗口的稳定识别与导出：当尚未进入活动 run 时，`snapshot`/`actions` 仍能返回结构化的菜单状态，而不是仅返回“run 未就绪”的不可操作状态。
- 在 `actions` 中新增一组菜单开局相关动作（拟定：`continue_run`、`start_new_run`、`select_character`、`confirm_start_run`，可选 `set_seed`），用于把“进入游戏”流程建模为可执行的 legal actions。
- 在 `POST /apply` 中增加上述动作的真实执行映射，确保在游戏主线程受控执行，并提供可诊断回执（accepted/rejected/failed + handler/原因）。
- 补充 fixture 与 live 联调脚本，支持从主菜单自动进入 run，为后续“完整战斗自动化”提供可重复的起点。

## Capabilities

### New Capabilities
- 无

### Modified Capabilities
- `in-game-runtime-bridge`: 增加 menu 窗口导出要求（`snapshot.phase="menu"`、菜单动作集合与 diagnostics），使 bridge 在无活动 run 时仍可驱动进入 run。
- `action-apply-bridge`: 扩展动作提交与执行映射，新增菜单开局动作的受控执行语义与拒绝/失败诊断约束。

## Impact

- 受影响代码主要在 `mod/Sts2Mod.StateBridge/Providers/Sts2RuntimeReflectionReader.cs` 的窗口识别、动作生成与 apply 映射；以及 `src/sts2_agent/orchestrator.py` 对 `phase="menu"` 的兼容处理（例如等待/推进策略）。
- 受影响接口为 live `/snapshot`、`/actions` 与 `POST /apply`（新增 action type）；并会影响 `tools/` 下的自动化冒烟脚本与 fixtures。
- 该能力面向自动化测试与本地批量调试，不要求一次性覆盖所有菜单分支，但需要保证“继续存档/开始新 run”两条主路径可用且可诊断。

