## Context

当前 bridge 已经把 cards、powers 与 reward/card preview 的说明文本尽量收敛为 mod 端输出的 canonical `description`，但公共协议里仍保留了 `description_quality`、`description_source`、`description_vars` 等诊断字段。它们原本用于联调 runtime 反射路径、定位模板回退与变量提取问题；随着 mod 端 description 解析职责已经明确下沉，这些字段继续停留在客户端 schema 中，只会让 Python models、LLM payload、fixtures 与 validation 长期背负额外复杂度。

这个变更是一次跨 C# contracts、runtime provider、Python bridge / policy 与 validation 的收敛：既要删掉公共 schema 中的说明诊断字段，又要保证 live 排障能力不倒退。因此设计重点不是“是否保留诊断”，而是“把诊断放到哪里、以什么粒度记录、如何避免刷屏”。

## Goals / Non-Goals

**Goals:**
- 让 cards、powers、card preview 等说明类对象对外只暴露决策真正需要的字段，首选 `description` 与必要的用户向辅助信息。
- 把 `description_quality`、`description_source`、`description_vars` 等说明诊断迁移到 mod 日志，便于在不污染客户端 schema 的前提下继续排障。
- 保持 live runtime 在说明解析失败时 fail-safe：客户端依然拿到可读快照，开发者依然能从日志中看到 fallback 与 unresolved 原因。
- 同步收敛 Python client、LLM payload、fixtures、tests 与 validation，避免残留对旧字段的隐式依赖。

**Non-Goals:**
- 本次不重新设计更高级的结构化牌效协议，例如 `semantic_effects`、长期牌库规划 schema 或怪物机制百科。
- 本次不要求为每一次成功解析都默认输出海量逐条日志；日志策略以排障优先、避免刷屏为原则。
- 本次不解决所有 description 解析质量问题；它只调整公共接口与诊断落点。

## Decisions

### 1. 公共 schema 只保留用户向说明文本，不再暴露解析诊断字段

cards、powers、card preview 等说明类对象对外只保留 canonical `description`，以及仍然直接服务决策的用户向字段（如 `glossary`，若客户端仍需单独消费）。`description_quality`、`description_source`、`description_vars` 从 C# contracts、Python models 与 LLM payload 中移除。

这样做的原因是当前调用方已经不应再参与模板渲染或变量拼接；继续暴露底层解析细节，只会诱导客户端形成新的隐式依赖。若后续确实需要程序可消费的强语义字段，应单独设计正式能力，而不是继续复用 description diagnostics。

备选方案是保留这些字段但标记为 optional/debug-only。该方案的问题是“调试字段”一旦进入公共 schema，就很难阻止上层继续消费，最终仍会形成兼容包袱，因此不选。

### 2. 说明解析 diagnostics 改为“异常常驻日志 + 可选详细日志”

mod 端日志承担说明排障职责：
- 对 `template_fallback`、变量未解析、glossary 规范化失败、反射异常等异常或降级路径，默认写 warning/debug 日志。
- 对完整成功解析的逐条细节，放在显式 debug 开关后，避免 live autoplay 时刷屏。

这样既能满足“通过日志定位问题”，又不会让正常联调被大量成功日志淹没。日志中应带上对象类别、稳定标识、原始模板摘要、最终 `description` 与失败原因，便于直接 grep。

备选方案是继续把 diagnostics 放在 `metadata.text_diagnostics`。该方案虽然便于抓 artifact，但会继续污染客户端响应，也会让策略层有机会误用内部诊断，因此不选。

### 3. validation 与 live artifact 改为校验“精简协议 + 日志可诊断”

测试与验证脚本不再断言公共响应里存在 `description_vars` 或 `description_quality`。取而代之：
- fixture / unit tests 校验 cards、powers、preview 只暴露 canonical `description`；
- mod/runtime tests 校验当解析失败时不会破坏 snapshot；
- live validation 额外记录本次运行对应的日志文件位置，必要时检查其中包含 fallback / unresolved 线索。

这样可以把“协议正确性”与“排障可用性”分开验证，避免测试再次把日志字段提升为公共契约。

## Risks / Trade-offs

- [现有 Python/LLM 代码仍引用旧字段] → 在同一变更里同步更新 models、policy、fixtures、tests 与 validation，避免出现半收敛状态。
- [日志不足以定位问题] → 统一日志键与对象标识格式，保证最少包含对象类型、稳定 id、原始模板摘要、最终 description、降级原因。
- [详细日志导致刷屏或影响 live 调试体验] → 仅对降级/失败路径默认输出；成功路径放到显式 debug 开关。
- [未来需要结构化语义时又得重新加字段] → 明确把本次删除定义为“移除 description diagnostics”，而不是否定未来新增正式 `semantic_effects` 能力。
