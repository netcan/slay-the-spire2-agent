## Why

当前 bridge 已经把卡牌与 powers 的 `description` 收敛为可直接消费的 canonical 文本，但 `description_quality`、`description_source`、`description_vars` 这类诊断字段仍继续暴露给 Python client 与 LLM payload。它们主要用于排障，而不是正常决策；继续把这些内部结构放进公共 schema，会让客户端承担不必要的协议复杂度，也会放大后续 schema 演进成本。

现在需要把这类说明解析诊断彻底收敛到 mod 日志与本地调试 artifacts：客户端只拿稳定、精简、面向决策的最终文本；排障时则通过日志定位具体解析来源、fallback 路径与变量提取问题。

## What Changes

- **BREAKING** 从公共 snapshot / action schema 中移除 `description_quality`、`description_source`、`description_vars` 等仅用于说明解析诊断的字段，不再要求客户端理解这些内部结构。
- 统一约束 cards、powers、card preview 等说明类对象对外只保留 `description` 与必要的 `glossary` 等用户向信息。
- 在 mod runtime 中新增或强化文本解析 debug logging，把说明来源、变量提取、fallback 原因与 unresolved 情况写入日志，而不是默认塞进客户端响应。
- 调整 Python bridge、LLM payload、fixtures、validation 与 live artifacts，使其围绕精简后的公共 schema 运作；若需要调试说明质量，改为读取日志或专门的 debug artifact。

## Capabilities

### New Capabilities

- 无

### Modified Capabilities

- `game-bridge`: 调整 bridge 对外快照契约，要求面向 agent 的公共对象移除说明解析 diagnostics，只保留稳定的用户向文本字段。
- `in-game-runtime-bridge`: 调整 live runtime bridge 的调试语义，要求说明解析问题优先写入 mod 日志或等效 debug diagnostics，而不是进入主响应字段。
- `mod-state-export`: 调整统一状态导出约束，要求说明类对象对外导出精简 schema，并定义日志化诊断的边界。

## Impact

- 受影响代码主要位于 `mod/Sts2Mod.StateBridge/Providers/Sts2RuntimeReflectionReader.cs`、runtime contracts、JSON serialization 与日志组件。
- Python 侧会影响 `src/sts2_agent/models.py`、`src/sts2_agent/bridge/`、`src/sts2_agent/policy/llm.py`、fixtures 与验证脚本。
- 需要补充日志侧验证与 live 联调，确保移除公共 diagnostics 后仍能通过 mod 日志定位 description 解析问题。
