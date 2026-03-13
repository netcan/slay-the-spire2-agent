## 1. 文本解析链路改造

- [x] 1.1 梳理 `Sts2RuntimeReflectionReader` 中当前 `ConvertToText`、`DescribeInventoryItem`、`DescribeReward` 的调用链路，明确 relics、potions、rewards、map nodes 的文本来源
- [x] 1.2 实现统一的多级文本解析 fallback pipeline，覆盖 `string`、`LocString`、常见文本字段和可接受 `ToString()` fallback
- [x] 1.3 将 relics、potions、rewards、cards、enemies、action labels 切换到新的文本解析路径

## 2. 状态质量与 diagnostics

- [x] 2.1 为文本解析增加 diagnostics metadata，至少能区分 resolved、fallback、unresolved 类型
- [x] 2.2 确保 `/snapshot` 与 `/actions` 主字段保持简洁稳定，不把调试信息直接混入 `name`、`label`、`relics` 等字段
- [x] 2.3 检查 state fingerprint 和 `decision_id` 推进逻辑，确保 diagnostics 增加后不会引入不必要的抖动

## 3. 测试与真实运行时验证

- [x] 3.1 为 fixture 或相关单测试补充 relics、rewards、map nodes、action labels 的文本断言
- [x] 3.2 补充文本解析失败的回归测试，验证 bridge 仍能返回可序列化结果且附带 diagnostics
- [x] 3.3 在真实 STS2 进程中重新执行 `/health`、`/snapshot`、`/actions` 联调，确认 relics 不再出现 `LocString` 类名泄漏
