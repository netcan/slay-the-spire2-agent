## Why

当前 bridge 已经能稳定导出 live `snapshot`、`actions`，并且 `play_card` 的 `card_id` / `action_id` 也已去重，但还缺少一条可重复执行的真实写入验证链路。要让后续 agent 或大模型自动打牌真正可用，现在必须补上可写模式下的 `POST /apply` 端到端验证，确认 bridge 不只是“看得见”，也能“打得出”。

## What Changes

- 新增一套 `live-apply-validation` 验证能力，用于在真实 STS2 进程中执行一次受控的 `POST /apply` 自动出牌冒烟验证。
- 提供可复用的调试脚本或工具，负责发现当前 live `decision_id`、筛选可执行动作、提交动作请求并等待状态推进。
- 为验证流程增加安全护栏，包括只在显式可写开关开启时执行、优先选择低风险动作、输出结构化验证 artifacts。
- 补充文档与结果约定，方便后续 agent 接入前快速确认当前 mod 是否具备真实自动打牌基础能力。

## Capabilities

### New Capabilities
- `live-apply-validation`: 定义真实游戏进程中对 `POST /apply` 做受控自动出牌验证的流程、产物与安全约束。

### Modified Capabilities
- None.

## Impact

- 主要影响 `tools/` 下的 live 调试与验证脚本。
- 会增加一份新的 OpenSpec capability，并补充与真实游戏联调相关的 README 或操作文档。
- 验证流程会依赖本地 STS2 安装、bridge HTTP 端点以及显式写入环境变量，不改变现有默认只读安全策略。
