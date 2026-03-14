## 1. Schema 与 contracts 扩展

- [x] 1.1 扩展 mod contracts 与 Python models，为卡牌、powers 等对象追加 `description_raw`、`description_rendered`、`description_vars` 与 glossary 锚点字段，并保持现有 `description` 兼容。
- [x] 1.2 明确 glossary 锚点的最小结构，至少区分稳定 id、显示文本与可选的简短中文提示来源。

## 2. Mod runtime reader 导出渲染文本

- [x] 2.1 在 `Sts2RuntimeReflectionReader` 中补齐卡牌描述导出，优先尝试读取当前战斗下已渲染文本，失败时回退到模板文本。
- [x] 2.2 为卡牌与 powers 提取最小 `description_vars`，优先覆盖 `damage`、`block`、`draw`、`strength` 等高价值动态数值。
- [x] 2.3 为卡牌 traits / powers / 常见状态术语补齐 glossary 锚点导出，并记录回退来源以便排障。

## 3. Python bridge 与策略输入适配

- [x] 3.1 更新 HTTP bridge、fixture decode 与 trace 序列化，确保新文本字段和 glossary 锚点能在 Python 侧稳定读写。
- [x] 3.2 更新 `ChatCompletionsPolicy` 的 snapshot 摘要逻辑，优先发送 `description_rendered`、关键变量和少量高价值 glossary 提示。
- [x] 3.3 为字段缺失、仍是模板文本或 glossary 不完整的场景增加降级兼容逻辑，确保 autoplay 不会中断。

## 4. 验证与联调

- [x] 4.1 补充 mod 侧与 Python 侧单元测试，覆盖渲染文本、变量提取、glossary 锚点与兼容回退。
- [x] 4.2 更新 fixture / smoke assets，增加至少一个包含模板文本、渲染文本与变量槽位的 combat 样例。
- [x] 4.3 在真实 STS2 runtime 完成至少一次 live validation，记录 `description_rendered`、`description_vars`、glossary 锚点与 agent 输入 artifacts。（2026-03-14 live：`tmp/card-description-rendering-glossary-live/20260314-183244`、`tmp/card-description-rendering-glossary-live/20260314-183348-agent-input`；实测已导出 glossary 与 power vars，但手牌 `description_rendered` / `description_vars` 仍可能停留在模板占位符回退。）
