## 1. Runtime 值解析链路

- [x] 1.1 梳理真实手牌实例上的动态数值候选字段与读取路径，优先覆盖 `damage`、`block`、`draw`、`magic` 等高频变量。
- [x] 1.2 重构 `Sts2RuntimeReflectionReader` 的卡牌描述解析逻辑，优先使用 live instance 值生成最终 `description_rendered`。
- [x] 1.3 为 `description_vars` 增加实例级真实值来源与 source diagnostics，避免长期输出 `null`。

## 2. 质量语义与 Bridge 导出

- [x] 2.1 为卡牌描述增加“已解析真实值 / 模板回退”质量判定，并统一应用到 snapshot 与 `card_preview`。
- [x] 2.2 调整 `description_rendered` 的兼容语义：仍含模板占位符时不再视为高质量 rendered，并补充对应 metadata/diagnostics。
- [x] 2.3 更新 fixture provider 或测试辅助对象，覆盖“真实值已解析”和“仍是模板回退”两种导出结果。

## 3. Python 摘要与降级策略

- [x] 3.1 更新 Python bridge decode / models，读写新增的描述质量或来源信号。
- [x] 3.2 更新 `ChatCompletionsPolicy` 摘要逻辑，对模板回退卡牌做安全降级，优先依赖真实值、卡名、traits 与 glossary。
- [x] 3.3 更新 live artifact 脚本，输出卡牌描述质量、已解析变量数量与 unresolved 诊断摘要。

## 4. 验证与联调

- [x] 4.1 补充 mod / Python 单元测试，覆盖基础牌 `Strike`、`Defend` 的真实值解析与模板回退场景。
- [x] 4.2 在 fixture / 本地 smoke 中验证 `description_rendered`、`description_vars` 与 `card_preview` 语义一致。
- [x] 4.3 在真实 STS2 runtime 完成至少一次 live validation，记录基础手牌的真实值导出 artifacts，并确认 agent 输入已识别质量差异。
