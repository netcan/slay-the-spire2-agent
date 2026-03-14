## 1. Glossary hint sourcing 设计收敛

- [ ] 1.1 梳理 cards、powers、potions、enemy move 当前 glossary anchor 的组装路径，明确哪些对象已有 `HoverTip`、`Description`、`SmartDescription` 或 `LocString` 可用。
- [ ] 1.2 设计并落地统一的 glossary hint sourcing helper，明确优先级、返回结构与 `source` 枚举语义。

## 2. Runtime 侧实现与 fallback 收缩

- [ ] 2.1 调整 `Sts2RuntimeReflectionReader` glossary 逻辑，优先从 runtime hover tip、模型描述与 localization 获取 `hint`，尽量移除现有手写 hint 表。
- [ ] 2.2 为仍无法解析真实 hint 的 glossary term 实现受控 fallback，允许空 hint 或最小 built-in hint，但必须打印 warning 日志并保留 diagnostics。
- [ ] 2.3 校正 glossary `source` 语义，避免继续把手写 hint 标成 `description_text` 或其他误导来源。

## 3. 验证与样例更新

- [ ] 3.1 更新 fixture、单元测试与必要的 schema 断言，覆盖 runtime sourced hint、fallback source 与缺失 hint 的场景。
- [ ] 3.2 在真实 STS2 runtime 完成至少一次 live validation，确认 `vulnerable` 或其他常见术语已优先导出游戏真实说明，并记录 fallback 告警样例。
