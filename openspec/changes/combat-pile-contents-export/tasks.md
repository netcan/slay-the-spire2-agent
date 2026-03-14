## 1. 扩展 mod contracts 与导出结构

- [ ] 1.1 更新 C# runtime/public contracts，在 `player` 下新增 `draw_pile_cards`、`discard_pile_cards`、`exhaust_pile_cards` 或等效字段，并保持现有 pile 计数字段不变。
- [ ] 1.2 更新 window extractors 与 fixture provider，确保 pile contents 能进入 `snapshot` 与相关测试场景。

## 2. 实现 live runtime pile 读取

- [ ] 2.1 在 `Sts2RuntimeReflectionReader` 中补充 draw/discard/exhaust 三类 pile 的卡牌提取逻辑，复用现有 card description / glossary 解析链路。
- [ ] 2.2 为单个 pile 读取失败设计独立降级与 metadata diagnostics，避免整个 combat snapshot 因 pile 解析问题失败。

## 3. 更新 Python 侧消费链路

- [ ] 3.1 更新 `src/sts2_agent/models.py`、HTTP/mock bridge 与 fixtures，使 Python 侧可以解析 pile contents。
- [ ] 3.2 调整 LLM snapshot 摘要与相关策略输入，加入 draw/discard/exhaust pile contents 的基础卡牌信息。

## 4. 补齐验证与联调

- [ ] 4.1 更新 unit tests、`tools/validate_mod_bridge.py` 与相关 validation，校验 pile 计数和 pile contents 同步导出。
- [ ] 4.2 完成至少一次 live runtime 验证，确认真实战斗中抽牌堆、弃牌堆、消耗堆内容可读，且与 hand / pile count 语义一致。
