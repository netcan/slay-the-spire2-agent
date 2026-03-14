## Context

当前仓库已经具备三块基础能力：一是 Python 侧的 `Policy` / `AutoplayOrchestrator` / trace 结构，二是 C# mod 暴露的本地 HTTP bridge，三是真实游戏内可成功执行的 `POST /apply`。但 Python 侧还没有面向真实 bridge 的 `GameBridge` 实现，也没有可接 OpenAI 兼容接口的大模型策略，因此“live state -> 模型决策 -> bridge 执行”的完整回路尚未接通。

本次设计面向一个最小但可调试的闭环：使用你给出的 `http://127.0.0.1:8080/v1/chat/completions` 作为默认模型入口，在 Python 侧新增 `HttpGameBridge`、`ChatCompletionsPolicy` 与 live autoplay 运行脚本，先实现单局、单线程、非流式的自动出牌原型。

## Goals / Non-Goals

**Goals:**
- 提供一个 `HttpGameBridge`，把现有本地 HTTP bridge 封装为 `GameBridge` 实现，复用现有 orchestrator。
- 提供一个 OpenAI 兼容 `chat/completions` 的 LLM policy，输出严格受限于当前 legal actions。
- 提供一个可直接本地调试的 autoplay 入口，默认支持 `http://127.0.0.1:8080/v1/chat/completions`。
- 为每一步决策落盘 trace，至少包含 snapshot、legal actions、prompt 摘要、raw response、最终动作与 bridge 回执。
- 在模型输出非法、响应不可解析、桥接失败时，返回明确错误并安全中断，而不是盲目提交未知动作。

**Non-Goals:**
- 不在本次 change 中实现多模型仲裁、树搜索、长程规划或 deck-building 策略优化。
- 不扩展 mod bridge 协议本身；本次优先复用已有 `/health`、`/snapshot`、`/actions`、`/apply`。
- 不引入流式响应、函数调用或多轮 tool calling；先采用非流式、单请求单决策模式。
- 不覆盖所有 phase 的复杂高阶策略，只要求当前 legal actions 中能稳定选出一个动作或显式 halt。

## Decisions

### 1. 复用 `Policy` / `AutoplayOrchestrator` 抽象，而不是单独再写一套 LLM loop

沿用 `src/sts2_agent/policy/base.py` 与 `src/sts2_agent/orchestrator.py`，让 LLM 决策实现 `Policy` 协议，避免把“模型如何选动作”和“回合如何推进”混在一起。这样现有 heuristic policy、trace 结构和超时控制都能继续复用。

备选方案：
- 直接新增独立 `run_live_llm_loop()`：实现快，但会重复 session、trace、停止条件和错误处理逻辑。
- 在 orchestrator 内硬编码 LLM 请求：耦合过高，后续难以切换 heuristic / scripted / LLM policy。

### 2. 新增 `HttpGameBridge` 适配当前本地 bridge，而不是让 orchestrator 直接发 HTTP

仓库当前只有 `MockGameBridge`，没有真实 bridge client。新增 `HttpGameBridge` 后，Python 侧所有 autoplay / validate 工具都能复用同一套错误映射和 payload 解析逻辑。`attach_or_start()` 在 live bridge 场景下采用“附着当前 bridge”语义，返回本地生成的 `session_id`，并在后续调用中校验 bridge 健康状态。

备选方案：
- 在脚本中直接请求 `/snapshot`、`/actions`、`/apply`：短期可用，但无法复用 orchestrator 和现有 `GameBridge` 抽象。

### 3. 模型输出采用“受限 JSON 合约”，优先返回 `action_id`

向模型提供结构化 `snapshot` 摘要和完整 legal actions 列表，要求模型只返回 JSON，例如：

```json
{
  "action_id": "act-xxxx",
  "reason": "简短原因",
  "halt": false
}
```

执行层只接受当前 legal set 中存在的 `action_id`。若模型返回其他字段、自然语言或非法动作，先进行本地校验；首次失败可在同一 decision 上重试一次，仍失败则中断。

备选方案：
- 让模型返回自然语言再做模糊匹配：容易误选动作，尤其同名卡、同类地图节点都存在歧义。
- 依赖 provider 的 function calling：并非所有 OpenAI 兼容服务都稳定支持，先避免额外兼容层。

### 4. Prompt 保持“短系统提示 + 精简局面 JSON + legal actions JSON”

系统提示明确三件事：只能从 legal actions 里选、必须返回 JSON、优先保守与可执行。用户提示则传入 phase、玩家/敌人简表、可行动作数组。为了减少 token 与解析复杂度，不直接传整份原始 snapshot，而是生成一份面向决策的精简视图。

备选方案：
- 直接传完整 `/snapshot` 原文：实现简单，但噪声高，且不同 phase 字段并不稳定。
- 手工拼接纯文本：可读性好，但更难测试与程序校验。

### 5. Trace 扩展为“决策证据”而不是只记最终 action

现有 `TraceEntry` 已能记录 observation、legal actions、policy_output 和 bridge_result。本次在 `policy_output.metadata` 中补充 `provider`, `model`, `request_payload_summary`, `raw_response_text`, `parse_status` 等关键信息，避免另起一套 trace 格式。必要时新增单独 artifact 目录保存完整请求/响应副本。

### 6. CLI 入口采用独立脚本，默认指向本地兼容接口

新增类似 `tools/run_llm_autoplay.py` 的脚本，负责读取 CLI 参数 / 环境变量，构造 `HttpGameBridge` 和 `ChatCompletionsPolicy`，再调用 orchestrator。默认 `base_url` 使用 `http://127.0.0.1:8080/v1`，允许通过参数覆盖 `model`、`api_key`、`max_steps`、`dry_run`、`trace_dir`。

## Risks / Trade-offs

- [模型输出不稳定，偶尔返回非 JSON] → 使用明确输出合约、本地 JSON 解析校验，并允许单次重试后中断。
- [合法动作很多时 prompt 体积膨胀] → 先用精简 snapshot 视图和必要字段；若后续仍超长，再引入 phase-specific summarizer。
- [本地兼容接口与 OpenAI 正式协议存在细微差异] → 仅依赖最小公共字段：`model`、`messages`、`temperature`、`max_tokens`、`choices[0].message.content`。
- [live bridge 没有真实“新建 session”语义] → 在 `HttpGameBridge` 内显式文档化 attach-only 语义，并把 scenario 参数视为保留字段。
- [模型选出 technically legal 但策略很差的动作] → 本次接受策略质量有限，先保证闭环稳定与 trace 可复盘。

## Migration Plan

1. 先在 Python 侧落地 `HttpGameBridge`，并用 fixture / 假响应测试错误映射。
2. 再实现 `ChatCompletionsPolicy` 与 prompt/response 解析。
3. 用 mock bridge + fake LLM 响应补齐 orchestrator 级测试。
4. 增加 live CLI 脚本，默认连接 `http://127.0.0.1:8080/v1/chat/completions`。
5. 在真实 STS2 战斗内完成至少一次模型驱动的自动出牌冒烟。

回滚方式：
- 若模型接入不稳定，可只停用新脚本与 LLM policy，不影响现有 mod bridge 和 live apply 验证链路。

## Open Questions

- 默认 prompt 是否需要针对 `combat` / `reward` / `map` 分 phase 定制，还是先共用一版模板。
- 本地模型接口是否需要 `api_key`，若不需要，CLI 是否默认允许空值。
- 首版是否允许模型显式返回 `halt=true` 作为“交还人工”的安全出口，还是仅在解析失败时中断。
