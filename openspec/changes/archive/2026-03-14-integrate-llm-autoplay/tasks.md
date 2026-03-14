## 1. HTTP bridge client

- [x] 1.1 在 `src/sts2_agent/bridge/` 下新增 `HttpGameBridge`，封装 `/health`、`/snapshot`、`/actions`、`/apply` 并实现 `GameBridge` 接口。
- [x] 1.2 把 live bridge 的 JSON payload 映射到现有 `DecisionSnapshot`、`LegalAction`、`ActionResult` 与 `BridgeError` 语义，补齐错误码处理。
- [x] 1.3 为 `HttpGameBridge` 增加单元测试，覆盖连接失败、accepted、stale_decision、illegal_action 等场景。

## 2. Chat completions policy

- [x] 2.1 新增 LLM 配置模型与 `ChatCompletionsPolicy`，支持 `base_url`、`model`、`api_key`、超时和基础采样参数。
- [x] 2.2 实现 prompt 构造与 JSON 响应解析，要求模型输出 `action_id`、`reason`、`halt`，并保留原始响应元数据用于 trace。
- [x] 2.3 为 policy 增加单元测试，覆盖正常 JSON、`halt=true`、超时、非法 JSON 与缺字段响应。

## 3. Live autoplay runner

- [x] 3.1 扩展 `AutoplayOrchestrator` 或相关运行入口，支持 `dry_run`、非法动作本地拦截、模型失败中断与扩展 trace 字段。
- [x] 3.2 新增本地调试脚本（如 `tools/run_llm_autoplay.py`），默认接入 `http://127.0.0.1:8080/v1/chat/completions`，并支持 CLI / 环境变量覆盖。
- [x] 3.3 增加 orchestrator / CLI 级测试，覆盖 dry-run、模型返回非法动作、正常单步执行等核心路径。

## 4. Live validation and docs

- [x] 4.1 更新 `README.md` 或 `docs/`，说明如何配置本地 OpenAI 兼容接口、启动 live autoplay 与查看 trace artifacts。
- [x] 4.2 在真实 STS2 战斗窗口完成至少一次模型驱动的自动打牌冒烟，并记录请求配置、模型输出与状态推进结果。
