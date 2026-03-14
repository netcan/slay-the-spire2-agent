## 1. 扩展 enemy contracts 与导出结构

- [x] 1.1 更新 C# runtime/public contracts，为 `enemies[]` 新增 richer enemy fields（例如 `move_name`、`move_description`、`move_glossary`、`traits`、`keywords`），并保持现有基础字段不变。
- [x] 1.2 更新 window extractors 与 fixture provider，确保 richer enemy fields 能进入 `snapshot` 与测试场景。

## 2. 实现 live runtime enemy enrich 提取

- [x] 2.1 在 `Sts2RuntimeReflectionReader` 中补充 enemy move text、trait/tag、keyword 或等效字段的提取逻辑，尽量复用现有文本解析与 glossary 链路。
- [x] 2.2 为单个 enemy 或单个 enrich 字段的读取失败设计独立降级与 metadata diagnostics，避免整个 enemy 列表或 combat snapshot 失败。

## 3. 更新 Python 侧消费链路

- [x] 3.1 更新 `src/sts2_agent/models.py`、HTTP/mock bridge 与 fixtures，使 Python 侧可以解析 richer enemy fields。
- [x] 3.2 调整 LLM snapshot 摘要与相关策略输入，加入敌人当前招式说明、traits、keywords 或等效基础 enrich 信息。

## 4. 补齐验证与联调

- [x] 4.1 更新 unit tests、`tools/validate_mod_bridge.py` 与相关 validation，校验 richer enemy fields 的导出与降级行为。
- [x] 4.2 完成至少一次 live runtime 验证，确认真实战斗中至少一类敌人的 richer enemy fields 可读，且与现有 `intent` / `powers` 语义一致。
