## Why

当前 in-game runtime bridge 已能读到真实局面，但部分文本字段仍以类名或反射对象形式泄漏，例如 relics 中出现 `MegaCrit.Sts2.Core.Localization.LocString`。如果不先修复文本解析和状态归一化，后续 Agent 或大模型很难直接使用这些字段做稳定决策。

## What Changes

- 增强 runtime 反射文本提取逻辑，优先解析 relics、potions、rewards、map nodes 和其他面向用户的显示文本。
- 增加对 `LocString` 及类似本地化容器的专用解包策略，避免对外输出类名、空字符串或无意义 `ToString()` 结果。
- 在 `/snapshot` 与 `/actions` 层面补齐更稳定的字段质量约束，保证面向 Agent 的字段尽可能输出可读、可比对、可调试的文本。
- 为状态导出增加降级与 diagnostics metadata，在文本解析失败时仍能明确区分“缺字段”、“未本地化”、“反射不兼容”等情况。

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `in-game-runtime-bridge`: 调整 live `snapshot` 与 `actions` 的文本导出要求，增加可读性、降级策略和 diagnostics metadata 约束。

## Impact

- 主要影响 `mod/Sts2Mod.StateBridge/Providers/Sts2RuntimeReflectionReader.cs` 中的文本提取、反射解析与状态构造流程。
- 可能影响 `mod/Sts2Mod.StateBridge/Extraction/WindowExtractors.cs` 和相关 contracts，以便暴露额外 metadata 或更稳定的 action label。
- 会增加运行时调试与回归校验要求，确保作为 Agent 输入的状态质量稳定。
