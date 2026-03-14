## Why

当前仓库已经打通了 STS2 的真实状态读取、合法动作枚举与 `POST /apply` 写入链路，下一步最自然的扩展就是把外部大模型接进来，验证“模型看局面 -> 选择动作 -> bridge 执行 -> 状态推进”的完整自动打牌闭环。现在已经有一个可调试的 OpenAI 兼容接口 `http://127.0.0.1:8080/v1/chat/completions`，适合先落地最小可用接入能力。

## What Changes

- 新增一个面向 OpenAI 兼容 `chat/completions` 的 LLM provider，支持自定义 `base_url`、`model`、`api_key`、超时和基础采样参数。
- 新增一个 Python 侧 `http-game-bridge-client`，把现有本地 HTTP bridge 封装成 `GameBridge` 实现，供 autoplay 与联调脚本复用。
- 新增一个 live autoplay runner，把 `/snapshot`、`/actions` 组装为模型输入，解析模型输出的 `action_id` 或等效动作选择，并调用 bridge 执行。
- 为 LLM 自动打牌增加最小安全约束，包括只允许当前 legal actions、单步重试/失败回退、dry-run 与 trace 落盘。
- 提供本地调试命令，优先支持你给出的 `http://127.0.0.1:8080/v1/chat/completions` 接口完成端到端联调。

## Capabilities

### New Capabilities
- `chat-completions-llm-provider`: 定义 OpenAI 兼容 chat completions 模型接入、请求格式、响应解析与错误处理约束。
- `http-game-bridge-client`: 定义 Python 侧对 `/health`、`/snapshot`、`/actions`、`/apply` 的封装、错误映射与会话行为。
- `llm-autoplay-runner`: 定义基于 live bridge 的自动打牌循环、动作校验、执行结果追踪与安全回退行为。

### Modified Capabilities

## Impact

- Python 侧将新增 HTTP bridge client、LLM client、prompt 组装、响应解析、autoplay runner 与配置模型。
- CLI / 调试脚本会增加面向本地模型接口的运行入口与 trace artifacts。
- 依赖现有 `GET /snapshot`、`GET /actions`、`POST /apply` 协议，但不要求修改当前 mod bridge 合约。
