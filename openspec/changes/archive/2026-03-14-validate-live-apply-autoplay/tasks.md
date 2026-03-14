## 1. 验证脚本与安全护栏

- [x] 1.1 盘点现有 `tools/debug_sts2_mod.py` 与 `tools/validate_mod_bridge.py`，确定 live apply 验证脚本的入口、参数和复用点。
- [x] 1.2 实现 discovery 模式：读取 `health`、`snapshot`、`actions`，筛选候选动作并输出未执行原因。
- [x] 1.3 实现 apply 模式的安全校验，仅在显式开启写入能力时允许发送真实 `POST /apply` 请求。

## 2. 状态推进验证与 artifacts

- [x] 2.1 实现候选动作选择策略，优先选择低风险且结构化字段完整的 live 动作。
- [x] 2.2 在 `POST /apply` 后轮询 live 状态，判定 `decision_id`、phase 或 legal actions 是否发生可观察推进。
- [x] 2.3 为每次验证输出 UTF-8 无 BOM 的结构化 artifacts，至少包含前后快照、请求回执与最终结论。

## 3. 文档与真实联调

- [x] 3.1 补充 README 或调试文档，说明如何启动可写 bridge、如何运行 discovery/apply 验证以及如何解读结果。
- [x] 3.2 在真实 STS2 环境完成至少一次 live `POST /apply` 冒烟验证，并记录实际验证结果或失败诊断。
